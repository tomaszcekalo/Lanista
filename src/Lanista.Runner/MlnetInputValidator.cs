using System.Globalization;
using System.Text.Json;

namespace Lanista.Runner;

public static class MlnetInputValidator
{
    public const int MinRecommendedRows = 100;
    public const int MinSplitRows = 10;

    private static readonly char[] Delimiters = [',', '\t', ';'];

    public static async Task<IReadOnlyList<ValidationIssue>> ValidateAsync(
        MlnetJobOptions options,
        CancellationToken cancellationToken = default)
    {
        var issues = new List<ValidationIssue>();

        if (options.TrainTimeSeconds is <= 0)
            issues.Add(Error("InvalidTrainTime", "Training time must be greater than 0 seconds."));

        var strategies = (options.SplitRatio is null ? 0 : 1) + (options.CvFolds is null ? 0 : 1) + (options.ValidationDatasetPath is null ? 0 : 1);
        if (strategies > 1)
            issues.Add(Error("ValidationOptionsConflict", "Use only one of split ratio, cross-validation folds or a validation dataset (mlnet resolves combinations inconsistently)."));

        if (options.SplitRatio is { } ratio && !(ratio > 0 && ratio < 1))
            issues.Add(Error("InvalidSplitRatio", $"Split ratio must be greater than 0 and less than 1, got {ratio.ToString(CultureInfo.InvariantCulture)}."));

        if (options.CvFolds is < 2)
            issues.Add(Error("InvalidCvFolds", $"Cross-validation needs at least 2 folds, got {options.CvFolds}."));

        if (options.ValidationDatasetPath is { } validationPath && !File.Exists(validationPath))
            issues.Add(Error("ValidationDatasetNotFound", $"Validation dataset file does not exist: {validationPath}"));

        if (options.Scenario == MlnetScenario.Recommendation)
        {
            if (string.IsNullOrWhiteSpace(options.UserColumn))
                issues.Add(Error("UserColumnMissing", "Recommendation requires a user column."));
            if (string.IsNullOrWhiteSpace(options.ItemColumn))
                issues.Add(Error("ItemColumnMissing", "Recommendation requires an item column."));
        }

        if (options.Scenario == MlnetScenario.Forecasting)
        {
            if (string.IsNullOrWhiteSpace(options.TimeColumn))
                issues.Add(Error("TimeColumnMissing", "Forecasting requires a time column."));
            if (options.Horizon is not > 0)
                issues.Add(Error("InvalidHorizon", "Forecasting requires a horizon greater than 0."));
        }

        if (options.Scenario is MlnetScenario.Recommendation or MlnetScenario.Forecasting && options.IgnoreColumns.Count > 0)
            issues.Add(Error("IgnoreColumnsUnsupported", $"mlnet {options.Scenario.ToString().ToLowerInvariant()} does not support ignored columns."));

        if (File.Exists(options.OutputDirectory))
            issues.Add(Error("OutputIsFile", $"Output path '{options.OutputDirectory}' is a file, not a directory."));

        if (!File.Exists(options.DatasetPath))
        {
            issues.Add(Error("DatasetNotFound", $"Dataset file does not exist: {options.DatasetPath}"));
            return issues;
        }

        await ValidateDatasetAsync(options, issues, cancellationToken).ConfigureAwait(false);
        return issues;
    }

    public static async Task<IReadOnlyList<ValidationIssue>> ValidateAsync(
        MlnetTrainOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(options.TrainingConfigPath))
            return [Error("ConfigNotFound", $"Training config file does not exist: {options.TrainingConfigPath}")];

        try
        {
            var text = await File.ReadAllTextAsync(options.TrainingConfigPath, cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("Scenario", out _) ||
                !doc.RootElement.TryGetProperty("DataSource", out var dataSource))
                return [Error("ConfigInvalid", "Training config is not a valid .mbconfig (missing Scenario or DataSource).")];

            if (dataSource.TryGetProperty("FilePath", out var pathElement) && pathElement.GetString() is { Length: > 0 } dataPath)
            {
                var resolved = Path.GetFullPath(dataPath, Path.GetDirectoryName(Path.GetFullPath(options.TrainingConfigPath))!);
                if (!File.Exists(resolved))
                    return [Error("DatasetNotFound", $"Dataset referenced by the training config does not exist: {resolved}")];
            }
        }
        catch (JsonException ex)
        {
            return [Error("ConfigInvalid", $"Training config is not valid JSON: {ex.Message}")];
        }

        return [];
    }

    private static async Task ValidateDatasetAsync(
        MlnetJobOptions options,
        List<ValidationIssue> issues,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(options.DatasetPath);

        var first = await NextNonBlankLineAsync(reader, cancellationToken).ConfigureAwait(false);
        if (first is null)
        {
            issues.Add(Error("DatasetEmpty", "Dataset file is empty."));
            return;
        }

        // mlnet does not honor quoting unless --allow-quote is set, so a plain split matches its behavior.
        var delimiter = DetectDelimiter(first);
        var hasHeader = options.HasHeader ?? true;
        var columnCount = first.Split(delimiter).Length;

        var labelIndex = ResolveLabelIndex(options.LabelColumn, hasHeader ? first.Split(delimiter) : null, columnCount);
        if (labelIndex is null)
        {
            issues.Add(Error("LabelColumnNotFound", hasHeader
                ? $"Label column '{options.LabelColumn}' was not found in the dataset header ({columnCount} columns)."
                : $"Label column index '{options.LabelColumn}' is not a valid zero-based index for {columnCount} columns."));
        }
        else if (options.IgnoreColumns.Contains(options.LabelColumn, StringComparer.Ordinal))
        {
            issues.Add(Error("LabelColumnIgnored", $"Label column '{options.LabelColumn}' is also listed as an ignored column."));
        }

        if (options.Scenario == MlnetScenario.Recommendation)
        {
            var header = hasHeader ? first.Split(delimiter) : null;
            foreach (var (code, kind, column) in new[] { ("UserColumnNotFound", "User", options.UserColumn), ("ItemColumnNotFound", "Item", options.ItemColumn) })
                if (!string.IsNullOrWhiteSpace(column) && ResolveLabelIndex(column, header, columnCount) is null)
                    issues.Add(Error(code, $"{kind} column '{column}' was not found in the dataset ({columnCount} columns)."));
        }

        if (options.Scenario == MlnetScenario.Forecasting &&
            !string.IsNullOrWhiteSpace(options.TimeColumn) &&
            ResolveLabelIndex(options.TimeColumn, hasHeader ? first.Split(delimiter) : null, columnCount) is null)
            issues.Add(Error("TimeColumnNotFound", $"Time column '{options.TimeColumn}' was not found in the dataset ({columnCount} columns)."));

        var rows = 0L;
        var raggedRows = 0L;
        long? firstRaggedLine = null;
        var distinctLabels = new HashSet<string>(StringComparer.Ordinal);
        string? nonNumericLabel = null;
        long nonNumericLine = 0;
        var lineNumber = hasHeader ? 1L : 0L;

        // Without a header the first line is data and must be processed like the rest.
        var pending = hasHeader ? null : first;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = pending ?? await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            pending = null;
            if (line is null) break;
            lineNumber++;
            if (line.Length == 0) continue;

            rows++;
            var fields = line.Split(delimiter);
            if (fields.Length != columnCount)
            {
                raggedRows++;
                firstRaggedLine ??= lineNumber;
            }

            if (labelIndex is { } li && li < fields.Length)
            {
                var value = fields[li].Trim();
                if (options.Scenario == MlnetScenario.Classification)
                {
                    if (distinctLabels.Count < 2) distinctLabels.Add(value);
                }
                else if (nonNumericLabel is null && value.Length > 0 &&
                         !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    nonNumericLabel = value;
                    nonNumericLine = lineNumber;
                }
            }
        }

        if (rows == 0)
        {
            issues.Add(Error("NoDataRows", "Dataset contains a header but no data rows."));
            return;
        }

        if (rows < MinRecommendedRows)
            issues.Add(Warning("FewRows", $"Dataset has only {rows} rows; results are unlikely to be meaningful (at least {MinRecommendedRows} recommended)."));

        if (options.SplitRatio is { } ratio && ratio > 0 && ratio < 1)
        {
            var validationRows = rows * ratio;
            var trainingRows = rows * (1 - ratio);
            if (validationRows < 1 || trainingRows < 1)
                issues.Add(Error("SplitLeavesNoRows", $"Split ratio {ratio.ToString(CultureInfo.InvariantCulture)} leaves no rows for {(validationRows < 1 ? "validation" : "training")} out of {rows} rows."));
            else if (validationRows < MinSplitRows || trainingRows < MinSplitRows)
                issues.Add(Warning("SplitTooSmall", $"Split ratio {ratio.ToString(CultureInfo.InvariantCulture)} leaves only {Math.Floor(Math.Min(validationRows, trainingRows))} rows on the smaller side; metrics will be unreliable."));
        }

        if (options.CvFolds is >= 2 and var folds)
        {
            if (folds > rows)
                issues.Add(Error("CvFoldsExceedRows", $"{folds} cross-validation folds requested but the dataset has only {rows} rows."));
            else if (rows / folds < MinSplitRows)
                issues.Add(Warning("CvFoldsTooSmall", $"{folds} folds over {rows} rows gives about {rows / folds} rows per fold; metrics will be unreliable."));
        }

        if (options.ValidationDatasetPath is { } validationPath && File.Exists(validationPath))
        {
            using var validationReader = new StreamReader(validationPath);
            var validationFirst = await NextNonBlankLineAsync(validationReader, cancellationToken).ConfigureAwait(false);
            if (validationFirst is null)
                issues.Add(Error("ValidationDatasetEmpty", "Validation dataset file is empty."));
            else if (hasHeader && !SameHeader(first, delimiter, validationFirst))
                issues.Add(Error("ValidationDatasetColumnMismatch", "Validation dataset header does not match the training dataset header."));
            else if (!hasHeader && validationFirst.Split(delimiter).Length != columnCount)
                issues.Add(Error("ValidationDatasetColumnMismatch", $"Validation dataset does not have {columnCount} columns like the training dataset."));
        }

        if (raggedRows > 0)
            issues.Add(Warning("RaggedRows", $"{raggedRows} row(s) do not have {columnCount} columns (first at line {firstRaggedLine}); mlnet will not report this."));

        if (labelIndex is not null)
        {
            if (options.Scenario == MlnetScenario.Classification && distinctLabels.Count < 2)
                issues.Add(Error("SingleClassLabel", $"Label column '{options.LabelColumn}' has fewer than two distinct values; classification needs at least two classes."));

            if (nonNumericLabel is not null)
                issues.Add(Error("NonNumericLabel", $"Label column '{options.LabelColumn}' must be numeric for regression, but line {nonNumericLine} contains '{nonNumericLabel}'."));
        }
    }

    private static int? ResolveLabelIndex(string label, string[]? header, int columnCount)
    {
        if (header is not null)
        {
            var index = Array.FindIndex(header, h => h.Trim() == label);
            if (index >= 0) return index;
        }

        return int.TryParse(label, NumberStyles.None, CultureInfo.InvariantCulture, out var i) && i < columnCount
            ? i
            : null;
    }

    private static bool SameHeader(string trainingHeader, char delimiter, string validationHeader) =>
        trainingHeader.Split(delimiter).Select(h => h.Trim()).SequenceEqual(validationHeader.Split(delimiter).Select(h => h.Trim()), StringComparer.Ordinal);

    private static char DetectDelimiter(string line) =>
        Delimiters.MaxBy(d => line.Count(c => c == d));

    private static async Task<string?> NextNonBlankLineAsync(StreamReader reader, CancellationToken ct)
    {
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            if (line.Length > 0) return line;
        return null;
    }

    private static ValidationIssue Error(string code, string message) => new(ValidationSeverity.Error, code, message);

    private static ValidationIssue Warning(string code, string message) => new(ValidationSeverity.Warning, code, message);
}
