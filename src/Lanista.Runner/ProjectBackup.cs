namespace Lanista.Runner;

internal static class ProjectBackup
{
    internal static readonly string[] SkippedFolders = ["bin", "obj"];

    // Copies the project folder to a sibling "<name>.backup-<timestamp>" folder and returns its path.
    public static string Create(string projectDirectory)
    {
        var full = Path.GetFullPath(projectDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var target = $"{full}.backup-{stamp}";
        for (var i = 2; Directory.Exists(target); i++) target = $"{full}.backup-{stamp}-{i}";

        Copy(full, target);
        return target;
    }

    public static bool TryRemove(string backupDirectory)
    {
        try
        {
            Directory.Delete(backupDirectory, recursive: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal static void Copy(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));

        foreach (var dir in Directory.GetDirectories(source))
        {
            var name = Path.GetFileName(dir);
            if (SkippedFolders.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            Copy(dir, Path.Combine(target, name));
        }
    }
}
