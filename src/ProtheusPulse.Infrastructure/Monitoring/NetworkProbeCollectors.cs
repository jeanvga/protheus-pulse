using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Domain.Monitoring;

namespace ProtheusPulse.Infrastructure.Monitoring;

public sealed class TcpProbeCollector(IClock clock) : IProbeCollector
{
    public ProbeType Type => ProbeType.Tcp;

    public bool CanCollect(Component component) => component.TcpChecks.Count > 0;

    public async Task<ProbeObservation> CollectAsync(Component component, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var observations = new List<CollectorSupport.TargetObservation>();
        var latencies = new List<double>();
        foreach (var check in component.TcpChecks)
        {
            var targetStopwatch = Stopwatch.StartNew();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(check.TimeoutMs));
            try
            {
                await using var stream = await SafeNetworkConnector.ConnectStreamAsync(check.Host, check.Port, timeout.Token);
                targetStopwatch.Stop();
                latencies.Add(targetStopwatch.Elapsed.TotalMilliseconds);
                observations.Add(new CollectorSupport.TargetObservation(HealthStatus.Healthy, check.IsRequired));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                observations.Add(new CollectorSupport.TargetObservation(HealthStatus.Critical, check.IsRequired));
            }
            catch (Exception exception) when (exception is IOException or SocketException or InvalidOperationException)
            {
                observations.Add(new CollectorSupport.TargetObservation(HealthStatus.Critical, check.IsRequired));
            }
        }

        IReadOnlyList<MetricObservation>? metrics = latencies.Count == 0
            ? null
            : [new MetricObservation("latency", Math.Round(latencies.Average(), 1), "ms")];
        return CollectorSupport.CreateObservation(
            stopwatch, observations, clock.UtcNow,
            "Todos os destinos TCP responderam.",
            "Um destino TCP opcional não respondeu.",
            "Ao menos um destino TCP obrigatório não respondeu.",
            "Não foi possível determinar a conectividade TCP.",
            metrics);
    }
}

public sealed class HttpProbeCollector : IProbeCollector, IDisposable
{
    private const int MaximumBodyBytes = 64 * 1024;
    private readonly IClock clock;
    private readonly HttpClient client;

    public HttpProbeCollector(IClock clock)
    {
        this.clock = clock;
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectCallback = static (context, cancellationToken) =>
                new ValueTask<Stream>(SafeNetworkConnector.ConnectStreamAsync(
                    context.DnsEndPoint.Host,
                    context.DnsEndPoint.Port,
                    cancellationToken))
        };
        client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    public ProbeType Type => ProbeType.Http;

    public bool CanCollect(Component component) => component.HttpChecks.Count > 0;

    public async Task<ProbeObservation> CollectAsync(Component component, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var observations = new List<CollectorSupport.TargetObservation>();
        var latencies = new List<double>();
        foreach (var check in component.HttpChecks)
        {
            var targetStopwatch = Stopwatch.StartNew();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(check.TimeoutMs));
            try
            {
                using var request = new HttpRequestMessage(
                    string.Equals(check.Method, "HEAD", StringComparison.OrdinalIgnoreCase) ? HttpMethod.Head : HttpMethod.Get,
                    check.Url);
                request.Headers.UserAgent.ParseAdd("ProtheusPulse/0.1");
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                var statusCode = (int)response.StatusCode;
                var statusMatches = statusCode >= check.ExpectedStatusMin && statusCode <= check.ExpectedStatusMax;
                var bodyMatches = string.IsNullOrEmpty(check.BodyPattern)
                    || await ContainsBodyPatternAsync(response.Content, check.BodyPattern, timeout.Token);
                targetStopwatch.Stop();
                latencies.Add(targetStopwatch.Elapsed.TotalMilliseconds);
                observations.Add(new CollectorSupport.TargetObservation(
                    statusMatches && bodyMatches ? HealthStatus.Healthy : HealthStatus.Critical,
                    check.IsRequired));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                observations.Add(new CollectorSupport.TargetObservation(HealthStatus.Critical, check.IsRequired));
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException)
            {
                observations.Add(new CollectorSupport.TargetObservation(HealthStatus.Critical, check.IsRequired));
            }
        }

        IReadOnlyList<MetricObservation>? metrics = latencies.Count == 0
            ? null
            : [new MetricObservation("latency", Math.Round(latencies.Average(), 1), "ms")];
        return CollectorSupport.CreateObservation(
            stopwatch, observations, clock.UtcNow,
            "Todos os endpoints HTTP responderam conforme esperado.",
            "Um endpoint HTTP opcional não respondeu conforme esperado.",
            "Ao menos um endpoint HTTP obrigatório falhou.",
            "Não foi possível determinar o estado dos endpoints HTTP.",
            metrics);
    }

    public void Dispose()
    {
        client.Dispose();
        GC.SuppressFinalize(this);
    }

    private static async Task<bool> ContainsBodyPatternAsync(
        HttpContent content,
        string pattern,
        CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[MaximumBodyBytes];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        var body = Encoding.UTF8.GetString(buffer, 0, total);
        return body.Contains(pattern, StringComparison.Ordinal);
    }
}

public sealed class TlsCertificateProbeCollector(IClock clock) : IProbeCollector
{
    public ProbeType Type => ProbeType.TlsCertificate;

    public bool CanCollect(Component component) => component.HttpChecks.Any(IsHttps);

    public async Task<ProbeObservation> CollectAsync(Component component, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var observations = new List<CollectorSupport.TargetObservation>();
        var remainingDays = new List<double>();
        foreach (var check in component.HttpChecks.Where(IsHttps))
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(check.TimeoutMs));
            try
            {
                var uri = new Uri(check.Url, UriKind.Absolute);
                await using var networkStream = await SafeNetworkConnector.ConnectStreamAsync(
                    uri.Host,
                    uri.IsDefaultPort ? 443 : uri.Port,
                    timeout.Token);
                X509Certificate2? remoteCertificate = null;
                using var tlsStream = new SslStream(
                    networkStream,
                    leaveInnerStreamOpen: false,
                    (_, certificate, _, errors) =>
                    {
                        if (certificate is not null)
                        {
                            remoteCertificate = new X509Certificate2(certificate);
                        }

                        return !check.ValidateTls || errors == SslPolicyErrors.None;
                    });
                await tlsStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = uri.Host,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                }, timeout.Token);

                using (remoteCertificate)
                {
                    if (remoteCertificate is null)
                    {
                        observations.Add(new CollectorSupport.TargetObservation(HealthStatus.Unknown, check.IsRequired));
                        continue;
                    }

                    var days = (remoteCertificate.NotAfter.ToUniversalTime() - clock.UtcNow.UtcDateTime).TotalDays;
                    remainingDays.Add(days);
                    var status = days <= 0
                        ? HealthStatus.Critical
                        : days <= check.CertificateWarningDays
                            ? HealthStatus.Warning
                            : HealthStatus.Healthy;
                    observations.Add(new CollectorSupport.TargetObservation(status, check.IsRequired));
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                observations.Add(new CollectorSupport.TargetObservation(HealthStatus.Critical, check.IsRequired));
            }
            catch (Exception exception) when (exception is AuthenticationException or IOException or SocketException or InvalidOperationException)
            {
                observations.Add(new CollectorSupport.TargetObservation(HealthStatus.Critical, check.IsRequired));
            }
        }

        IReadOnlyList<MetricObservation>? metrics = remainingDays.Count == 0
            ? null
            : [new MetricObservation("certificateDays", Math.Floor(remainingDays.Min()), "dias")];
        return CollectorSupport.CreateObservation(
            stopwatch, observations, clock.UtcNow,
            "Certificados TLS dentro da validade configurada.",
            "Um certificado TLS está próximo do vencimento.",
            "Um certificado TLS expirou ou não pôde ser validado.",
            "Não foi possível determinar a validade dos certificados TLS.",
            metrics);
    }

    private static bool IsHttps(HttpCheck check) =>
        Uri.TryCreate(check.Url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}
