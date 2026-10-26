using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Lanista.Runner;

public sealed class MlnetRunner(string executable = "mlnet", bool validateInput = true)
{
    public IAsyncEnumerable<MlnetEvent> RunAsync(MlnetJobOptions options, CancellationToken cancellationToken = default)
    {
        var outputDirectoryExisted = false;
        var projectDirectoryExisted = false;

        return RunCoreAsync(
            validate: ct => MlnetInputValidator.ValidateAsync(options, ct),
            prepare: () =>
            {
                outputDirectoryExisted = Directory.Exists(options.OutputDirectory);
                projectDirectoryExisted = Directory.Exists(Path.Combine(options.OutputDirectory, options.ProjectName));
                // mlnet exits with "Directory does not exist" instead of creating the output folder itself.
                Directory.CreateDirectory(options.OutputDirectory);
                return [];
            },
            arguments: options.ToArguments(),
            cleanupAfterCancel: () => CancelCleanup.Run(
                options.OutputDirectory, options.ProjectName, outputDirectoryExisted, projectDirectoryExisted),
            cancellationToken);
    }

    // Train overwrites the project next to the .mbconfig in place, so by default the project is copied
    // aside first. mlnet writes its outputs only at the end, so a cancelled run changes nothing and the
    // backup is removed again; after a failed run the backup is kept.
    public IAsyncEnumerable<MlnetEvent> RunAsync(MlnetTrainOptions options, CancellationToken cancellationToken = default)
    {
        string? backup = null;

        return RunCoreAsync(
            validate: ct => MlnetInputValidator.ValidateAsync(options, ct),
            prepare: !options.BackupPreviousProject
                ? null
                : () =>
                {
                    backup = ProjectBackup.Create(Path.GetDirectoryName(Path.GetFullPath(options.TrainingConfigPath))!);
                    return [new ProjectBackedUp(backup)];
                },
            arguments: options.ToArguments(),
            cleanupAfterCancel: () => backup is not null && ProjectBackup.TryRemove(backup) ? [backup] : [],
            cancellationToken);
    }

    private async IAsyncEnumerable<MlnetEvent> RunCoreAsync(
        Func<CancellationToken, Task<IReadOnlyList<ValidationIssue>>> validate,
        Func<IReadOnlyList<MlnetEvent>>? prepare,
        IReadOnlyList<string> arguments,
        Func<IReadOnlyList<string>>? cleanupAfterCancel,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IReadOnlyList<ValidationIssue> issues = [];
        var cancelledDuringValidation = false;
        try
        {
            if (validateInput)
                issues = await validate(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancelledDuringValidation = true;
        }

        if (cancelledDuringValidation)
        {
            yield return new JobFinished(null, true);
            yield break;
        }

        foreach (var issue in issues) yield return new ValidationReported(issue);
        if (issues.FirstOrDefault(i => i.Severity == ValidationSeverity.Error) is { } blocking)
        {
            yield return new JobFinished(null, false, blocking.Message);
            yield break;
        }

        if (prepare is not null)
            foreach (var evt in prepare()) yield return evt;

        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in arguments) startInfo.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = startInfo };
        var channel = Channel.CreateUnbounded<MlnetEvent>(new UnboundedChannelOptions { SingleReader = true });
        var parser = new MlnetOutputParser();
        var gate = new object();
        string? errorMessage = null;
        string? firstStderrLine = null;
        var outputGenerated = false;
        string? metric = null;
        var trials = new List<TrialCompleted>();

        void OnLine(string? line, bool isError)
        {
            if (line is null) return;
            lock (gate)
            {
                channel.Writer.TryWrite(new LogEvent(line, isError));
                if (isError)
                {
                    if (!string.IsNullOrWhiteSpace(line)) firstStderrLine ??= line.Trim();
                    return;
                }

                if (parser.Parse(line) is not { } parsed) return;

                // With -v diag mlnet prints the same exception twice.
                if (parsed is ErrorReported e)
                {
                    if (errorMessage == e.Message) return;
                    errorMessage ??= e.Message;
                }
                else if (parsed is OutputGenerated)
                {
                    outputGenerated = true;
                }
                else if (parsed is MetricDeclared m)
                {
                    metric = m.Metric;
                }
                else if (parsed is TrialCompleted t)
                {
                    trials.Add(t);
                }

                channel.Writer.TryWrite(parsed);
            }
        }

        process.OutputDataReceived += (_, e) => OnLine(e.Data, false);
        process.ErrorDataReceived += (_, e) => OnLine(e.Data, true);

        process.Start();
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var cancelled = false;
        var completion = Task.Run(async () =>
        {
            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // If the process finished just before the cancellation, report its real result.
                if (!process.HasExited)
                {
                    cancelled = true;
                    TryKill(process);
                }

                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }

            // Parameterless WaitForExit lets the async output readers drain before we report completion.
            process.WaitForExit();
            lock (gate)
            {
                if (cancelled && cleanupAfterCancel is not null)
                {
                    var removed = cleanupAfterCancel();
                    foreach (var path in removed)
                        channel.Writer.TryWrite(new LogEvent($"Removed partial output: {path}", false));
                }

                int? exitCode = cancelled ? null : process.ExitCode;
                string? error = null;
                if (!cancelled)
                {
                    error = errorMessage
                        ?? (exitCode != 0 ? firstStderrLine ?? $"mlnet exited with code {exitCode}" : null)
                        ?? (outputGenerated ? null : "mlnet finished without generating any output");
                }

                if (!cancelled && error is null)
                    foreach (var warning in ModelQuality.Assess(metric, trials))
                        channel.Writer.TryWrite(warning);

                channel.Writer.TryWrite(new JobFinished(exitCode, cancelled, error));
                channel.Writer.Complete();
            }
        }, CancellationToken.None);

        try
        {
            await foreach (var evt in channel.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
                yield return evt;
        }
        finally
        {
            if (!completion.IsCompleted) TryKill(process);
            await completion.ConfigureAwait(false);
        }
    }

    private static void TryKill(Process process)
    {
        try { process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
    }
}
