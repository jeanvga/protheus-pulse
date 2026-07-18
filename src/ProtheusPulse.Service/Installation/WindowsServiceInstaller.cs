using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using Microsoft.Win32;

namespace ProtheusPulse.Service.WindowsSetup;

internal static class WindowsServiceInstaller
{
    private const string InstallCommand = "--install-service";
    private const string UninstallCommand = "--uninstall-service";
    private const string DataDirectoryOption = "--data-directory";
    private const string ServiceName = "ProtheusPulse";
    private const string ServiceDisplayName = "Protheus Pulse";
    private const string ServiceAccount = @"NT AUTHORITY\LocalService";
    private const string ServiceRegistryPath = @"SYSTEM\CurrentControlSet\Services\ProtheusPulse";
    private const string HealthUrl = "http://127.0.0.1:5058/health/ready";

    public static async Task<int?> TryRunAsync(string[] args)
    {
        var installRequested = args.Any(item => string.Equals(item, InstallCommand, StringComparison.OrdinalIgnoreCase));
        var uninstallRequested = args.Any(item => string.Equals(item, UninstallCommand, StringComparison.OrdinalIgnoreCase));
        if (!installRequested && !uninstallRequested)
        {
            return null;
        }

        if (installRequested && uninstallRequested)
        {
            Console.Error.WriteLine("Informe somente uma operação de serviço por execução.");
            return 2;
        }

        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("A instalação do serviço está disponível somente no Windows.");
            return 2;
        }

        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            ServiceName);
        try
        {
            dataDirectory = ResolveDataDirectory(args);
            EnsureAdministrator();
            if (installRequested)
            {
                await InstallAsync(dataDirectory);
            }
            else
            {
                await UninstallAsync();
            }

            return 0;
        }
        catch (Exception exception)
        {
            var diagnosticPath = await WriteDiagnosticsAsync(dataDirectory, exception);
            Console.Error.WriteLine($"Falha ao {(installRequested ? "instalar" : "remover")} o serviço {ServiceDisplayName}: {exception.Message}");
            Console.Error.WriteLine($"Diagnóstico salvo em: {diagnosticPath}");
            return 1;
        }
    }

    private static string ResolveDataDirectory(string[] args)
    {
        var configuredPath = GetOptionValue(args, DataDirectoryOption)
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), ServiceName);
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            throw new InvalidOperationException("O Windows não informou o diretório de dados compartilhados.");
        }

        var fullPath = Path.GetFullPath(configuredPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var expectedPath = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            ServiceName));
        if (!string.Equals(fullPath, expectedPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"O diretório de dados deve ser {expectedPath}.");
        }

        return fullPath;
    }

    private static string? GetOptionValue(string[] args, string option)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (!string.Equals(args[index], option, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"A opção {option} exige um valor.");
            }

            return args[index + 1];
        }

        return null;
    }

    [SupportedOSPlatform("windows")]
    private static void EnsureAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
        {
            throw new UnauthorizedAccessException("Execute o instalador como administrador.");
        }
    }

    [SupportedOSPlatform("windows")]
    private static async Task InstallAsync(string dataDirectory)
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            throw new InvalidOperationException("Não foi possível localizar o executável do serviço.");
        }

        executablePath = Path.GetFullPath(executablePath);
        if (!string.Equals(Path.GetFileName(executablePath), "ProtheusPulse.Service.exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("O modo de instalação deve ser executado pelo ProtheusPulse.Service.exe publicado.");
        }

        var installDirectory = Path.GetDirectoryName(executablePath)
            ?? throw new InvalidOperationException("Não foi possível localizar o diretório de instalação.");
        var expectedInstallDirectory = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            ServiceDisplayName));
        if (!string.Equals(installDirectory, expectedInstallDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"O executável deve estar instalado em {expectedInstallDirectory}.");
        }

        var secretDirectory = Path.Combine(dataDirectory, "secrets");
        var keysDirectory = Path.Combine(dataDirectory, "keys");
        var logsDirectory = Path.Combine(dataDirectory, "logs");
        var jwtKeyPath = Path.Combine(secretDirectory, "jwt.key");

        await StopServiceAsync();

        Directory.CreateDirectory(dataDirectory);
        Directory.CreateDirectory(secretDirectory);
        Directory.CreateDirectory(keysDirectory);
        Directory.CreateDirectory(logsDirectory);

        await ApplyDirectoryAccessControlAsync(installDirectory, dataDirectory, secretDirectory, keysDirectory);
        ClearDatabaseReadOnlyAttributes(dataDirectory);
        CreateOrValidateJwtKey(jwtKeyPath);
        await ApplyJwtKeyAccessControlAsync(jwtKeyPath);
        await CreateOrUpdateServiceAsync(executablePath, dataDirectory, jwtKeyPath);

        var startResult = await RunScAsync("start", ServiceName);
        startResult.EnsureSuccess("iniciar o serviço", 0, 1056);
        await WaitForServiceStatusAsync(ServiceControllerStatus.Running, TimeSpan.FromSeconds(60));
        await WaitForHealthCheckAsync();

        TryDeleteLegacyReleases(installDirectory);
        Console.WriteLine($"{ServiceDisplayName} instalado e saudável em http://127.0.0.1:5058/.");
        Console.WriteLine($"Dados preservados em: {dataDirectory}");
    }

    [SupportedOSPlatform("windows")]
    private static async Task UninstallAsync()
    {
        if (!ServiceExists())
        {
            Console.WriteLine($"O serviço {ServiceName} já não está instalado.");
            return;
        }

        await StopServiceAsync();
        var deleteResult = await RunScAsync("delete", ServiceName);
        deleteResult.EnsureSuccess("remover o serviço", 0, 1060, 1072);
        Console.WriteLine("Serviço removido. Banco, chaves e logs foram preservados.");
    }

    private static void ClearDatabaseReadOnlyAttributes(string dataDirectory)
    {
        try
        {
            foreach (var databaseFile in Directory.EnumerateFiles(dataDirectory, "pulse.db*", SearchOption.TopDirectoryOnly))
            {
                File.SetAttributes(databaseFile, FileAttributes.Normal);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Aviso: não foi possível normalizar os atributos do banco: {exception.Message}");
        }
    }

    private static void CreateOrValidateJwtKey(string jwtKeyPath)
    {
        if (!File.Exists(jwtKeyPath))
        {
            var randomBytes = RandomNumberGenerator.GetBytes(64);
            try
            {
                File.WriteAllText(jwtKeyPath, Convert.ToBase64String(randomBytes), new UTF8Encoding(false));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(randomBytes);
            }
        }

        var keyFile = new FileInfo(jwtKeyPath);
        if (!keyFile.Exists || keyFile.Length is < 32 or > 1_024 || (keyFile.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("A chave JWT existente é inválida; ela não foi substituída automaticamente.");
        }
    }

    private static async Task ApplyDirectoryAccessControlAsync(
        string installDirectory,
        string dataDirectory,
        string secretDirectory,
        string keysDirectory)
    {
        // Versões antigas podem ter deixado arquivos com dono e ACL que negam
        // acesso administrativo; sem a posse, o icacls abaixo falharia parcialmente.
        var takeOwnership = await RunProcessAsync(
            ResolveSystemExecutable("takeown.exe"),
            "/F",
            dataDirectory,
            "/A",
            "/R",
            "/D",
            "Y");
        takeOwnership.EnsureSuccess("assumir a propriedade administrativa dos dados", 0, 1);

        // /grant:r não remove ACEs explícitas antigas (inclusive Deny); o /reset
        // zera o DACL herdado de qualquer versão anterior antes do estado final.
        await RunIcaclsAsync(dataDirectory, "/reset", "/T", "/C", "/Q");

        await RunIcaclsAsync(
            installDirectory,
            "/grant:r",
            "*S-1-5-19:(OI)(CI)RX",
            "/T",
            "/C",
            "/Q");
        await RunIcaclsAsync(
            dataDirectory,
            "/inheritance:r",
            "/grant:r",
            "*S-1-5-18:(OI)(CI)F",
            "*S-1-5-32-544:(OI)(CI)F",
            "*S-1-5-19:(OI)(CI)M",
            "/T",
            "/C",
            "/Q");
        await RunIcaclsAsync(
            secretDirectory,
            "/inheritance:r",
            "/grant:r",
            "*S-1-5-18:(OI)(CI)F",
            "*S-1-5-32-544:(OI)(CI)F",
            "*S-1-5-19:(OI)(CI)R",
            "/T",
            "/C",
            "/Q");
        await RunIcaclsAsync(
            keysDirectory,
            "/inheritance:r",
            "/grant:r",
            "*S-1-5-18:(OI)(CI)F",
            "*S-1-5-32-544:(OI)(CI)F",
            "*S-1-5-19:(OI)(CI)M",
            "/T",
            "/C",
            "/Q");
    }

    private static Task ApplyJwtKeyAccessControlAsync(string jwtKeyPath) =>
        RunIcaclsAsync(
            jwtKeyPath,
            "/inheritance:r",
            "/grant:r",
            "*S-1-5-18:F",
            "*S-1-5-32-544:F",
            "*S-1-5-19:R",
            "/Q");

    private static async Task RunIcaclsAsync(params string[] arguments)
    {
        var result = await RunProcessAsync(ResolveSystemExecutable("icacls.exe"), arguments);
        result.EnsureSuccess("aplicar as permissões de arquivos", 0);
    }

    [SupportedOSPlatform("windows")]
    private static async Task CreateOrUpdateServiceAsync(string executablePath, string dataDirectory, string jwtKeyPath)
    {
        var quotedExecutablePath = $"\"{executablePath}\"";
        Task<ProcessResult> ConfigureAsync(string verb) => RunScAsync(
            verb,
            ServiceName,
            "binPath=",
            quotedExecutablePath,
            "start=",
            "delayed-auto",
            "obj=",
            ServiceAccount,
            "DisplayName=",
            ServiceDisplayName);

        if (IsServiceMarkedForDeletion())
        {
            await WaitForPendingServiceDeletionAsync();
        }

        var result = await ConfigureAsync(ServiceExists() ? "config" : "create");
        if (result.ExitCode == 1072)
        {
            // O SCM ainda tinha uma exclusão pendente; aguarda concluir e recria o serviço.
            await WaitForPendingServiceDeletionAsync();
            result = await ConfigureAsync("create");
        }

        result.EnsureSuccess("criar ou atualizar o serviço", 0);

        var descriptionResult = await RunScAsync(
            "description",
            ServiceName,
            "Monitoramento técnico local e somente leitura do ambiente Protheus");
        descriptionResult.EnsureSuccess("configurar a descrição do serviço", 0);

        var failureResult = await RunScAsync(
            "failure",
            ServiceName,
            "reset=",
            "86400",
            "actions=",
            "restart/5000/restart/15000/restart/60000");
        failureResult.EnsureSuccess("configurar a recuperação do serviço", 0);

        var failureFlagResult = await RunScAsync("failureflag", ServiceName, "1");
        failureFlagResult.EnsureSuccess("habilitar a recuperação do serviço", 0);

        using var serviceKey = Registry.LocalMachine.OpenSubKey(ServiceRegistryPath, writable: true)
            ?? throw new InvalidOperationException("O registro do serviço não foi criado.");
        serviceKey.SetValue("ImagePath", quotedExecutablePath, RegistryValueKind.ExpandString);
        serviceKey.SetValue(
            "Environment",
            new[]
            {
                "DOTNET_ENVIRONMENT=Production",
                $"Pulse__DataDirectory={dataDirectory}",
                $"PULSE_JWT_SIGNING_KEY_FILE={jwtKeyPath}"
            },
            RegistryValueKind.MultiString);
        serviceKey.SetValue("DelayedAutostart", 1, RegistryValueKind.DWord);
    }

    [SupportedOSPlatform("windows")]
    private static bool ServiceExists()
    {
        using var serviceKey = Registry.LocalMachine.OpenSubKey(ServiceRegistryPath, writable: false);
        return serviceKey is not null;
    }

    [SupportedOSPlatform("windows")]
    private static bool IsServiceMarkedForDeletion()
    {
        using var serviceKey = Registry.LocalMachine.OpenSubKey(ServiceRegistryPath, writable: false);
        return serviceKey?.GetValue("DeleteFlag") is int deleteFlag && deleteFlag != 0;
    }

    [SupportedOSPlatform("windows")]
    private static async Task WaitForPendingServiceDeletionAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(30))
        {
            if (!ServiceExists())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new InvalidOperationException(
            "O serviço ProtheusPulse está marcado para exclusão e o Windows não concluiu a remoção. "
            + "Feche o console services.msc, o Gerenciador de Tarefas e outras ferramentas administrativas "
            + "(ou reinicie o servidor) e execute o instalador novamente.");
    }

    [SupportedOSPlatform("windows")]
    private static async Task StopServiceAsync()
    {
        if (!ServiceExists())
        {
            return;
        }

        using var controller = new ServiceController(ServiceName);
        controller.Refresh();
        if (controller.Status == ServiceControllerStatus.Stopped)
        {
            return;
        }

        var stopResult = await RunScAsync("stop", ServiceName);
        stopResult.EnsureSuccess("parar o serviço anterior", 0, 1062);
        await WaitForServiceStatusAsync(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
    }

    [SupportedOSPlatform("windows")]
    private static async Task WaitForServiceStatusAsync(ServiceControllerStatus expectedStatus, TimeSpan timeout)
    {
        using var controller = new ServiceController(ServiceName);
        var stopwatch = Stopwatch.StartNew();
        ServiceControllerStatus lastStatus;
        do
        {
            controller.Refresh();
            lastStatus = controller.Status;
            if (lastStatus == expectedStatus)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
        while (stopwatch.Elapsed < timeout);

        throw new System.TimeoutException($"O serviço permaneceu no estado {lastStatus} por mais de {timeout.TotalSeconds:0} segundos.");
    }

    private static async Task WaitForHealthCheckAsync()
    {
        using var handler = new HttpClientHandler { UseProxy = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
        for (var attempt = 1; attempt <= 30; attempt++)
        {
            try
            {
                using var response = await client.GetAsync(HealthUrl);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // O processo ainda pode estar inicializando o banco e o Kestrel.
            }
            catch (TaskCanceledException)
            {
                // Continua tentando até o limite global abaixo.
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new InvalidOperationException($"O serviço iniciou, mas {HealthUrl} não ficou saudável em 30 tentativas.");
    }

    private static void TryDeleteLegacyReleases(string installDirectory)
    {
        var releasesDirectory = Path.Combine(installDirectory, "releases");
        if (!Directory.Exists(releasesDirectory))
        {
            return;
        }

        try
        {
            Directory.Delete(releasesDirectory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Aviso: versões antigas não foram removidas: {exception.Message}");
        }
    }

    [SupportedOSPlatform("windows")]
    private static async Task<string> WriteDiagnosticsAsync(string dataDirectory, Exception exception)
    {
        var logDirectory = Path.Combine(dataDirectory, "logs");
        var diagnosticPath = Path.Combine(logDirectory, "install-diagnostics.txt");
        try
        {
            Directory.CreateDirectory(logDirectory);
            var diagnostics = new StringBuilder()
                .AppendLine(CultureInfo.InvariantCulture, $"UTC: {DateTimeOffset.UtcNow:O}")
                .AppendLine(CultureInfo.InvariantCulture, $"Operação: {exception}")
                .AppendLine(CultureInfo.InvariantCulture, $"HRESULT: 0x{exception.HResult:X8}")
                .AppendLine(CultureInfo.InvariantCulture, $"Executável: {Environment.ProcessPath}")
                .AppendLine(CultureInfo.InvariantCulture, $"Dados: {dataDirectory}");

            if (exception is FileNotFoundException fileNotFoundException)
            {
                diagnostics.AppendLine(CultureInfo.InvariantCulture, $"Arquivo ausente: {fileNotFoundException.FileName ?? "(não informado)"}");
            }

            if (ServiceExists())
            {
                using var serviceKey = Registry.LocalMachine.OpenSubKey(ServiceRegistryPath, writable: false);
                diagnostics.AppendLine(CultureInfo.InvariantCulture, $"ImagePath: {serviceKey?.GetValue("ImagePath", string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames)}");
                await AppendCommandOutputAsync(diagnostics, "sc queryex", "queryex", ServiceName);
                await AppendCommandOutputAsync(diagnostics, "sc qc", "qc", ServiceName);
            }

            var netstat = await RunProcessAsync(ResolveSystemExecutable("netstat.exe"), "-ano", "-p", "tcp");
            diagnostics.AppendLine("Porta 5058:");
            foreach (var line in netstat.StandardOutput.Split(Environment.NewLine).Where(item => item.Contains(":5058", StringComparison.Ordinal)))
            {
                diagnostics.AppendLine(line.Trim());
            }

            var dataAcl = await RunProcessAsync(ResolveSystemExecutable("icacls.exe"), dataDirectory);
            diagnostics.AppendLine("ACL do diretório de dados:");
            diagnostics.AppendLine(dataAcl.StandardOutput.Trim());

            var databasePath = Path.Combine(dataDirectory, "pulse.db");
            if (File.Exists(databasePath))
            {
                var databaseAcl = await RunProcessAsync(ResolveSystemExecutable("icacls.exe"), databasePath);
                diagnostics.AppendLine(CultureInfo.InvariantCulture, $"ACL do banco ({File.GetAttributes(databasePath)}):");
                diagnostics.AppendLine(databaseAcl.StandardOutput.Trim());
            }

            var serviceProcesses = await RunProcessAsync(
                ResolveSystemExecutable("tasklist.exe"),
                "/FI",
                "IMAGENAME eq ProtheusPulse.Service.exe");
            diagnostics.AppendLine("Processos ProtheusPulse.Service.exe:");
            diagnostics.AppendLine(serviceProcesses.StandardOutput.Trim());

            var scmEvents = await RunProcessAsync(
                ResolveSystemExecutable("wevtutil.exe"),
                "qe",
                "System",
                "/q:*[System[Provider[@Name='Service Control Manager']]]",
                "/c:12",
                "/rd:true",
                "/f:text");
            diagnostics.AppendLine("Eventos recentes do Service Control Manager:");
            diagnostics.AppendLine(scmEvents.StandardOutput.Trim());
            if (!string.IsNullOrWhiteSpace(scmEvents.StandardError))
            {
                diagnostics.AppendLine(scmEvents.StandardError.Trim());
            }

            await AppendRecentApplicationLogsAsync(diagnostics, logDirectory);
            diagnosticPath = await WriteDiagnosticsFileAsync(logDirectory, diagnosticPath, diagnostics.ToString());
        }
        catch (Exception diagnosticException)
        {
            Console.Error.WriteLine($"Também não foi possível gravar o diagnóstico: {diagnosticException.Message}");
        }

        return diagnosticPath;
    }

    private static async Task<string> WriteDiagnosticsFileAsync(string logDirectory, string diagnosticPath, string content)
    {
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                if (File.Exists(diagnosticPath))
                {
                    File.SetAttributes(diagnosticPath, FileAttributes.Normal);
                }

                await File.WriteAllTextAsync(diagnosticPath, content, new UTF8Encoding(false));
                return diagnosticPath;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt == 1)
                {
                    // ACLs herdadas de versões antigas podem negar a escrita administrativa; repara e tenta de novo.
                    await RunProcessAsync(
                        ResolveSystemExecutable("icacls.exe"),
                        logDirectory,
                        "/grant:r",
                        "*S-1-5-32-544:(OI)(CI)F",
                        "/T",
                        "/C",
                        "/Q");
                }
            }
        }

        var fallbackPath = Path.Combine(Path.GetTempPath(), "protheus-pulse-install-diagnostics.txt");
        await File.WriteAllTextAsync(fallbackPath, content, new UTF8Encoding(false));
        return fallbackPath;
    }

    private static async Task AppendCommandOutputAsync(StringBuilder diagnostics, string title, params string[] arguments)
    {
        var result = await RunScAsync(arguments);
        diagnostics.AppendLine(CultureInfo.InvariantCulture, $"{title} (código {result.ExitCode}):");
        diagnostics.AppendLine(result.StandardOutput.Trim());
        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            diagnostics.AppendLine(result.StandardError.Trim());
        }
    }

    private static async Task AppendRecentApplicationLogsAsync(StringBuilder diagnostics, string logDirectory)
    {
        try
        {
            var candidates = new List<string>();
            var latestLog = Directory.EnumerateFiles(logDirectory, "pulse-*.log", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (latestLog is not null)
            {
                candidates.Add(latestLog);
            }

            var startupCrashLog = Path.Combine(logDirectory, "startup-crash.log");
            if (File.Exists(startupCrashLog))
            {
                candidates.Add(startupCrashLog);
            }

            foreach (var candidate in candidates)
            {
                await AppendFileTailAsync(diagnostics, candidate);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.AppendLine(CultureInfo.InvariantCulture, $"Não foi possível listar os logs da aplicação: {exception.Message}");
        }
    }

    private static async Task AppendFileTailAsync(StringBuilder diagnostics, string path)
    {
        diagnostics.AppendLine(CultureInfo.InvariantCulture, $"Log da aplicação: {path}");
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                foreach (var line in File.ReadLines(path).TakeLast(80))
                {
                    diagnostics.AppendLine(line);
                }

                return;
            }
            catch (UnauthorizedAccessException) when (attempt == 1)
            {
                // O arquivo pode ter herdado uma ACL sem leitura administrativa; corrige e tenta de novo.
                await RunProcessAsync(ResolveSystemExecutable("icacls.exe"), path, "/grant:r", "*S-1-5-32-544:R", "/Q");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.AppendLine(CultureInfo.InvariantCulture, $"Não foi possível ler o log da aplicação: {exception.Message}");
                return;
            }
        }
    }

    private static Task<ProcessResult> RunScAsync(params string[] arguments) =>
        RunProcessAsync(ResolveSystemExecutable("sc.exe"), arguments);

    private static string ResolveSystemExecutable(string executableName)
    {
        var systemPath = Path.Combine(Environment.SystemDirectory, executableName);
        if (File.Exists(systemPath))
        {
            return systemPath;
        }

        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var fallbackPath = Path.Combine(windowsDirectory, "System32", executableName);
        if (File.Exists(fallbackPath))
        {
            return fallbackPath;
        }

        throw new FileNotFoundException(
            $"A ferramenta do Windows {executableName} não foi encontrada.",
            systemPath);
    }

    private static async Task<ProcessResult> RunProcessAsync(string fileName, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Não foi possível executar {Path.GetFileName(fileName)}.");
        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, await standardOutputTask, await standardErrorTask);
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public void EnsureSuccess(string action, params int[] acceptedExitCodes)
        {
            if (acceptedExitCodes.Contains(ExitCode))
            {
                return;
            }

            var detail = string.IsNullOrWhiteSpace(StandardError) ? StandardOutput : StandardError;
            throw new InvalidOperationException($"Falha ao {action} (código {ExitCode}): {detail.Trim()}");
        }
    }
}
