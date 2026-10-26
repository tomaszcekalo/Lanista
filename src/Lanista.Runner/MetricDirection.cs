namespace Lanista.Runner;

public enum MetricDirection
{
    HigherIsBetter,
    LowerIsBetter,
}

public static class MlnetMetrics
{
    private static readonly Dictionary<string, MetricDirection> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MacroAccuracy"] = MetricDirection.HigherIsBetter,
        ["MicroAccuracy"] = MetricDirection.HigherIsBetter,
        ["Accuracy"] = MetricDirection.HigherIsBetter,
        ["TopKAccuracy"] = MetricDirection.HigherIsBetter,
        ["LogLossReduction"] = MetricDirection.HigherIsBetter,
        ["AreaUnderRocCurve"] = MetricDirection.HigherIsBetter,
        ["AreaUnderPrecisionRecallCurve"] = MetricDirection.HigherIsBetter,
        ["AUC"] = MetricDirection.HigherIsBetter,
        ["AUPRC"] = MetricDirection.HigherIsBetter,
        ["F1Score"] = MetricDirection.HigherIsBetter,
        ["RSquared"] = MetricDirection.HigherIsBetter,

        ["LogLoss"] = MetricDirection.LowerIsBetter,
        ["RMSE"] = MetricDirection.LowerIsBetter,
        ["RootMeanSquaredError"] = MetricDirection.LowerIsBetter,
        ["MSE"] = MetricDirection.LowerIsBetter,
        ["MeanSquaredError"] = MetricDirection.LowerIsBetter,
        ["MAE"] = MetricDirection.LowerIsBetter,
        ["MeanAbsoluteError"] = MetricDirection.LowerIsBetter,
    };

    /// <summary>The direction of a metric named by mlnet, or null when the name is not known.</summary>
    public static MetricDirection? DirectionOf(string metric) =>
        Known.TryGetValue(metric, out var direction) ? direction : null;

    public static bool IsBetter(this MetricDirection direction, double candidate, double current) =>
        direction == MetricDirection.HigherIsBetter ? candidate > current : candidate < current;

    /// <summary>The best trial by the given direction; the earliest wins ties, as in mlnet.</summary>
    public static TrialCompleted? BestOf(IEnumerable<TrialCompleted> trials, MetricDirection direction)
    {
        TrialCompleted? best = null;
        foreach (var trial in trials)
        {
            if (double.IsNaN(trial.Metric)) continue;
            if (best is null || direction.IsBetter(trial.Metric, best.Metric)) best = trial;
        }
        return best;
    }
}
