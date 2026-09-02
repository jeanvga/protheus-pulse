using System.Globalization;
using System.Text;

namespace ProtheusPulse.Infrastructure.Monitoring;

/// <summary>Um alvo de rede que o INI declara, já com o motivo pelo qual ele existe.</summary>
public sealed record IniNetworkTarget(string Label, string Host, int Port, bool IsRequired);

/// <summary>O que um <c>appserver.ini</c> diz sobre o ambiente que ele sobe.</summary>
public sealed record AppServerIniSummary(
    string? EnvironmentName,
    string? WindowsServiceName,
    string? InstallPath,
    string? RootPath,
    string? SourcePath,
    string? RpoVersion,
    string? DatabaseKind,
    string? ConsoleFile,
    bool ConsoleLogEnabled,
    long? ConsoleMaxSizeBytes,
    IReadOnlyList<IniNetworkTarget> Targets,
    IReadOnlyList<string> Jobs);

/// <summary>
/// Leitura do <c>appserver.ini</c> por seção. Varrer o arquivo atrás de qualquer chave que
/// contenha "port" não serve: <c>MultiProtocolPort=1</c> é liga-desliga, não porta. Cada
/// alvo abaixo sai de uma seção conhecida, com o rótulo do que ele é.
/// </summary>
public static class ProtheusAppServerIni
{
    /// <summary>Porta padrão do DBAccess quando o INI não declara outra.</summary>
    public const int DefaultDbAccessPort = 7_890;

    private const int MaximumLines = 5_000;

    public static AppServerIniSummary Parse(string content)
    {
        var sections = ReadSections(content);
        var general = Section(sections, "GENERAL");
        var environmentName = Value(general, "App_Environment");
        var environment = environmentName is not null ? Section(sections, environmentName) : null;
        environment ??= sections.Values.FirstOrDefault(item => item.ContainsKey("ROOTPATH") && item.ContainsKey("STARTPATH"));
        environmentName ??= environment is not null ? Value(environment, "Environment") : null;

        var targets = new List<IniNetworkTarget>();
        if (Port(Section(sections, "TCP"), "Port") is { } appServerPort)
        {
            targets.Add(new IniNetworkTarget("AppServer (TCP)", "127.0.0.1", appServerPort, true));
        }

        if (Port(Section(sections, "WEBAPP"), "Port") is { } webPort)
        {
            targets.Add(new IniNetworkTarget("Portal web", "127.0.0.1", webPort, false));
        }

        foreach (var (name, section) in sections)
        {
            if (name.StartsWith("HTTPREST", StringComparison.Ordinal) && Port(section, "Port") is { } restPort)
            {
                targets.Add(new IniNetworkTarget($"REST ({name})", "127.0.0.1", restPort, false));
            }
        }

        var license = Section(sections, "LICENSECLIENT");
        if (Value(license, "server") is { Length: > 0 } licenseHost && Port(license, "port") is { } licensePort)
        {
            targets.Add(new IniNetworkTarget("License Server", licenseHost, licensePort, true));
        }

        if (environment is not null && Value(environment, "TopServer") is { Length: > 0 } databaseHost)
        {
            targets.Add(new IniNetworkTarget(
                "DBAccess",
                databaseHost,
                Port(environment, "TopPort") ?? DefaultDbAccessPort,
                true));
        }

        // Broker e WebMonitor: balanceador na frente dos AppServers. O INI diz em que porta
        // ele escuta, para quais instâncias distribui e com que nome o serviço foi criado.
        var balance = Section(sections, "BALANCE_HTTP");
        if (Port(balance, "LOCAL_SERVER_PORT") is { } balancePort)
        {
            targets.Add(new IniNetworkTarget("Broker (balanceador)", "127.0.0.1", balancePort, true));
        }

        if (balance is not null)
        {
            foreach (var entry in balance.Where(item => item.Key.StartsWith("REMOTE_SERVER", StringComparison.OrdinalIgnoreCase)))
            {
                var parts = entry.Value.Split([' ', '\t', ':'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2
                    && int.TryParse(parts[1], CultureInfo.InvariantCulture, out var remotePort)
                    && remotePort is > 0 and <= 65_535)
                {
                    targets.Add(new IniNetworkTarget("AppServer balanceado", parts[0], remotePort, false));
                }
            }
        }

        var jobs = (Value(Section(sections, "ONSTART"), "Jobs") ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToArray();

        return new AppServerIniSummary(
            environmentName,
            Value(balance, "SERVICE_NAME"),
            Value(general, "InstallPath"),
            environment is null ? null : Value(environment, "RootPath"),
            environment is null ? null : Value(environment, "SourcePath"),
            environment is null ? null : Value(environment, "RpoVersion"),
            environment is null ? null : Value(environment, "TopDataBase"),
            Value(general, "ConsoleFile"),
            string.Equals(Value(general, "ConsoleLog"), "1", StringComparison.Ordinal),
            long.TryParse(Value(general, "ConsoleMaxSize"), CultureInfo.InvariantCulture, out var maxSize) ? maxSize : null,
            Deduplicate(targets),
            jobs);
    }

    /// <summary>O INI do Protheus é gravado em CP1252, como o resto do que o AppServer escreve.</summary>
    public static AppServerIniSummary ParseBytes(byte[] content)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        try
        {
            return Parse(strict.GetString(content));
        }
        catch (DecoderFallbackException)
        {
            return Parse(Encoding.GetEncoding(1252).GetString(content));
        }
    }

    private static IniNetworkTarget[] Deduplicate(List<IniNetworkTarget> targets) =>
        targets
            .GroupBy(item => (item.Host, item.Port))
            .Select(group => group.First())
            .ToArray();

    private static Dictionary<string, Dictionary<string, string>> ReadSections(string content)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        sections[string.Empty] = current;
        var lines = 0;
        foreach (var raw in content.Split('\n'))
        {
            if (++lines > MaximumLines)
            {
                break;
            }

            // O INI do Broker vem com BOM: sem tirar, a primeira seção não é reconhecida.
            var line = raw.Trim().TrimStart('\uFEFF').Trim();
            if (line.Length == 0 || line[0] is ';' or '#')
            {
                continue;
            }

            if (line[0] == '[' && line[^1] == ']')
            {
                current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                sections[line[1..^1].Trim()] = current;
                continue;
            }

            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            current[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        return sections;
    }

    private static Dictionary<string, string>? Section(
        Dictionary<string, Dictionary<string, string>> sections,
        string name) => sections.TryGetValue(name, out var section) ? section : null;

    private static string? Value(Dictionary<string, string>? section, string key) =>
        section is not null && section.TryGetValue(key, out var value) && value.Length > 0 ? value : null;

    private static int? Port(Dictionary<string, string>? section, string key) =>
        int.TryParse(Value(section, key), CultureInfo.InvariantCulture, out var port) && port is > 0 and <= 65_535
            ? port
            : null;
}
