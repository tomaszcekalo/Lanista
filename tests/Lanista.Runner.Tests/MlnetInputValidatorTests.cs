using System.Text;
using Lanista.Runner;

namespace Lanista.Runner.Tests;

public class MlnetInputValidatorTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lanista-val-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Valid_classification_dataset_has_no_issues()
    {
        var issues = await Validate(Csv(rows: 200, label: i => (i % 2).ToString()));
        Assert.Empty(issues);
    }

    [Fact]
    public async Task Valid_regression_dataset_has_no_issues()
    {
        var issues = await Validate(Csv(rows: 200, label: i => (i * 1.5).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            o => o with { Scenario = MlnetScenario.Regression });
        Assert.Empty(issues);
    }

    [Fact]
    public async Task Missing_dataset_is_an_error()
    {
        var issues = await Validate(Path.Combine(_dir, "nope.csv"));
        Assert.Equal(["DatasetNotFound"], Errors(issues));
    }

    [Fact]
    public async Task Directory_as_dataset_is_an_error()
    {
        var issues = await Validate(_dir);
        Assert.Equal(["DatasetNotFound"], Errors(issues));
    }

    [Fact]
    public async Task Empty_file_is_an_error()
    {
        var issues = await Validate(Write("empty.csv", ""));
        Assert.Equal(["DatasetEmpty"], Errors(issues));
    }

    [Fact]
    public async Task Header_only_file_is_an_error()
    {
        var issues = await Validate(Write("header.csv", "age,tenure,churned\n"));
        Assert.Equal(["NoDataRows"], Errors(issues));
    }

    [Fact]
    public async Task Unknown_label_column_is_an_error()
    {
        var issues = await Validate(Csv(200, i => (i % 2).ToString()), o => o with { LabelColumn = "nolabel" });
        Assert.Equal(["LabelColumnNotFound"], Errors(issues));
    }

    [Fact]
    public async Task Label_can_be_a_zero_based_index()
    {
        var issues = await Validate(Csv(200, i => (i % 2).ToString()), o => o with { LabelColumn = "2" });
        Assert.Empty(Errors(issues));
    }

    [Fact]
    public async Task Label_index_out_of_range_is_an_error()
    {
        var issues = await Validate(Csv(200, i => (i % 2).ToString()), o => o with { LabelColumn = "9" });
        Assert.Equal(["LabelColumnNotFound"], Errors(issues));
    }

    [Fact]
    public async Task Label_that_is_also_ignored_is_an_error()
    {
        var issues = await Validate(Csv(200, i => (i % 2).ToString()), o => o with { IgnoreColumns = ["churned"] });
        Assert.Equal(["LabelColumnIgnored"], Errors(issues));
    }

    [Fact]
    public async Task Single_class_label_is_an_error_for_classification()
    {
        var issues = await Validate(Csv(200, _ => "1"));
        Assert.Equal(["SingleClassLabel"], Errors(issues));
    }

    [Fact]
    public async Task Text_label_is_an_error_for_regression()
    {
        var issues = await Validate(Csv(200, i => i % 2 == 0 ? "yes" : "no"), o => o with { Scenario = MlnetScenario.Regression });
        var error = Assert.Single(issues, i => i.Severity == ValidationSeverity.Error);
        Assert.Equal("NonNumericLabel", error.Code);
        Assert.Contains("'yes'", error.Message);
    }

    [Fact]
    public async Task Text_label_is_fine_for_classification()
    {
        var issues = await Validate(Csv(200, i => i % 2 == 0 ? "yes" : "no"));
        Assert.Empty(issues);
    }

    [Fact]
    public async Task Semicolon_delimited_file_is_supported()
    {
        var path = Write("semi.csv", string.Join("\n", Enumerable.Range(0, 200).Select(i => i == 0 ? "age;churned" : $"{i};{i % 2}")));
        var issues = await Validate(path);
        Assert.Empty(issues);
    }

    [Fact]
    public async Task Few_rows_is_a_warning_not_an_error()
    {
        var issues = await Validate(Csv(5, i => (i % 2).ToString()));
        Assert.Empty(Errors(issues));
        Assert.Contains(issues, i => i.Code == "FewRows" && i.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public async Task Ragged_rows_are_a_warning_with_the_first_line_number()
    {
        var lines = Enumerable.Range(0, 200).Select(i => i == 0 ? "age,tenure,churned" : i == 50 ? "1,2" : $"1,2,{i % 2}");
        var issues = await Validate(Write("ragged.csv", string.Join("\n", lines)));

        Assert.Empty(Errors(issues));
        var warning = Assert.Single(issues, i => i.Code == "RaggedRows");
        Assert.Contains("line 51", warning.Message);
    }

    [Fact]
    public async Task Headerless_dataset_counts_the_first_line_as_data()
    {
        var path = Write("noheader.csv", string.Join("\n", Enumerable.Range(0, 200).Select(i => $"{i},{i % 2}")));
        var issues = await Validate(path, o => o with { HasHeader = false, LabelColumn = "1" });
        Assert.Empty(issues);
    }

    [Fact]
    public async Task Non_positive_train_time_is_an_error()
    {
        var issues = await Validate(Csv(200, i => (i % 2).ToString()), o => o with { TrainTimeSeconds = 0 });
        Assert.Equal(["InvalidTrainTime"], Errors(issues));
    }

    [Fact]
    public async Task Output_path_that_is_a_file_is_an_error()
    {
        var outFile = Write("outfile", "x");
        var issues = await Validate(Csv(200, i => (i % 2).ToString()), o => o with { OutputDirectory = outFile });
        Assert.Equal(["OutputIsFile"], Errors(issues));
    }

    [Fact]
    public async Task Runner_does_not_start_mlnet_when_validation_fails()
    {
        // The executable does not exist: if the runner tried to start it, Process.Start would throw.
        var runner = new MlnetRunner("lanista-no-such-executable");
        var options = Options(Path.Combine(_dir, "nope.csv"));
        var events = new List<MlnetEvent>();

        await foreach (var evt in runner.RunAsync(options)) events.Add(evt);

        Assert.Contains(events, e => e is ValidationReported { Issue.Code: "DatasetNotFound" });
        var finished = Assert.IsType<JobFinished>(events[^1]);
        Assert.False(finished.Succeeded);
        Assert.Contains("does not exist", finished.ErrorMessage);
        Assert.False(Directory.Exists(options.OutputDirectory));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(-0.1)]
    [InlineData(1.5)]
    [InlineData(double.NaN)]
    public async Task Split_ratio_outside_zero_and_one_is_an_error(double ratio)
    {
        var issues = await Validate(Csv(200, i => (i % 2).ToString()), o => o with { SplitRatio = ratio });
        Assert.Equal(["InvalidSplitRatio"], Errors(issues));
    }

    [Fact]
    public async Task Reasonable_split_ratio_has_no_issues_and_is_passed_to_mlnet()
    {
        var options = Options(Csv(200, i => (i % 2).ToString())) with { SplitRatio = 0.2 };
        Assert.Empty(await MlnetInputValidator.ValidateAsync(options));

        var args = options.ToArguments().ToList();
        Assert.Equal("0.2", args[args.IndexOf("--split-ratio") + 1]);
    }

    [Fact]
    public async Task Split_ratio_that_leaves_no_training_rows_is_an_error()
    {
        var issues = await Validate(Csv(200, i => (i % 2).ToString()), o => o with { SplitRatio = 0.9999 });
        Assert.Equal(["SplitLeavesNoRows"], Errors(issues));
    }

    [Fact]
    public async Task Split_ratio_with_a_tiny_side_is_a_warning()
    {
        var issues = await Validate(Csv(200, i => (i % 2).ToString()), o => o with { SplitRatio = 0.02 });
        Assert.Empty(Errors(issues));
        Assert.Contains(issues, i => i.Code == "SplitTooSmall" && i.Severity == ValidationSeverity.Warning);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-2)]
    public async Task Fewer_than_two_cv_folds_is_an_error(int folds)
    {
        var issues = await Validate(Csv(200, i => (i % 2).ToString()), o => o with { CvFolds = folds });
        Assert.Equal(["InvalidCvFolds"], Errors(issues));
    }

    [Fact]
    public async Task More_cv_folds_than_rows_is_an_error()
    {
        var issues = await Validate(Csv(200, i => (i % 2).ToString()), o => o with { CvFolds = 201 });
        Assert.Equal(["CvFoldsExceedRows"], Errors(issues));
    }

    [Fact]
    public async Task Cv_folds_with_tiny_folds_are_a_warning()
    {
        var issues = await Validate(Csv(200, i => (i % 2).ToString()), o => o with { CvFolds = 50 });
        Assert.Empty(Errors(issues));
        Assert.Contains(issues, i => i.Code == "CvFoldsTooSmall");
    }

    [Fact]
    public async Task Reasonable_cv_folds_have_no_issues_and_are_passed_to_mlnet()
    {
        var options = Options(Csv(200, i => (i % 2).ToString())) with { CvFolds = 5 };
        Assert.Empty(await MlnetInputValidator.ValidateAsync(options));

        var args = options.ToArguments().ToList();
        Assert.Equal("5", args[args.IndexOf("--cv-fold") + 1]);
    }

    [Fact]
    public async Task Validation_strategies_are_mutually_exclusive()
    {
        var data = Csv(200, i => (i % 2).ToString());
        var validation = Write("valid.csv", File.ReadAllText(data));

        foreach (var tweak in new Func<MlnetJobOptions, MlnetJobOptions>[]
        {
            o => o with { SplitRatio = 0.2, CvFolds = 3 },
            o => o with { SplitRatio = 0.2, ValidationDatasetPath = validation },
            o => o with { CvFolds = 3, ValidationDatasetPath = validation },
        })
            Assert.Equal(["ValidationOptionsConflict"], Errors(await Validate(data, tweak)));
    }

    [Fact]
    public async Task Missing_validation_dataset_is_an_error()
    {
        var issues = await Validate(Csv(200, i => (i % 2).ToString()), o => o with { ValidationDatasetPath = Path.Combine(_dir, "gone.csv") });
        Assert.Equal(["ValidationDatasetNotFound"], Errors(issues));
    }

    [Fact]
    public async Task Validation_dataset_with_a_different_header_is_an_error()
    {
        var validation = Write("valid.csv", "age,tenure,other\n1,2,3\n");
        var issues = await Validate(Csv(200, i => (i % 2).ToString()), o => o with { ValidationDatasetPath = validation });
        Assert.Equal(["ValidationDatasetColumnMismatch"], Errors(issues));
    }

    [Fact]
    public async Task Empty_validation_dataset_is_an_error()
    {
        var issues = await Validate(Csv(200, i => (i % 2).ToString()), o => o with { ValidationDatasetPath = Write("valid.csv", "") });
        Assert.Equal(["ValidationDatasetEmpty"], Errors(issues));
    }

    [Fact]
    public async Task Matching_validation_dataset_has_no_issues_and_is_passed_to_mlnet()
    {
        var data = Csv(200, i => (i % 2).ToString());
        var validation = Write("valid.csv", File.ReadAllText(data));
        var options = Options(data) with { ValidationDatasetPath = validation };

        Assert.Empty(await MlnetInputValidator.ValidateAsync(options));
        var args = options.ToArguments().ToList();
        Assert.Equal(validation, args[args.IndexOf("--validation-dataset") + 1]);
    }

    private string Csv(int rows, Func<int, string> label)
    {
        var sb = new StringBuilder("age,tenure,churned\n");
        for (var i = 0; i < rows; i++) sb.Append($"{20 + i % 40},{i % 70},{label(i)}\n");
        return Write("data.csv", sb.ToString());
    }

    [Fact]
    public async Task Valid_recommendation_dataset_has_no_issues()
    {
        var issues = await Validate(Write("ratings.csv", RatingsCsv()), Recommendation);
        Assert.Empty(issues);
    }

    [Fact]
    public async Task Recommendation_requires_user_and_item_columns()
    {
        var issues = await Validate(Write("ratings.csv", RatingsCsv()), o => Recommendation(o) with { UserColumn = null, ItemColumn = " " });
        Assert.Equal(["UserColumnMissing", "ItemColumnMissing"], Errors(issues));
    }

    [Fact]
    public async Task Recommendation_unknown_user_and_item_columns_are_errors()
    {
        var issues = await Validate(Write("ratings.csv", RatingsCsv()), o => Recommendation(o) with { UserColumn = "nobody", ItemColumn = "nothing" });
        Assert.Equal(["UserColumnNotFound", "ItemColumnNotFound"], Errors(issues));
    }

    [Fact]
    public async Task Recommendation_rejects_ignored_columns()
    {
        var issues = await Validate(Write("ratings.csv", RatingsCsv()), o => Recommendation(o) with { IgnoreColumns = ["x"] });
        Assert.Equal(["IgnoreColumnsUnsupported"], Errors(issues));
    }

    [Fact]
    public async Task Recommendation_non_numeric_rating_is_an_error()
    {
        var csv = RatingsCsv() + "1,1,great\n";
        var issues = await Validate(Write("ratings.csv", csv), Recommendation);
        Assert.Equal(["NonNumericLabel"], Errors(issues));
    }

    [Fact]
    public void Recommendation_arguments_use_user_item_and_rating_columns()
    {
        var args = Recommendation(Options("d.csv")).ToArguments();
        Assert.Equal("recommendation", args[0]);
        Assert.Contains("--rating-col", args);
        Assert.Equal("userId", args[args.ToList().IndexOf("--user-col") + 1]);
        Assert.Equal("movieId", args[args.ToList().IndexOf("--item-col") + 1]);
        Assert.DoesNotContain("--label-col", args);
    }

    [Fact]
    public async Task Valid_forecasting_dataset_has_no_issues()
    {
        var issues = await Validate(Write("sales.csv", SalesCsv()), Forecasting);
        Assert.Empty(issues);
    }

    [Fact]
    public async Task Forecasting_requires_time_column_and_positive_horizon()
    {
        var issues = await Validate(Write("sales.csv", SalesCsv()), o => Forecasting(o) with { TimeColumn = null, Horizon = 0 });
        Assert.Equal(["TimeColumnMissing", "InvalidHorizon"], Errors(issues));
    }

    [Fact]
    public async Task Forecasting_unknown_time_column_is_an_error()
    {
        var issues = await Validate(Write("sales.csv", SalesCsv()), o => Forecasting(o) with { TimeColumn = "when" });
        Assert.Equal(["TimeColumnNotFound"], Errors(issues));
    }

    [Fact]
    public async Task Forecasting_rejects_ignored_columns()
    {
        var issues = await Validate(Write("sales.csv", SalesCsv()), o => Forecasting(o) with { IgnoreColumns = ["x"] });
        Assert.Equal(["IgnoreColumnsUnsupported"], Errors(issues));
    }

    [Fact]
    public void Forecasting_arguments_include_time_column_and_horizon()
    {
        var args = Forecasting(Options("d.csv")).ToArguments().ToList();
        Assert.Equal("forecasting", args[0]);
        Assert.Equal("date", args[args.IndexOf("--time-col") + 1]);
        Assert.Equal("7", args[args.IndexOf("--horizon") + 1]);
        Assert.Equal("sales", args[args.IndexOf("--label-col") + 1]);
    }

    [Fact]
    public void Forecasting_default_project_name_follows_the_mlnet_quirk()
    {
        Assert.Equal("SampleClassification", (Forecasting(Options("d.csv")) with { Name = null }).ProjectName);
        Assert.Equal("Sales", (Forecasting(Options("d.csv")) with { Name = "Sales" }).ProjectName);
    }

    [Fact]
    public async Task Train_missing_config_is_an_error()
    {
        var issues = await MlnetInputValidator.ValidateAsync(new MlnetTrainOptions { TrainingConfigPath = Path.Combine(_dir, "nope.mbconfig") });
        Assert.Equal(["ConfigNotFound"], Errors(issues));
    }

    [Fact]
    public async Task Train_non_json_config_is_an_error()
    {
        var issues = await MlnetInputValidator.ValidateAsync(new MlnetTrainOptions { TrainingConfigPath = Write("a.mbconfig", "nope") });
        Assert.Equal(["ConfigInvalid"], Errors(issues));
    }

    [Fact]
    public async Task Train_config_without_scenario_is_an_error()
    {
        var issues = await MlnetInputValidator.ValidateAsync(new MlnetTrainOptions { TrainingConfigPath = Write("a.mbconfig", "{}") });
        Assert.Equal(["ConfigInvalid"], Errors(issues));
    }

    [Fact]
    public async Task Train_config_with_missing_dataset_is_an_error()
    {
        var config = Write("a.mbconfig", """{"Scenario":"Forecasting","DataSource":{"FilePath":"gone.csv"}}""");
        var issues = await MlnetInputValidator.ValidateAsync(new MlnetTrainOptions { TrainingConfigPath = config });
        Assert.Equal(["DatasetNotFound"], Errors(issues));
    }

    [Fact]
    public async Task Train_config_resolves_a_relative_dataset_next_to_the_config()
    {
        Write("data.csv", "a,b\n1,2\n");
        var config = Write("a.mbconfig", """{"Scenario":"Regression","DataSource":{"FilePath":"data.csv"}}""");
        var issues = await MlnetInputValidator.ValidateAsync(new MlnetTrainOptions { TrainingConfigPath = config });
        Assert.Empty(issues);
    }

    [Fact]
    public void Train_arguments_contain_only_the_config_and_logging()
    {
        var args = new MlnetTrainOptions { TrainingConfigPath = "x.mbconfig", LogFilePath = "l.txt" }.ToArguments();
        Assert.Equal(["train", "--training-config", "x.mbconfig", "-v", "diag", "--log-file-path", "l.txt"], args);
    }

    private static MlnetJobOptions Forecasting(MlnetJobOptions o) =>
        o with { Scenario = MlnetScenario.Forecasting, LabelColumn = "sales", TimeColumn = "date", Horizon = 7 };

    private static string SalesCsv()
    {
        var sb = new StringBuilder("date,sales\n");
        for (var i = 0; i < 200; i++) sb.Append($"{new DateTime(2022, 1, 1).AddDays(i):yyyy-MM-dd},{100 + i % 7}\n");
        return sb.ToString();
    }

    private static MlnetJobOptions Recommendation(MlnetJobOptions o) =>
        o with { Scenario = MlnetScenario.Recommendation, LabelColumn = "rating", UserColumn = "userId", ItemColumn = "movieId" };

    private static string RatingsCsv()
    {
        var sb = new StringBuilder("userId,movieId,rating\n");
        for (var i = 0; i < 200; i++) sb.Append($"{i % 20},{i % 7},{1 + i % 5}\n");
        return sb.ToString();
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private MlnetJobOptions Options(string dataset) => new()
    {
        Scenario = MlnetScenario.Classification,
        DatasetPath = dataset,
        LabelColumn = "churned",
        OutputDirectory = Path.Combine(_dir, "out"),
    };

    private Task<IReadOnlyList<ValidationIssue>> Validate(string dataset, Func<MlnetJobOptions, MlnetJobOptions>? tweak = null)
    {
        var options = Options(dataset);
        return MlnetInputValidator.ValidateAsync(tweak?.Invoke(options) ?? options);
    }

    private static string[] Errors(IEnumerable<ValidationIssue> issues) =>
        issues.Where(i => i.Severity == ValidationSeverity.Error).Select(i => i.Code).ToArray();
}
