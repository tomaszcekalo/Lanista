namespace Lanista.Runner;

/// <summary>
/// Re-trains from an existing .mbconfig. mlnet takes everything else from the file and regenerates
/// the project in the folder that contains it.
/// </summary>
public sealed record MlnetTrainOptions
{
    public required string TrainingConfigPath { get; init; }
    public string? LogFilePath { get; init; }

    /// <summary>Copy the project folder to a sibling "&lt;name&gt;.backup-&lt;timestamp&gt;" folder before re-training, so the previous model survives.</summary>
    public bool BackupPreviousProject { get; init; } = true;

    internal IReadOnlyList<string> ToArguments()
    {
        var args = new List<string> { "train", "--training-config", TrainingConfigPath, "-v", "diag" };
        if (LogFilePath is not null) args.AddRange(["--log-file-path", LogFilePath]);
        return args;
    }
}
