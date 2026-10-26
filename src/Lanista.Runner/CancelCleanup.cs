namespace Lanista.Runner;

internal static class CancelCleanup
{
    // Removes only what this run could have created: the generated project folder (if it did not exist
    // before the run) and the output directory (if the runner created it and nothing else lives there).
    public static IReadOnlyList<string> Run(
        string outputDirectory,
        string projectName,
        bool outputDirectoryExisted,
        bool projectDirectoryExisted)
    {
        var removed = new List<string>();
        var projectDirectory = Path.Combine(outputDirectory, projectName);

        if (!projectDirectoryExisted && TryDelete(projectDirectory, recursive: true))
            removed.Add(projectDirectory);

        if (!outputDirectoryExisted
            && Directory.Exists(outputDirectory)
            && !Directory.EnumerateFileSystemEntries(outputDirectory).Any()
            && TryDelete(outputDirectory, recursive: false))
        {
            removed.Add(outputDirectory);
        }

        return removed;
    }

    private static bool TryDelete(string path, bool recursive)
    {
        if (!Directory.Exists(path)) return false;

        try
        {
            Directory.Delete(path, recursive);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
