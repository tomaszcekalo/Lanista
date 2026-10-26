namespace Lanista.Runner;

internal static class ModelQuality
{
    public static IReadOnlyList<ModelQualityWarning> Assess(string? metric, IReadOnlyList<TrialCompleted> trials)
    {
        if (trials.Count == 0)
            return [new("NoTrialsReported", "mlnet reported no completed trials, so there is no metric to judge the model by; try a longer training time.")];

        if (metric is null || MlnetMetrics.DirectionOf(metric) is not { } direction) return [];
        if (MlnetMetrics.BestOf(trials, direction) is not { } best) return [];

        if (string.Equals(metric, "RSquared", StringComparison.OrdinalIgnoreCase) && best.Metric < 0)
            return [new("NegativeRSquared", $"Best RSquared is {best.Metric.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}: the model predicts worse than always guessing the mean. Try a longer training time or check the data.")];

        return [];
    }
}
