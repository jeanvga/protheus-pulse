using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace ProtheusPulse.Service.Configuration;

/// <summary>
/// Marcos de inicialização com o tempo decorrido, gravados antes de o Serilog existir.
/// O Gerenciador de Serviços derruba o start em 30 segundos com o erro 1053 e, até aqui,
/// uma falha nesse intervalo não deixava rastro nenhum: o log da aplicação só começa em
/// <c>Run()</c> e o de crash só recebe exceção. Sem estes marcos não dá para saber se o
/// tempo foi embora carregando o binário, lendo configuração ou construindo o host.
/// </summary>
public static class StartupTrace
{
    private const long MaximumBytes = 256 * 1024;

    private static readonly Stopwatch Elapsed = Stopwatch.StartNew();
    private static readonly object Gate = new();

    private static readonly List<string> Pending = [];

    private static string? file;

    /// <summary>
    /// Direciona os marcos para o diretório de dados assim que ele é conhecido e descarrega
    /// os que ficaram em memória. Os primeiros marcos são os mais importantes justamente
    /// quando a falha é cedo, e o caminho padrão pode não estar gravável ainda.
    /// </summary>
    public static void UseDataDirectory(string dataDirectory)
    {
        string[] buffered;
        lock (Gate)
        {
            file = Path.Combine(dataDirectory, "logs", "startup-trace.log");
            buffered = [.. Pending];
            Pending.Clear();
        }

        foreach (var line in buffered)
        {
            Write(line);
        }
    }

    /// <summary>
    /// Quanto tempo passou entre o Windows lançar o processo e o código gerenciado rodar.
    /// É o que separa "o binário demorou a carregar" — antivírus, disco, EDR — de "o
    /// serviço demorou a se preparar": só a segunda hipótese tem conserto no código.
    /// </summary>
    public static void MarkProcessStart()
    {
        try
        {
            using var current = Process.GetCurrentProcess();
            var launched = current.StartTime.ToUniversalTime();
            var untilManaged = DateTime.UtcNow - launched;
            Mark(string.Create(
                CultureInfo.InvariantCulture,
                $"processo lançado às {launched:O}; {untilManaged.TotalMilliseconds:0} ms até o código gerenciado"));
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            Mark("processo iniciado (não foi possível medir a carga do binário)");
        }
    }

    public static void Mark(string stage)
    {
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{DateTimeOffset.UtcNow:O} +{Elapsed.ElapsedMilliseconds,6}ms  {stage}{Environment.NewLine}");
        lock (Gate)
        {
            if (file is null)
            {
                Pending.Add(line);
            }
        }

        Write(line);
    }

    private static void Write(string line)
    {
        try
        {
            lock (Gate)
            {
                var path = file ?? DefaultPath();
                var directory = Path.GetDirectoryName(path);
                if (directory is not null)
                {
                    Directory.CreateDirectory(directory);
                }

                if (File.Exists(path) && new FileInfo(path).Length > MaximumBytes)
                {
                    File.Delete(path);
                }

                File.AppendAllText(path, line, Encoding.UTF8);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Diagnóstico nunca pode derrubar a inicialização que ele existe para explicar.
        }
    }

    private static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ProtheusPulse",
        "logs",
        "startup-trace.log");
}
