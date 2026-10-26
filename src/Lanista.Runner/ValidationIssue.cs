namespace Lanista.Runner;

public enum ValidationSeverity
{
    Warning,
    Error,
}

public sealed record ValidationIssue(ValidationSeverity Severity, string Code, string Message);
