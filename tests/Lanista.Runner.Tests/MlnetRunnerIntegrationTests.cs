using System.Diagnostics;
using System.Globalization;
using System.Text;
using Lanista.Runner;

namespace Lanista.Runner.Tests;

// These tests need the real `mlnet` CLI on PATH and take up to a minute; without it they pass trivially.
public class MlnetRunnerIntegrationTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lanista-it-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Every_validation_strategy_trains_successfully_with_real_mlnet()
    {
        if (!MlnetAvailable()) return;

        var baseOptions = Options(trainTime: 5);
        foreach (var tweak in new Func<MlnetJobOptions, MlnetJobOptions>[]
        {
            o => o with { SplitRatio = 0.2, Name = "Split" },
            o => o with { CvFolds = 3, Name = "Cv" },
            o => o with { ValidationDatasetPath = o.DatasetPath, Name = "Valid" },
        })
        {
            var options = tweak(baseOptions);
            JobFinished? finished = null;
            await foreach (var evt in new MlnetRunner().RunAsync(options))
                if (evt is JobFinished f) finished = f;

            Assert.True(finished?.Succeeded, $"{options.Name}: {finished?.ErrorMessage}");
        }
    }

    [Fact]
    public async Task Streams_trials_while_training_and_finishes_successfully()
    {
        if (!MlnetAvailable()) return;

        var started = Stopwatch.StartNew();
        TimeSpan? firstTrialAt = null;
        var trials = new List<TrialCompleted>();
        JobFinished? finished = null;

        await foreach (var evt in new MlnetRunner().RunAsync(Options(trainTime: 15)))
        {
            if (evt is TrialCompleted t)
            {
                firstTrialAt ??= started.Elapsed;
                trials.Add(t);
            }
            if (evt is JobFinished f) finished = f;
        }

        Assert.NotNull(finished);
        Assert.True(finished.Succeeded);
        Assert.NotEmpty(trials);
        Assert.Equal(trials.Count, trials.Select(t => t.TrialId).Distinct().Count());
        Assert.True(firstTrialAt < started.Elapsed - TimeSpan.FromSeconds(3),
            $"First trial arrived at {firstTrialAt}, total {started.Elapsed}: output was not streamed.");
        Assert.True(Directory.GetFiles(_dir, "*.mlnet", SearchOption.AllDirectories).Length > 0);
    }

    [Fact]
    public async Task Cancellation_kills_the_process_and_reports_cancelled()
    {
        if (!MlnetAvailable()) return;

        using var cts = new CancellationTokenSource();
        JobFinished? finished = null;

        await foreach (var evt in new MlnetRunner().RunAsync(Options(trainTime: 120), cts.Token))
        {
            if (evt is TrialCompleted) cts.Cancel();
            if (evt is JobFinished f) finished = f;
        }

        Assert.NotNull(finished);
        Assert.True(finished.Cancelled);
        Assert.False(finished.Succeeded);
        Assert.False(Directory.Exists(Path.Combine(_dir, "out")), "Output directory created by the run should be removed.");
    }

    [Fact]
    public async Task Cancellation_keeps_a_preexisting_output_directory_and_its_content()
    {
        if (!MlnetAvailable()) return;

        var options = Options(trainTime: 120);
        Directory.CreateDirectory(options.OutputDirectory);
        var marker = Path.Combine(options.OutputDirectory, "keep.txt");
        File.WriteAllText(marker, "keep");

        using var cts = new CancellationTokenSource();
        JobFinished? finished = null;

        await foreach (var evt in new MlnetRunner().RunAsync(options, cts.Token))
        {
            if (evt is TrialCompleted) cts.Cancel();
            if (evt is JobFinished f) finished = f;
        }

        Assert.NotNull(finished);
        Assert.True(finished.Cancelled);
        Assert.True(File.Exists(marker));
        Assert.False(Directory.Exists(Path.Combine(options.OutputDirectory, "Churn")));
    }

    [Fact]
    public async Task Runtime_error_with_exit_code_zero_is_reported_as_failure()
    {
        if (!MlnetAvailable()) return;

        var options = Options(trainTime: 5) with { LabelColumn = "no_such_column" };
        var errors = new List<ErrorReported>();
        JobFinished? finished = null;

        await foreach (var evt in new MlnetRunner(validateInput: false).RunAsync(options))
        {
            if (evt is ErrorReported e) errors.Add(e);
            if (evt is JobFinished f) finished = f;
        }

        Assert.NotNull(finished);
        Assert.Equal(0, finished.ExitCode);
        Assert.False(finished.Succeeded);
        Assert.Contains("Cannot find column no_such_column", finished.ErrorMessage);
        Assert.Single(errors);
    }

    [Fact]
    public async Task Successful_run_has_no_error_message()
    {
        if (!MlnetAvailable()) return;

        JobFinished? finished = null;
        await foreach (var evt in new MlnetRunner().RunAsync(Options(trainTime: 10)))
            if (evt is JobFinished f) finished = f;

        Assert.NotNull(finished);
        Assert.Null(finished.ErrorMessage);
        Assert.True(finished.Succeeded);
    }

    [Fact]
    public async Task Missing_dataset_finishes_with_nonzero_exit_code()
    {
        if (!MlnetAvailable()) return;

        var options = Options(trainTime: 5) with { DatasetPath = Path.Combine(_dir, "nope.csv") };
        JobFinished? finished = null;

        await foreach (var evt in new MlnetRunner(validateInput: false).RunAsync(options))
            if (evt is JobFinished f) finished = f;

        Assert.NotNull(finished);
        Assert.False(finished.Succeeded);
        Assert.NotEqual(0, finished.ExitCode);
        Assert.Contains("File does not exist", finished.ErrorMessage);
    }

    [Fact]
    public async Task Recommendation_scenario_trains_and_generates_a_model()
    {
        if (!MlnetAvailable()) return;

        var dataset = Path.Combine(_dir, "ratings.csv");
        var rng = new Random(1);
        var sb = new StringBuilder("userId,movieId,rating\n");
        for (var i = 0; i < 2000; i++)
            sb.Append($"{rng.Next(1, 51)},{rng.Next(1, 31)},{rng.Next(1, 6)}\n");
        File.WriteAllText(dataset, sb.ToString());

        var options = new MlnetJobOptions
        {
            Scenario = MlnetScenario.Recommendation,
            DatasetPath = dataset,
            UserColumn = "userId",
            ItemColumn = "movieId",
            LabelColumn = "rating",
            OutputDirectory = Path.Combine(_dir, "out"),
            Name = "Rec",
            TrainTimeSeconds = 10,
        };

        var trials = new List<TrialCompleted>();
        MetricDeclared? metric = null;
        JobFinished? finished = null;
        await foreach (var evt in new MlnetRunner().RunAsync(options))
        {
            if (evt is TrialCompleted t) trials.Add(t);
            if (evt is MetricDeclared m) metric = m;
            if (evt is JobFinished f) finished = f;
        }

        Assert.NotNull(finished);
        Assert.True(finished.Succeeded, finished.ErrorMessage);
        Assert.NotEmpty(trials);
        Assert.All(trials, t => Assert.Equal("MatrixFactorization", t.Trainer));
        Assert.Equal("RSquared", metric?.Metric);
        Assert.True(File.Exists(Path.Combine(_dir, "out", "Rec", "Rec.mlnet")));
    }

    [Fact]
    public async Task Forecasting_scenario_trains_and_generates_a_model_in_the_default_project_folder()
    {
        if (!MlnetAvailable()) return;

        var dataset = Path.Combine(_dir, "sales.csv");
        var rng = new Random(2);
        var sb = new StringBuilder("date,sales\n");
        for (var i = 0; i < 400; i++)
        {
            var v = 100 + i * 0.1 + 15 * Math.Sin(2 * Math.PI * i / 7) + (rng.NextDouble() - .5) * 6;
            sb.Append(string.Create(CultureInfo.InvariantCulture, $"{new DateTime(2022, 1, 1).AddDays(i):yyyy-MM-dd},{v:F2}\n"));
        }
        File.WriteAllText(dataset, sb.ToString());

        var options = new MlnetJobOptions
        {
            Scenario = MlnetScenario.Forecasting,
            DatasetPath = dataset,
            LabelColumn = "sales",
            TimeColumn = "date",
            Horizon = 7,
            OutputDirectory = Path.Combine(_dir, "out"),
            TrainTimeSeconds = 10,
        };

        var trials = new List<TrialCompleted>();
        MetricDeclared? metric = null;
        JobFinished? finished = null;
        await foreach (var evt in new MlnetRunner().RunAsync(options))
        {
            if (evt is TrialCompleted t) trials.Add(t);
            if (evt is MetricDeclared m) metric = m;
            if (evt is JobFinished f) finished = f;
        }

        Assert.NotNull(finished);
        Assert.True(finished.Succeeded, finished.ErrorMessage);
        Assert.NotEmpty(trials);
        Assert.All(trials, t => Assert.Equal("ForecastBySsa", t.Trainer));
        Assert.Equal("RMSE", metric?.Metric);
        Assert.True(File.Exists(Path.Combine(_dir, "out", options.ProjectName, options.ProjectName + ".mlnet")));
    }

    [Fact]
    public async Task Train_regenerates_the_project_from_an_mbconfig()
    {
        if (!MlnetAvailable()) return;

        var options = Options(trainTime: 5);
        await foreach (var _ in new MlnetRunner().RunAsync(options)) { }
        var config = Path.Combine(options.OutputDirectory, "Churn", "Churn.mbconfig");
        Assert.True(File.Exists(config));
        var before = File.ReadAllText(config);

        var oldModel = File.ReadAllBytes(Path.Combine(options.OutputDirectory, "Churn", "Churn.mlnet"));

        var trials = new List<TrialCompleted>();
        ProjectBackedUp? backedUp = null;
        JobFinished? finished = null;
        await foreach (var evt in new MlnetRunner().RunAsync(new MlnetTrainOptions { TrainingConfigPath = config }))
        {
            if (evt is TrialCompleted t) trials.Add(t);
            if (evt is ProjectBackedUp b) backedUp = b;
            if (evt is JobFinished f) finished = f;
        }

        Assert.NotNull(backedUp);
        Assert.Equal(before, File.ReadAllText(Path.Combine(backedUp.Path, "Churn.mbconfig")));
        Assert.Equal(oldModel, File.ReadAllBytes(Path.Combine(backedUp.Path, "Churn.mlnet")));

        Assert.NotNull(finished);
        Assert.True(finished.Succeeded, finished.ErrorMessage);
        Assert.NotEmpty(trials);
        Assert.True(File.Exists(Path.Combine(options.OutputDirectory, "Churn", "Churn.mlnet")));
        Assert.NotEqual(before, File.ReadAllText(config));
    }

    [Fact]
    public async Task Cancelled_train_leaves_the_existing_project_untouched()
    {
        if (!MlnetAvailable()) return;

        var options = Options(trainTime: 5);
        await foreach (var _ in new MlnetRunner().RunAsync(options)) { }
        var project = Path.Combine(options.OutputDirectory, "Churn");
        var config = Path.Combine(project, "Churn.mbconfig");
        var before = File.ReadAllText(config);

        // Re-train with a long budget by editing the config's TrainingTime.
        File.WriteAllText(config, before.Replace("\"TrainingTime\": 5", "\"TrainingTime\": 120"));
        var edited = File.ReadAllText(config);
        Assert.NotEqual(before, edited);

        using var cts = new CancellationTokenSource();
        JobFinished? finished = null;
        await foreach (var evt in new MlnetRunner().RunAsync(new MlnetTrainOptions { TrainingConfigPath = config }, cts.Token))
        {
            if (evt is TrialCompleted) cts.Cancel();
            if (evt is JobFinished f) finished = f;
        }

        Assert.NotNull(finished);
        Assert.True(finished.Cancelled);
        Assert.Equal(edited, File.ReadAllText(config));
        Assert.True(File.Exists(Path.Combine(project, "Churn.mlnet")));
        Assert.Empty(Directory.GetDirectories(options.OutputDirectory, "Churn.backup-*"));
    }

    [Fact]
    public async Task Train_without_backup_creates_no_backup_folder()
    {
        if (!MlnetAvailable()) return;

        var options = Options(trainTime: 5);
        await foreach (var _ in new MlnetRunner().RunAsync(options)) { }
        var config = Path.Combine(options.OutputDirectory, "Churn", "Churn.mbconfig");

        var events = new List<MlnetEvent>();
        await foreach (var evt in new MlnetRunner().RunAsync(new MlnetTrainOptions { TrainingConfigPath = config, BackupPreviousProject = false }))
            events.Add(evt);

        Assert.DoesNotContain(events, e => e is ProjectBackedUp);
        Assert.True(events.OfType<JobFinished>().Single().Succeeded);
        Assert.Empty(Directory.GetDirectories(options.OutputDirectory, "Churn.backup-*"));
    }

    private MlnetJobOptions Options(int trainTime)
    {
        var dataset = Path.Combine(_dir, "churn.csv");
        if (!File.Exists(dataset)) File.WriteAllText(dataset, GenerateCsv());

        return new MlnetJobOptions
        {
            Scenario = MlnetScenario.Classification,
            DatasetPath = dataset,
            LabelColumn = "churned",
            OutputDirectory = Path.Combine(_dir, "out"),
            Name = "Churn",
            TrainTimeSeconds = trainTime,
        };
    }

    private static string GenerateCsv()
    {
        var rng = new Random(1);
        var sb = new StringBuilder("age,tenure,monthly,plan,churned\n");
        string[] plans = ["basic", "pro", "team"];
        for (var i = 0; i < 600; i++)
        {
            var age = rng.Next(18, 71);
            var tenure = rng.Next(1, 73);
            var monthly = Math.Round(10 + rng.NextDouble() * 110, 2);
            var plan = plans[rng.Next(plans.Length)];
            var p = 0.15 + (monthly > 80 ? 0.5 : 0) + (tenure < 12 ? 0.2 : 0) - (plan == "team" ? 0.1 : 0);
            sb.Append(string.Create(CultureInfo.InvariantCulture, $"{age},{tenure},{monthly},{plan},{(rng.NextDouble() < p ? 1 : 0)}\n"));
        }
        return sb.ToString();
    }

    private static bool MlnetAvailable()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("mlnet", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            })!;
            p.WaitForExit(15_000);
            return p.HasExited && p.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
