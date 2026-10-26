using Lanista.Runner;

namespace Lanista.Runner.Tests;

public class MlnetOutputParserTests
{
    [Fact]
    public void Parses_metric_declaration()
    {
        var evt = new MlnetOutputParser().Parse("Evaluate Metric: MacroAccuracy");
        Assert.Equal(new MetricDeclared("MacroAccuracy"), evt);
    }

    [Fact]
    public void Parses_trial_row()
    {
        var evt = new MlnetOutputParser().Parse("|5     LightGbmMulti                       0.8078     4.8500         |");
        Assert.Equal(new TrialCompleted(5, "LightGbmMulti", 0.8078, 4.85), evt);
    }

    [Fact]
    public void Parses_trial_row_with_comma_decimals()
    {
        var evt = new MlnetOutputParser().Parse("|12    FastTreeOva                         0,7472     3,2970         |");
        Assert.Equal(new TrialCompleted(12, "FastTreeOva", 0.7472, 3.297), evt);
    }

    [Fact]
    public void Parses_best_trial()
    {
        var evt = new MlnetOutputParser().Parse("found best trial - trial id: 5");
        Assert.Equal(new BestTrialFound(5), evt);
    }

    [Theory]
    [InlineData("|      Trainer                             MacroAccuracy Duration    |")]
    [InlineData("|--------------------------------------------------------------------|")]
    [InlineData("Available Trainers: LGBM,FASTFOREST,FASTTREE,LBFGS,SDCA")]
    [InlineData("")]
    public void Ignores_non_event_lines(string line)
    {
        Assert.Null(new MlnetOutputParser().Parse(line));
    }

    [Fact]
    public void Ignores_trial_rows_repeated_in_final_summary()
    {
        var parser = new MlnetOutputParser();
        Assert.NotNull(parser.Parse("|0     FastTreeOva                         0.7472     3.2970         |"));

        parser.Parse("|                          Experiment Results                        |");

        Assert.Null(parser.Parse("|0     FastTreeOva                         0.7472     3.2970         |"));
    }

    [Fact]
    public void Parses_regression_metric_declaration()
    {
        Assert.Equal(new MetricDeclared("RSquared"), new MlnetOutputParser().Parse("Evaluate Metric: RSquared"));
    }

    [Fact]
    public void Parses_negative_metric_and_long_trainer_name()
    {
        var parser = new MlnetOutputParser();

        Assert.Equal(
            new TrialCompleted(7, "FastTreeRegression", -0.9244, 0.422),
            parser.Parse("|7     FastTreeRegression                  -0.9244    0.4220         |"));
        Assert.Equal(
            new TrialCompleted(5, "LbfgsPoissonRegressionRegression", 0.6743, 1.729),
            parser.Parse("|5     LbfgsPoissonRegressionRegression    0.6743     1.7290         |"));
    }

    [Theory]
    [InlineData("System.ArgumentException: Cannot find column nolabel in the data source",
        "System.ArgumentException: Cannot find column nolabel in the data source")]
    [InlineData("System.Exception: Unable to parse file. Only comma, tab or semi-colon delimited files are allowed.",
        "System.Exception: Unable to parse file. Only comma, tab or semi-colon delimited files are allowed.")]
    [InlineData("System.TimeoutException: Training time finished without completing a successful trial.",
        "System.TimeoutException: Training time finished without completing a successful trial.")]
    public void Detects_exception_lines(string line, string expected)
    {
        Assert.Equal(new ErrorReported(expected), new MlnetOutputParser().Parse(line));
    }

    [Theory]
    [InlineData("   at Microsoft.ML.CLI.Program.TrainAsync() in /_/src/mlnet/Program.cs:line 428")]
    [InlineData("--- End of stack trace from previous location ---")]
    [InlineData(@"Check out log file for more information: C:\Users\x\mlnet\log.txt")]
    [InlineData("[Source=AutoMLExperiment, Kind=Info] cancel training because cancellation token is invoked...")]
    public void Does_not_treat_stack_frames_or_info_lines_as_errors(string line)
    {
        Assert.Null(new MlnetOutputParser().Parse(line));
    }

    [Fact]
    public void Detects_output_generation_marker()
    {
        var evt = new MlnetOutputParser().Parse(@"Generating a console project for the best pipeline at location : C:\out");
        Assert.IsType<OutputGenerated>(evt);
    }

    [Fact]
    public void Parses_forecasting_output()
    {
        var parser = new MlnetOutputParser();
        Assert.Equal(new MetricDeclared("RMSE"), parser.Parse("Evaluate Metric: RMSE"));
        Assert.Equal(
            new TrialCompleted(45, "ForecastBySsa", 2.8278, 0.005),
            parser.Parse("|45    ForecastBySsa                       2.8278     0.0050         |"));
        Assert.Equal(new BestTrialFound(45), parser.Parse("found best trial - trial id: 45"));
        Assert.Null(parser.Parse("trial fail - trial id: 46"));
    }

    [Fact]
    public void Parses_recommendation_output()
    {
        var parser = new MlnetOutputParser();
        Assert.Equal(new MetricDeclared("RSquared"), parser.Parse("Evaluate Metric: RSquared"));
        Assert.Equal(
            new TrialCompleted(34, "MatrixFactorization", -1.6328, 0.674),
            parser.Parse("|34    MatrixFactorization                 -1.6328    0.6740         |"));
        Assert.Null(parser.Parse("  12    -nan(ind)    -nan(ind)"));
    }
}
