using Lanista.Runner;

namespace Lanista.Runner.Tests;

public class ModelQualityTests
{
    private static TrialCompleted Trial(int id, double metric) => new(id, "MatrixFactorization", metric, 1);

    [Fact]
    public void No_trials_is_a_warning_whatever_the_metric()
    {
        var warning = Assert.Single(ModelQuality.Assess("RSquared", []));
        Assert.Equal("NoTrialsReported", warning.Code);
        Assert.Single(ModelQuality.Assess(null, []));
    }

    [Fact]
    public void Movielens_like_run_with_negative_best_rsquared_is_a_warning()
    {
        // Real run: ml-latest-small, recommendation, 60 s, best RSquared -0.45.
        var warning = Assert.Single(ModelQuality.Assess("RSquared", [Trial(115, -0.62), Trial(118, -0.45), Trial(120, -0.51)]));
        Assert.Equal("NegativeRSquared", warning.Code);
        Assert.Contains("-0.45", warning.Message);
    }

    [Fact]
    public void Positive_rsquared_is_fine()
    {
        Assert.Empty(ModelQuality.Assess("RSquared", [Trial(1, -0.2), Trial(2, 0.4)]));
    }

    [Fact]
    public void Only_the_best_trial_counts_and_nan_is_skipped()
    {
        Assert.Empty(ModelQuality.Assess("RSquared", [Trial(1, double.NaN), Trial(2, 0.1)]));
        Assert.Single(ModelQuality.Assess("RSquared", [Trial(1, double.NaN), Trial(2, -0.1)]));
    }

    [Fact]
    public void Other_metrics_and_unknown_names_are_not_judged()
    {
        Assert.Empty(ModelQuality.Assess("RMSE", [Trial(1, 5.0)]));
        Assert.Empty(ModelQuality.Assess("MacroAccuracy", [Trial(1, 0.0)]));
        Assert.Empty(ModelQuality.Assess("SomethingNew", [Trial(1, -1)]));
        Assert.Empty(ModelQuality.Assess(null, [Trial(1, -1)]));
    }
}
