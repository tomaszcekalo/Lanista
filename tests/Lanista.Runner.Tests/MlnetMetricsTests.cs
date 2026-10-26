using Lanista.Runner;

namespace Lanista.Runner.Tests;

public class MlnetMetricsTests
{
    [Theory]
    [InlineData("MacroAccuracy", MetricDirection.HigherIsBetter)]
    [InlineData("RSquared", MetricDirection.HigherIsBetter)]
    [InlineData("rsquared", MetricDirection.HigherIsBetter)]
    [InlineData("AreaUnderRocCurve", MetricDirection.HigherIsBetter)]
    [InlineData("RMSE", MetricDirection.LowerIsBetter)]
    [InlineData("MeanAbsoluteError", MetricDirection.LowerIsBetter)]
    [InlineData("LogLoss", MetricDirection.LowerIsBetter)]
    public void Known_metrics_have_a_direction(string metric, MetricDirection expected) =>
        Assert.Equal(expected, MlnetMetrics.DirectionOf(metric));

    [Theory]
    [InlineData("Evaluate Metric: Accuracy", "Accuracy", "|0     FastTreeBinary                      0.9286     0.2200         |", "FastTreeBinary", 0.9286)]
    [InlineData("Evaluate Metric: MacroAccuracy", "MacroAccuracy", "|1     LbfgsMaximumEntropyMulti            0.8634     0.0660         |", "LbfgsMaximumEntropyMulti", 0.8634)]
    public void Metrics_seen_in_real_classification_runs_parse_and_have_a_direction(string declaration, string metric, string row, string trainer, double value)
    {
        var parser = new MlnetOutputParser();
        var declared = Assert.IsType<MetricDeclared>(parser.Parse(declaration));
        Assert.Equal(metric, declared.Metric);
        Assert.Equal(MetricDirection.HigherIsBetter, declared.Direction);
        Assert.Equal(new TrialCompleted(row.Contains("|0 ") ? 0 : 1, trainer, value, row.Contains("0.2200") ? 0.22 : 0.066), parser.Parse(row));
    }

    [Fact]
    public void Unknown_metric_has_no_direction() => Assert.Null(MlnetMetrics.DirectionOf("Fancy"));

    [Fact]
    public void MetricDeclared_exposes_the_direction()
    {
        Assert.Equal(MetricDirection.LowerIsBetter, new MetricDeclared("RMSE").Direction);
        Assert.Null(new MetricDeclared("Fancy").Direction);
    }

    [Fact]
    public void BestOf_picks_the_lowest_for_lower_is_better_and_the_highest_otherwise()
    {
        TrialCompleted[] trials =
        [
            new(0, "A", 14.3, 1), new(1, "A", 2.8, 1), new(2, "A", 9.0, 1), new(3, "A", 2.8, 1),
        ];

        Assert.Equal(1, MlnetMetrics.BestOf(trials, MetricDirection.LowerIsBetter)!.TrialId);
        Assert.Equal(0, MlnetMetrics.BestOf(trials, MetricDirection.HigherIsBetter)!.TrialId);
    }

    [Fact]
    public void BestOf_handles_negative_values_nan_and_empty_input()
    {
        TrialCompleted[] trials = [new(0, "A", -1.6, 1), new(1, "A", double.NaN, 1), new(2, "A", -0.45, 1)];

        Assert.Equal(2, MlnetMetrics.BestOf(trials, MetricDirection.HigherIsBetter)!.TrialId);
        Assert.Null(MlnetMetrics.BestOf([], MetricDirection.HigherIsBetter));
    }

    [Fact]
    public void Direction_agrees_with_the_trial_mlnet_marked_as_best()
    {
        var parser = new MlnetOutputParser();
        var trials = new List<TrialCompleted>();
        int? best = null;
        foreach (var line in new[]
        {
            "|0     ForecastBySsa                       14.3458    0.3490         |",
            "found best trial - trial id: 0",
            "|8     ForecastBySsa                       11.2062    0.4600         |",
            "found best trial - trial id: 8",
            "|45    ForecastBySsa                       2.8278     0.0050         |",
            "found best trial - trial id: 45",
            "|46    ForecastBySsa                       14.3458    0.0040         |",
        })
        {
            switch (parser.Parse(line))
            {
                case TrialCompleted t: trials.Add(t); break;
                case BestTrialFound b: best = b.TrialId; break;
            }
        }

        Assert.Equal(best, MlnetMetrics.BestOf(trials, MetricDirection.LowerIsBetter)!.TrialId);
    }
}
