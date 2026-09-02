using System.Globalization;
using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using ProtheusPulse.Infrastructure.Persistence;
using ProtheusPulse.Service.Configuration;

namespace ProtheusPulse.Service.Monitoring;

/// <summary>
/// Cópia de segurança do que não se recupera de outro lugar: o banco, a chave de Data
/// Protection e a configuração de rede.
/// </summary>
/// <remarks>
/// Sem a chave, um banco restaurado perde as URLs dos pontos de contato e a senha do
/// SMTP, que ficam cifradas por ela — é o item que costuma faltar num backup feito à mão.
/// O banco é copiado com <c>VACUUM INTO</c> para sair consistente mesmo com o serviço
/// coletando durante a cópia.
/// </remarks>
public sealed class BackupService(PulseDbContext dbContext, PulseDataDirectory dataDirectory)
{
    public const int RetainedBackups = 10;
    private const string DirectoryName = "backups";

    public string BackupDirectory => Path.Combine(dataDirectory.Path, DirectoryName);

    public async Task<BackupFile> CreateAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(BackupDirectory);
        var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var archivePath = Path.Combine(BackupDirectory, $"pulse-backup-{stamp}.zip");
        var snapshotPath = Path.Combine(BackupDirectory, $"snapshot-{stamp}.db");

        try
        {
            // VACUUM INTO tira uma cópia consistente sem parar o serviço; copiar o arquivo
            // durante uma escrita traria um banco pela metade. O SQLite não aceita
            // parâmetro no destino, então o caminho entra no texto — ele é montado aqui,
            // a partir do diretório de dados e de um carimbo de hora, sem entrada de fora.
            var destination = snapshotPath.Replace("'", "''", StringComparison.Ordinal);
            var vacuum = "VACUUM INTO '" + destination + "'";
            await dbContext.Database.ExecuteSqlRawAsync(vacuum, cancellationToken);

            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(snapshotPath, "pulse.db", CompressionLevel.Optimal);
                AddDirectory(archive, Path.Combine(dataDirectory.Path, "keys"), "keys");
                AddFile(archive, Path.Combine(dataDirectory.Path, "network.json"), "network.json");
                AddFile(archive, Path.Combine(dataDirectory.Path, TlsCertificates.PasswordFileName), TlsCertificates.PasswordFileName);
                var readme = archive.CreateEntry("LEIAME.txt", CompressionLevel.Optimal);
                await using var writer = new StreamWriter(readme.Open());
                await writer.WriteAsync(RestoreInstructions);
            }
        }
        finally
        {
            if (File.Exists(snapshotPath))
            {
                File.Delete(snapshotPath);
            }
        }

        PruneOldBackups();
        var info = new FileInfo(archivePath);
        return new BackupFile(info.Name, info.Length, new DateTimeOffset(info.CreationTimeUtc, TimeSpan.Zero));
    }

    public IReadOnlyList<BackupFile> List()
    {
        if (!Directory.Exists(BackupDirectory))
        {
            return [];
        }

        return new DirectoryInfo(BackupDirectory)
            .GetFiles("pulse-backup-*.zip")
            .OrderByDescending(item => item.CreationTimeUtc)
            .Select(item => new BackupFile(item.Name, item.Length, new DateTimeOffset(item.CreationTimeUtc, TimeSpan.Zero)))
            .ToArray();
    }

    /// <summary>Resolve o caminho recusando qualquer nome que tente sair da pasta de backups.</summary>
    public string? ResolvePath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName.Contains('/', StringComparison.Ordinal)
            || fileName.Contains('\\', StringComparison.Ordinal)
            || fileName.Contains("..", StringComparison.Ordinal)
            || !fileName.StartsWith("pulse-backup-", StringComparison.Ordinal)
            || !fileName.EndsWith(".zip", StringComparison.Ordinal))
        {
            return null;
        }

        var path = Path.Combine(BackupDirectory, fileName);
        return File.Exists(path) ? path : null;
    }

    private void PruneOldBackups()
    {
        foreach (var stale in new DirectoryInfo(BackupDirectory)
            .GetFiles("pulse-backup-*.zip")
            .OrderByDescending(item => item.CreationTimeUtc)
            .Skip(RetainedBackups))
        {
            try
            {
                stale.Delete();
            }
            catch (IOException)
            {
                // Um arquivo em uso continua para a próxima limpeza.
            }
        }
    }

    private static void AddDirectory(ZipArchive archive, string path, string entryPrefix)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(path))
        {
            archive.CreateEntryFromFile(file, $"{entryPrefix}/{Path.GetFileName(file)}", CompressionLevel.Optimal);
        }
    }

    private static void AddFile(ZipArchive archive, string path, string entryName)
    {
        if (File.Exists(path))
        {
            archive.CreateEntryFromFile(path, entryName, CompressionLevel.Optimal);
        }
    }

    private const string RestoreInstructions = """
        Restauração do Protheus Pulse
        =============================

        Este pacote traz o banco, a chave de Data Protection e a configuração de rede.
        A chave é indispensável: sem ela, o banco restaurado perde as URLs dos pontos de
        contato e a senha do SMTP, que ficam cifradas por ela.

        Passo a passo, no servidor de destino:

        1. Pare o serviço:            sc.exe stop ProtheusPulse
        2. Guarde o estado atual:     renomeie C:\ProgramData\ProtheusPulse para ...-antigo
        3. Recrie a pasta             C:\ProgramData\ProtheusPulse
        4. Copie deste pacote:
             pulse.db                   -> C:\ProgramData\ProtheusPulse\pulse.db
             keys\*                     -> C:\ProgramData\ProtheusPulse\keys\
             network.json               -> C:\ProgramData\ProtheusPulse\network.json
             certificate-password.dat   -> C:\ProgramData\ProtheusPulse\ (se existir)
        5. Suba o serviço:            sc.exe start ProtheusPulse
        6. Confira http://127.0.0.1:5058/health/ready

        A restauração não é feita pelo painel de propósito: trocar o banco embaixo de um
        serviço em execução corrompe o que estiver sendo escrito naquele instante.
        """;

    public sealed record BackupFile(string Name, long SizeBytes, DateTimeOffset CreatedAt);
}
