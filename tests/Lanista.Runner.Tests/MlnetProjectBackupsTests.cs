using Lanista.Runner;

namespace Lanista.Runner.Tests;

public class MlnetProjectBackupsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("lanista-rs-").FullName;
    private string Project => Path.Combine(_root, "Churn");

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private void Write(string relative, string content)
    {
        var path = Path.Combine(Project, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void List_returns_only_backups_of_this_project_newest_first()
    {
        Write("Churn.mlnet", "v1");
        var first = ProjectBackup.Create(Project);
        Directory.SetCreationTimeUtc(first, DateTime.UtcNow.AddMinutes(-5));
        var second = ProjectBackup.Create(Project);
        Directory.CreateDirectory(Path.Combine(_root, "Other.backup-20200101-000000"));
        Directory.CreateDirectory(Path.Combine(_root, "Churn2.backup-20200101-000000"));

        var list = MlnetProjectBackups.List(Project);

        Assert.Equal([second, first], list.Select(b => b.Path));
    }

    [Fact]
    public void List_is_empty_when_there_are_no_backups_or_no_parent()
    {
        Write("Churn.mlnet", "v1");
        Assert.Empty(MlnetProjectBackups.List(Project));
        Assert.Empty(MlnetProjectBackups.List(Path.Combine(_root, "missing", "Churn")));
    }

    [Fact]
    public void Restore_brings_back_the_old_files_and_removes_files_added_since()
    {
        Write("Churn.mlnet", "old model");
        Write("sub/old.txt", "old");
        var backup = ProjectBackup.Create(Project);

        Write("Churn.mlnet", "new model");
        Write("extra.cs", "new file");
        Directory.Delete(Path.Combine(Project, "sub"), recursive: true);
        Write("bin/Debug/app.dll", "build output");

        var safety = MlnetProjectBackups.Restore(Project, backup);

        Assert.Equal("old model", File.ReadAllText(Path.Combine(Project, "Churn.mlnet")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(Project, "sub", "old.txt")));
        Assert.False(File.Exists(Path.Combine(Project, "extra.cs")));
        Assert.True(File.Exists(Path.Combine(Project, "bin", "Debug", "app.dll")));
        Assert.True(Directory.Exists(backup));
        Assert.NotEqual(backup, safety);
        Assert.Equal("new model", File.ReadAllText(Path.Combine(safety, "Churn.mlnet")));
        Assert.True(File.Exists(Path.Combine(safety, "extra.cs")));
    }

    [Fact]
    public void Restore_can_be_undone_through_the_safety_backup()
    {
        Write("Churn.mlnet", "v1");
        var v1 = ProjectBackup.Create(Project);
        Write("Churn.mlnet", "v2");

        var safety = MlnetProjectBackups.Restore(Project, v1);
        Assert.Equal("v1", File.ReadAllText(Path.Combine(Project, "Churn.mlnet")));

        MlnetProjectBackups.Restore(Project, safety);
        Assert.Equal("v2", File.ReadAllText(Path.Combine(Project, "Churn.mlnet")));
    }

    [Fact]
    public void Restore_recreates_a_project_folder_that_was_deleted()
    {
        Write("Churn.mlnet", "v1");
        var backup = ProjectBackup.Create(Project);
        Directory.Delete(Project, recursive: true);

        MlnetProjectBackups.Restore(Project, backup);

        Assert.Equal("v1", File.ReadAllText(Path.Combine(Project, "Churn.mlnet")));
    }

    [Fact]
    public void Restore_and_delete_refuse_folders_that_are_not_backups_of_the_project()
    {
        Write("Churn.mlnet", "v1");
        var elsewhere = Directory.CreateDirectory(Path.Combine(_root, "Other")).FullName;
        var otherBackup = Directory.CreateDirectory(Path.Combine(_root, "Other.backup-1")).FullName;

        Assert.Throws<ArgumentException>(() => MlnetProjectBackups.Restore(Project, elsewhere));
        Assert.Throws<ArgumentException>(() => MlnetProjectBackups.Restore(Project, otherBackup));
        Assert.Throws<ArgumentException>(() => MlnetProjectBackups.Delete(Project, _root));
        Assert.Throws<ArgumentException>(() => MlnetProjectBackups.Delete(Project, Path.Combine(Project, "..", "Churn.backup-1", "..", "Other")));
        Assert.True(Directory.Exists(elsewhere));
        Assert.Equal("v1", File.ReadAllText(Path.Combine(Project, "Churn.mlnet")));
    }

    [Fact]
    public void Missing_backup_throws()
    {
        Write("Churn.mlnet", "v1");
        Assert.Throws<DirectoryNotFoundException>(() => MlnetProjectBackups.Restore(Project, Project + ".backup-gone"));
    }

    [Fact]
    public void Delete_removes_only_the_chosen_backup()
    {
        Write("Churn.mlnet", "v1");
        var a = ProjectBackup.Create(Project);
        var b = ProjectBackup.Create(Project);

        MlnetProjectBackups.Delete(Project, a);

        Assert.False(Directory.Exists(a));
        Assert.True(Directory.Exists(b));
        Assert.True(Directory.Exists(Project));
    }
}
