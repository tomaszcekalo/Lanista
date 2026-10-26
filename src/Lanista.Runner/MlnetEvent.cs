namespace Lanista.Runner;

public abstract record MlnetEvent;

public sealed record LogEvent(string Line, bool IsError) : MlnetEvent;

public sealed record MetricDeclared(string Metric) : MlnetEvent
{
    /// <summary>Whether higher or lower values are better; null for a metric this library does not know.</summary>
    public MetricDirection? Direction => MlnetMetrics.DirectionOf(Metric);
}

public sealed record TrialCompleted(int TrialId, string Trainer, double Metric, double DurationSeconds) : MlnetEvent;

public sealed record BestTrialFound(int TrialId) : MlnetEvent;

public sealed record ValidationReported(ValidationIssue Issue) : MlnetEvent;

/// <summary>A successful run whose result is probably not useful; emitted just before JobFinished.</summary>
public sealed record ModelQualityWarning(string Code, string Message) : MlnetEvent;

public sealed record ErrorReported(string Message) : MlnetEvent;

public sealed record OutputGenerated : MlnetEvent;

public sealed record ProjectBackedUp(string Path) : MlnetEvent;

// mlnet exits 0 after runtime failures, so success also requires that no error was detected.
public sealed record JobFinished(int? ExitCode, bool Cancelled, string? ErrorMessage = null) : MlnetEvent
{
    public bool Succeeded => !Cancelled && ExitCode == 0 && ErrorMessage is null;
}
