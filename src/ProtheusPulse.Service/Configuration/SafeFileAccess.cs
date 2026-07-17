namespace ProtheusPulse.Service.Configuration;

public static class SafeFileAccess
{
    public static bool TryResolveRoot(string? root, out string fullRoot, out string error)
    {
        fullRoot = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(root) || root.Length > 2_048 || root.Any(char.IsControl))
        {
            error = "A raiz é obrigatória e deve possuir no máximo 2048 caracteres sem controles.";
            return false;
        }

        try
        {
            fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root.Trim()));
            var volumeRoot = Path.TrimEndingDirectorySeparator(Path.GetPathRoot(fullRoot) ?? string.Empty);
            if (string.Equals(fullRoot, volumeRoot, PathComparison))
            {
                error = "A raiz de um volume ou compartilhamento não pode ser varrida diretamente.";
                return false;
            }

            if (!Directory.Exists(fullRoot))
            {
                error = "A raiz informada não existe ou não está acessível.";
                return false;
            }

            if (HasReparsePoint(fullRoot))
            {
                error = "A raiz não pode ser um link simbólico, junction ou reparse point.";
                return false;
            }

            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error = "A raiz informada é inválida ou não está acessível.";
            return false;
        }
    }

    public static bool TryResolveExistingFile(
        string? root,
        string? path,
        string requiredExtension,
        out string fullPath,
        out string error)
    {
        fullPath = string.Empty;
        if (!TryResolveRoot(root, out var fullRoot, out error))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(path) || path.Length > 2_048 || path.Any(char.IsControl))
        {
            error = "O caminho do arquivo é obrigatório e inválido.";
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(path.Trim());
            var relative = Path.GetRelativePath(fullRoot, fullPath);
            if (Path.IsPathRooted(relative)
                || relative == ".."
                || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
            {
                error = "O arquivo deve estar contido na raiz autorizada.";
                return false;
            }

            if (!string.Equals(Path.GetExtension(fullPath), requiredExtension, StringComparison.OrdinalIgnoreCase))
            {
                error = $"O arquivo deve possuir extensão {requiredExtension}.";
                return false;
            }

            if (!File.Exists(fullPath))
            {
                error = "O arquivo não existe ou não está acessível.";
                return false;
            }

            if (ContainsReparsePoint(fullRoot, relative))
            {
                error = "O caminho não pode atravessar links simbólicos, junctions ou reparse points.";
                return false;
            }

            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error = "O caminho informado é inválido ou não está acessível.";
            return false;
        }
    }

    private static bool ContainsReparsePoint(string root, string relative)
    {
        var current = root;
        foreach (var segment in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (HasReparsePoint(current))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
