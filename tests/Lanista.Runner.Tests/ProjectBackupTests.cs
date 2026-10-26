using Lanista.Runner;

namespace Lanista.Runner.Tests;

public class ProjectBackupTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("lanista-bk-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string MakeProject()
    {
        var project = Path.Combine(_root, "Churn");
        Directory.CreateDirectory(Path.Combine(project, "sub"));
        Directory.CreateDirectory(Path.Combine(project, "bin", "Debug"));
        Directory.CreateDirectory(Path.Combine(project, "obj"));
        File.WriteAllText(Path.Combine(project, "Churn.mlnet"), "model");
        File.WriteAllText(Path.Combine(project, "sub", "x.txt"), "x");
        File.WriteAllText(Path.Combine(project, "bin", "Debug", "big.dll"), "binary");
        return project;
    }

    [Fact]
    public void Copies_files_and_subfolders_but_skips_bin_and_obj()
    {
        var project = MakeProject();

        var backup = ProjectBackup.Create(project);

        Assert.StartsWith(project + ".backup-", backup);
        Assert.Equal("model", File.ReadAllText(Path.Combine(backup, "Churn.mlnet")));
        Assert.Equal("x", File.ReadAllText(Path.Combine(backup, "sub", "x.txt")));
        Assert.False(Directory.Exists(Path.Combine(backup, "bin")));
        Assert.False(Directory.Exists(Path.Combine(backup, "obj")));
        Assert.True(File.Exists(Path.Combine(project, "bin", "Debug", "big.dll")));
    }

    [Fact]
    public void Two_backups_in_the_same_second_get_distinct_folders()
    {
        var project = MakeProject();

        var first = ProjectBackup.Create(project);
        var second = ProjectBackup.Create(project + Path.DirectorySeparatorChar);

        Assert.NotEqual(first, second);
        Assert.True(Directory.Exists(first));
        Assert.True(Directory.Exists(second));
    }

    [Fact]
    public void TryRemove_deletes_the_backup()
    {
        var backup = ProjectBackup.Create(MakeProject());
        Assert.True(ProjectBackup.TryRemove(backup));
        Assert.False(Directory.Exists(backup));
    }
}
