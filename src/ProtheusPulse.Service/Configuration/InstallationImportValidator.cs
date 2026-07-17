using ProtheusPulse.Domain.Monitoring;

namespace ProtheusPulse.Service.Configuration;

public static class InstallationImportValidator
{
    private const int MaximumInstallations = 20;
    private const int MaximumComponents = 50;
    private const int MaximumChecksPerComponent = 20;
    private const int MaximumTags = 20;

    public static ValidationResult Validate(
        InstallationImportDocument document,
        IReadOnlyCollection<string> existingInstallationNames)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        if (document.SchemaVersion != 1)
        {
            errors.Add("schemaVersion deve ser igual a 1.");
        }

        if (document.Installations is null || document.Installations.Count == 0)
        {
            errors.Add("Informe ao menos uma instalação.");
            return new ValidationResult(errors, warnings);
        }

        if (document.Installations.Count > MaximumInstallations)
        {
            errors.Add($"Informe no máximo {MaximumInstallations} instalações por importação.");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var installationIndex = 0; installationIndex < document.Installations.Count; installationIndex++)
        {
            var installation = document.Installations[installationIndex];
            var prefix = $"installations[{installationIndex}]";
            ValidateInstallation(installation, prefix, errors, warnings);

            var name = installation.Name?.Trim();
            if (!string.IsNullOrEmpty(name))
            {
                if (!names.Add(name))
                {
                    errors.Add($"{prefix}.name está duplicado no arquivo.");
                }

                if (existingInstallationNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    errors.Add($"{prefix}.name já existe no Pulse.");
                }
            }
        }

        return new ValidationResult(errors.Take(100).ToArray(), warnings.Take(100).ToArray());
    }

    private static void ValidateInstallation(
        InstallationDefinition installation,
        string prefix,
        List<string> errors,
        List<string> warnings)
    {
        ValidateText(installation.Name, 160, $"{prefix}.name", errors);
        if (!TryParseEnum<EnvironmentKind>(installation.Environment, out var environment))
        {
            errors.Add($"{prefix}.environment deve ser Production, Homologation, Development ou Custom.");
        }
        else if (environment == EnvironmentKind.Custom)
        {
            ValidateText(installation.CustomEnvironmentName, 80, $"{prefix}.customEnvironmentName", errors);
        }

        if (installation.Tags is { Count: > MaximumTags })
        {
            errors.Add($"{prefix}.tags deve possuir no máximo {MaximumTags} itens.");
        }

        if (installation.Tags is not null)
        {
            for (var index = 0; index < installation.Tags.Count; index++)
            {
                ValidateText(installation.Tags[index], 40, $"{prefix}.tags[{index}]", errors);
            }
        }

        if (installation.Components is null || installation.Components.Count == 0)
        {
            errors.Add($"{prefix}.components deve possuir ao menos um componente.");
            return;
        }

        if (installation.Components.Count > MaximumComponents)
        {
            errors.Add($"{prefix}.components deve possuir no máximo {MaximumComponents} itens.");
        }

        var componentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < installation.Components.Count; index++)
        {
            var component = installation.Components[index];
            var componentPrefix = $"{prefix}.components[{index}]";
            if (component is null)
            {
                errors.Add($"{componentPrefix} não pode ser nulo.");
                continue;
            }

            ValidateComponent(component, componentPrefix, errors, warnings);
            var componentName = component.Name?.Trim();
            if (!string.IsNullOrEmpty(componentName) && !componentNames.Add(componentName))
            {
                errors.Add($"{componentPrefix}.name está duplicado na instalação.");
            }
        }
    }

    private static void ValidateComponent(
        ComponentDefinition component,
        string prefix,
        List<string> errors,
        List<string> warnings)
    {
        ValidateText(component.Name, 160, $"{prefix}.name", errors);
        if (!TryParseEnum<ComponentType>(component.Type, out _))
        {
            errors.Add($"{prefix}.type não é um tipo de componente válido.");
        }

        if (component.WindowsServiceName is not null)
        {
            ValidateText(component.WindowsServiceName, 256, $"{prefix}.windowsServiceName", errors);
            if (component.WindowsServiceName.IndexOfAny(['\\', '/', ':']) >= 0)
            {
                errors.Add($"{prefix}.windowsServiceName não pode conter separadores de caminho.");
            }
        }

        ValidateOptionalPath(component.ExecutablePath, $"{prefix}.executablePath", errors);
        ValidateOptionalPath(component.IniPath, $"{prefix}.iniPath", errors);
        ValidatePaths(component.LogPaths, $"{prefix}.logPaths", errors);

        if (component.TcpChecks is { Count: > MaximumChecksPerComponent })
        {
            errors.Add($"{prefix}.tcpChecks deve possuir no máximo {MaximumChecksPerComponent} itens.");
        }

        if (component.TcpChecks is not null)
        {
            for (var index = 0; index < component.TcpChecks.Count; index++)
            {
                ValidateTcp(component.TcpChecks[index], $"{prefix}.tcpChecks[{index}]", errors);
            }
        }

        if (component.HttpChecks is { Count: > MaximumChecksPerComponent })
        {
            errors.Add($"{prefix}.httpChecks deve possuir no máximo {MaximumChecksPerComponent} itens.");
        }

        if (component.HttpChecks is not null)
        {
            for (var index = 0; index < component.HttpChecks.Count; index++)
            {
                ValidateHttp(component.HttpChecks[index], $"{prefix}.httpChecks[{index}]", errors);
            }
        }

        var hasTarget = component.WindowsServiceName is not null
            || component.ExecutablePath is not null
            || component.IniPath is not null
            || component.LogPaths is { Count: > 0 }
            || component.TcpChecks is { Count: > 0 }
            || component.HttpChecks is { Count: > 0 };
        if (!hasTarget)
        {
            warnings.Add($"{prefix} não possui alvo de coleta e permanecerá Unknown.");
        }
    }

    private static void ValidateTcp(TcpCheckDefinition? check, string prefix, List<string> errors)
    {
        if (check is null)
        {
            errors.Add($"{prefix} não pode ser nulo.");
            return;
        }

        ValidateText(check.Host, 253, $"{prefix}.host", errors);
        if (check.Host is not null && (check.Host.Any(char.IsWhiteSpace) || check.Host.IndexOfAny(['/', '\\']) >= 0 || check.Host.Contains("://", StringComparison.Ordinal)))
        {
            errors.Add($"{prefix}.host não é um host válido.");
        }

        if (check.Port is < 1 or > 65_535)
        {
            errors.Add($"{prefix}.port deve estar entre 1 e 65535.");
        }

        if (check.TimeoutMs is < 250 or > 30_000)
        {
            errors.Add($"{prefix}.timeoutMs deve estar entre 250 e 30000.");
        }
    }

    private static void ValidateHttp(HttpCheckDefinition? check, string prefix, List<string> errors)
    {
        if (check is null)
        {
            errors.Add($"{prefix} não pode ser nulo.");
            return;
        }

        if (!Uri.TryCreate(check.Url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || (check.Url?.Length ?? 0) > 2_048)
        {
            errors.Add($"{prefix}.url deve ser uma URL HTTP/HTTPS absoluta, sem credenciais embutidas.");
        }

        if (!string.Equals(check.Method, "GET", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(check.Method, "HEAD", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{prefix}.method deve ser GET ou HEAD.");
        }

        if (check.ExpectedStatusMin is < 100 or > 599
            || check.ExpectedStatusMax is < 100 or > 599
            || check.ExpectedStatusMin > check.ExpectedStatusMax)
        {
            errors.Add($"{prefix} possui faixa de status HTTP inválida.");
        }

        if (check.TimeoutMs is < 250 or > 30_000)
        {
            errors.Add($"{prefix}.timeoutMs deve estar entre 250 e 30000.");
        }

        if (check.CertificateWarningDays is < 1 or > 365)
        {
            errors.Add($"{prefix}.certificateWarningDays deve estar entre 1 e 365.");
        }

        if (check.BodyPattern is not null && !IsValidText(check.BodyPattern, 500))
        {
            errors.Add($"{prefix}.bodyPattern deve possuir no máximo 500 caracteres sem controles.");
        }
    }

    private static void ValidatePaths(IReadOnlyList<string?>? paths, string prefix, List<string> errors)
    {
        if (paths is null)
        {
            return;
        }

        if (paths.Count > MaximumChecksPerComponent)
        {
            errors.Add($"{prefix} deve possuir no máximo {MaximumChecksPerComponent} itens.");
        }

        for (var index = 0; index < paths.Count; index++)
        {
            ValidateOptionalPath(paths[index], $"{prefix}[{index}]", errors, required: true);
        }
    }

    private static void ValidateOptionalPath(string? path, string field, List<string> errors, bool required = false)
    {
        if (path is null && !required)
        {
            return;
        }

        if (!IsSafeConfiguredPath(path))
        {
            errors.Add($"{field} deve ser um caminho absoluto Windows/UNC, sem segmentos '..' ou prefixos de dispositivo.");
        }
    }

    private static bool IsSafeConfiguredPath(string? value)
    {
        if (!IsValidText(value, 2_048))
        {
            return false;
        }

        var path = value!.Trim();
        if (path.StartsWith("\\\\?\\", StringComparison.Ordinal) || path.StartsWith("\\\\.\\", StringComparison.Ordinal))
        {
            return false;
        }

        var segments = path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment == ".."))
        {
            return false;
        }

        var driveRooted = path.Length >= 3
            && char.IsAsciiLetter(path[0])
            && path[1] == ':'
            && (path[2] == '\\' || path[2] == '/');
        var uncRooted = path.StartsWith("\\\\", StringComparison.Ordinal) && segments.Length >= 3;
        return driveRooted || uncRooted;
    }

    private static bool TryParseEnum<TEnum>(string? value, out TEnum result)
        where TEnum : struct, Enum =>
        Enum.TryParse(value, ignoreCase: true, out result) && Enum.IsDefined(result);

    private static void ValidateText(string? value, int maximumLength, string field, List<string> errors)
    {
        if (!IsValidText(value, maximumLength))
        {
            errors.Add($"{field} é obrigatório, deve possuir no máximo {maximumLength} caracteres e não pode conter controles.");
        }
    }

    private static bool IsValidText(string? value, int maximumLength)
    {
        var trimmed = value?.Trim();
        return !string.IsNullOrEmpty(trimmed)
            && trimmed.Length <= maximumLength
            && !trimmed.Any(char.IsControl);
    }

    public sealed record ValidationResult(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
    {
        public bool IsValid => Errors.Count == 0;
    }
}
