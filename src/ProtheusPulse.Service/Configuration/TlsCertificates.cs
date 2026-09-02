using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace ProtheusPulse.Service.Configuration;

/// <summary>
/// Certificado que o painel usa para servir HTTPS. O arquivo PFX fica onde o
/// administrador indicar; a senha nunca entra no <c>network.json</c>, que é texto puro
/// lido pela configuração — ela vai protegida com Data Protection em arquivo separado.
/// </summary>
public static class TlsCertificates
{
    public const string PasswordFileName = "certificate-password.dat";
    private const string ProtectorPurpose = "ProtheusPulse.TlsCertificate.v1";

    public static IDataProtector CreateProtector(IDataProtectionProvider provider) =>
        provider.CreateProtector(ProtectorPurpose);

    public static async Task SavePasswordAsync(string dataDirectory, IDataProtector protector, string? password, CancellationToken cancellationToken)
    {
        var path = Path.Combine(dataDirectory, PasswordFileName);
        if (string.IsNullOrEmpty(password))
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return;
        }

        await File.WriteAllTextAsync(path, protector.Protect(password), cancellationToken);
    }

    /// <summary>Devolve a senha guardada, ou <c>null</c> quando não há nenhuma ou ela não decifra.</summary>
    public static string? ReadPassword(string dataDirectory, IDataProtector protector)
    {
        var path = Path.Combine(dataDirectory, PasswordFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return protector.Unprotect(File.ReadAllText(path));
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Carrega o certificado para conferir que serve antes de o serviço depender dele.
    /// Um PFX sem chave privada sobe o Kestrel e derruba todo handshake.
    /// </summary>
    public static CertificateCheck Inspect(string? path, string? password)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new CertificateCheck(false, "Informe o caminho do arquivo .pfx do certificado.", null, null);
        }

        if (!File.Exists(path))
        {
            return new CertificateCheck(false, $"O arquivo {path} não foi encontrado pelo serviço.", null, null);
        }

        try
        {
            // Sem gravar a chave em disco onde a plataforma permite; o macOS, usado só em
            // desenvolvimento, não suporta EphemeralKeySet e cai no comportamento padrão.
            var flags = OperatingSystem.IsMacOS()
                ? X509KeyStorageFlags.DefaultKeySet
                : X509KeyStorageFlags.EphemeralKeySet;
            using var certificate = new X509Certificate2(path, NormalizePassword(password), flags);
            if (!certificate.HasPrivateKey)
            {
                return new CertificateCheck(false, "O arquivo não traz a chave privada; exporte o certificado com a chave.", null, null);
            }

            return new CertificateCheck(true, null, certificate.Subject, certificate.NotAfter);
        }
        catch (Exception exception) when (exception is CryptographicException or PlatformNotSupportedException or IOException)
        {
            return new CertificateCheck(false, $"Não foi possível abrir o certificado: {exception.Message}", null, null);
        }
    }

    /// <summary>
    /// Gera um certificado próprio para a máquina. O navegador avisa que não conhece
    /// quem assinou, mas o tráfego deixa de ir em texto claro — que é o problema quando
    /// o painel é aberto de outro computador.
    /// </summary>
    public static SelfSignedResult CreateSelfSigned(string dataDirectory, string? password)
    {
        var hostName = Environment.MachineName;
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={hostName}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], true));

        var alternativeNames = new SubjectAlternativeNameBuilder();
        alternativeNames.AddDnsName(hostName);
        alternativeNames.AddDnsName("localhost");
        alternativeNames.AddIpAddress(IPAddress.Loopback);
        foreach (var address in LocalAddresses())
        {
            alternativeNames.AddIpAddress(address);
        }

        request.CertificateExtensions.Add(alternativeNames.Build());

        var now = DateTimeOffset.UtcNow;
        using var certificate = request.CreateSelfSigned(now.AddDays(-1), now.AddYears(2));
        var directory = Path.Combine(dataDirectory, "certs");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "pulse-self-signed.pfx");
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, NormalizePassword(password)));
        return new SelfSignedResult(path, certificate.Subject, certificate.NotAfter);
    }

    /// <summary>
    /// Exportar um PFX com senha vazia e reabri-lo com senha vazia falha a verificação de
    /// MAC no OpenSSL: sem senha é <c>null</c> dos dois lados, não string vazia.
    /// </summary>
    private static string? NormalizePassword(string? password) =>
        string.IsNullOrEmpty(password) ? null : password;

    private static IEnumerable<IPAddress> LocalAddresses()
    {
        IEnumerable<IPAddress> addresses;
        try
        {
            addresses = NetworkInterface.GetAllNetworkInterfaces()
                .Where(item => item.OperationalStatus == OperationalStatus.Up
                    && item.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(item => item.GetIPProperties().UnicastAddresses)
                .Select(item => item.Address)
                .Where(item => item.AddressFamily == AddressFamily.InterNetwork)
                .Distinct()
                .Take(8)
                .ToArray();
        }
        catch (NetworkInformationException)
        {
            return [];
        }

        return addresses;
    }

    public sealed record CertificateCheck(bool Valid, string? Message, string? Subject, DateTime? NotAfter);

    public sealed record SelfSignedResult(string Path, string Subject, DateTime NotAfter);

    /// <summary>Serialização do <c>network.json</c>, escrita pela tela de configurações.</summary>
    public static readonly JsonSerializerOptions FileOptions = new() { WriteIndented = true };
}
