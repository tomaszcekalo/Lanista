namespace Lanista.Runner;

public sealed record MlnetJobOptions
{
    public required MlnetScenario Scenario { get; init; }
    public required string DatasetPath { get; init; }
    /// <summary>The label column; for recommendation this is the rating column.</summary>
    public required string LabelColumn { get; init; }
    public string? UserColumn { get; init; }
    public string? ItemColumn { get; init; }
    public string? TimeColumn { get; init; }
    public int? Horizon { get; init; }
    public required string OutputDirectory { get; init; }
    public string? Name { get; init; }
    public int? TrainTimeSeconds { get; init; }
    public string? LogFilePath { get; init; }
    public bool? HasHeader { get; init; }
    public IReadOnlyList<string> IgnoreColumns { get; init; } = [];
    /// <summary>Fraction of rows held out for validation, strictly between 0 and 1. Exclusive with CvFolds and ValidationDatasetPath.</summary>
    public double? SplitRatio { get; init; }
    /// <summary>Number of cross-validation folds (at least 2). Exclusive with SplitRatio and ValidationDatasetPath.</summary>
    public int? CvFolds { get; init; }
    /// <summary>A separate validation file. Exclusive with SplitRatio and CvFolds.</summary>
    public string? ValidationDatasetPath { get; init; }

    // mlnet forecasting names its default project "SampleClassification" (CLI quirk).
    internal string ProjectName => Name ?? (Scenario == MlnetScenario.Forecasting ? "SampleClassification" : $"Sample{Scenario}");

    internal IReadOnlyList<string> ToArguments()
    {
        var args = new List<string>
        {
            Scenario.ToString().ToLowerInvariant(),
            "--dataset", DatasetPath,
            "-o", OutputDirectory,
            "-v", "diag",
        };

        if (Scenario == MlnetScenario.Recommendation)
            args.AddRange(["--user-col", UserColumn ?? "", "--item-col", ItemColumn ?? "", "--rating-col", LabelColumn]);
        else
            args.AddRange(["--label-col", LabelColumn]);

        if (Scenario == MlnetScenario.Forecasting)
            args.AddRange(["--time-col", TimeColumn ?? "", "--horizon", (Horizon ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture)]);

        if (Name is not null) args.AddRange(["--name", Name]);
        if (TrainTimeSeconds is { } t) args.AddRange(["--train-time", t.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        if (LogFilePath is not null) args.AddRange(["--log-file-path", LogFilePath]);
        if (HasHeader is { } h) args.AddRange(["--has-header", h ? "true" : "false"]);
        if (SplitRatio is { } r) args.AddRange(["--split-ratio", r.ToString("R", System.Globalization.CultureInfo.InvariantCulture)]);
        if (CvFolds is { } f) args.AddRange(["--cv-fold", f.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        if (ValidationDatasetPath is not null) args.AddRange(["--validation-dataset", ValidationDatasetPath]);
        if (IgnoreColumns.Count > 0 && Scenario is not (MlnetScenario.Recommendation or MlnetScenario.Forecasting)) { args.Add("--ignore-cols"); args.AddRange(IgnoreColumns); }

        return args;
    }
}
