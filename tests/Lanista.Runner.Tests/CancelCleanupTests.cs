using Lanista.Runner;

namespace Lanista.Runner.Tests;

public class CancelCleanupTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("lanista-cc-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Removes_partial_project_and_the_output_directory_created_by_the_run()
    {
        var output = Path.Combine(_root, "out");
        var project = Path.Combine(output, "Churn");
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, "Churn.training.cs"), "partial");

        var removed = CancelCleanup.Run(output, "Churn", outputDirectoryExisted: false, projectDirectoryExisted: false);

        Assert.Equal([project, output], removed);
        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public void Keeps_an_existing_output_directory_with_other_content()
    {
        var output = Path.Combine(_root, "out");
        Directory.CreateDirectory(output);
        var other = Path.Combine(output, "other.txt");
        File.WriteAllText(other, "keep me");

        var removed = CancelCleanup.Run(output, "Churn", outputDirectoryExisted: true, projectDirectoryExisted: false);

        Assert.Empty(removed);
        Assert.True(File.Exists(other));
    }

    [Fact]
    public void Keeps_a_project_directory_that_existed_before_the_run()
    {
        var output = Path.Combine(_root, "out");
        var project = Path.Combine(output, "Churn");
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, "Churn.mlnet"), "previous model");

        var removed = CancelCleanup.Run(output, "Churn", outputDirectoryExisted: true, projectDirectoryExisted: true);

        Assert.Empty(removed);
        Assert.True(File.Exists(Path.Combine(project, "Churn.mlnet")));
    }

    [Fact]
    public void Removes_only_the_partial_project_when_the_output_directory_already_existed()
    {
        var output = Path.Combine(_root, "out");
        var project = Path.Combine(output, "Churn");
        Directory.CreateDirectory(project);

        var removed = CancelCleanup.Run(output, "Churn", outputDirectoryExisted: true, projectDirectoryExisted: false);

        Assert.Equal([project], removed);
        Assert.True(Directory.Exists(output));
    }

    [Fact]
    public void Does_nothing_when_nothing_was_created()
    {
        var removed = CancelCleanup.Run(Path.Combine(_root, "never-created"), "Churn", false, false);
        Assert.Empty(removed);
    }
}
