namespace Lanista.Runner;

public sealed record ProjectBackupInfo(string Path, DateTime CreatedUtc);

/// <summary>Lists, restores and deletes the "&lt;name&gt;.backup-&lt;timestamp&gt;" folders made before a re-train.</summary>
public static class MlnetProjectBackups
{
    private const string Marker = ".backup-";

    public static IReadOnlyList<ProjectBackupInfo> List(string projectDirectory)
    {
        var project = Normalize(projectDirectory);
        var parent = Path.GetDirectoryName(project)!;
        if (!Directory.Exists(parent)) return [];

        return Directory.GetDirectories(parent, Path.GetFileName(project) + Marker + "*")
            .Select(p => new ProjectBackupInfo(p, Directory.GetCreationTimeUtc(p)))
            .OrderByDescending(b => b.CreatedUtc)
            .ThenByDescending(b => b.Path, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Replaces the project's files with the backup's. The current state is backed up first, so a
    /// restore can itself be undone; the returned path is that safety backup. The restored backup is kept.
    /// </summary>
    public static string Restore(string projectDirectory, string backupDirectory)
    {
        var project = Normalize(projectDirectory);
        var backup = ValidateBackup(project, backupDirectory);

        var safety = Directory.Exists(project)
            ? ProjectBackup.Create(project)
            : null;

        Directory.CreateDirectory(project);
        foreach (var file in Directory.GetFiles(project)) File.Delete(file);
        foreach (var dir in Directory.GetDirectories(project))
        {
            if (ProjectBackup.SkippedFolders.Contains(Path.GetFileName(dir), StringComparer.OrdinalIgnoreCase)) continue;
            Directory.Delete(dir, recursive: true);
        }

        ProjectBackup.Copy(backup, project);
        return safety ?? backup;
    }

    public static void Delete(string projectDirectory, string backupDirectory)
    {
        var backup = ValidateBackup(Normalize(projectDirectory), backupDirectory);
        Directory.Delete(backup, recursive: true);
    }

    // Only folders that this library names as backups of this very project may be restored or deleted.
    private static string ValidateBackup(string project, string backupDirectory)
    {
        var backup = Normalize(backupDirectory);
        var sameParent = string.Equals(Path.GetDirectoryName(backup), Path.GetDirectoryName(project), StringComparison.OrdinalIgnoreCase);
        var named = Path.GetFileName(backup).StartsWith(Path.GetFileName(project) + Marker, StringComparison.OrdinalIgnoreCase);
        if (!sameParent || !named)
            throw new ArgumentException($"'{backupDirectory}' is not a backup of '{project}'.", nameof(backupDirectory));
        if (!Directory.Exists(backup))
            throw new DirectoryNotFoundException($"Backup folder does not exist: {backup}");
        return backup;
    }

    private static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
