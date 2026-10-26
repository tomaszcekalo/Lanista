using System.Globalization;
using System.Text.RegularExpressions;

namespace Lanista.Runner;

public sealed partial class MlnetOutputParser
{
    private bool _inSummary;

    [GeneratedRegex(@"^\|\s*(\d+)\s+(\S+)\s+(-?\d+(?:[.,]\d+)?)\s+(\d+(?:[.,]\d+)?)\s*\|$")]
    private static partial Regex TrialRow();

    [GeneratedRegex(@"^found best trial - trial id:\s*(\d+)")]
    private static partial Regex BestTrial();

    [GeneratedRegex(@"^Evaluate Metric:\s*(\S+)")]
    private static partial Regex Metric();

    [GeneratedRegex(@"^(?:[A-Za-z_]\w*\.)*\w*Exception:\s*(.+)$")]
    private static partial Regex ExceptionLine();

    public MlnetEvent? Parse(string line)
    {
        if (ExceptionLine().Match(line) is { Success: true })
            return new ErrorReported(line.Trim());

        if (line.StartsWith("Generating a console project", StringComparison.Ordinal))
            return new OutputGenerated();

        if (line.Contains("Experiment Results", StringComparison.Ordinal))
        {
            _inSummary = true;
            return null;
        }

        if (Metric().Match(line) is { Success: true } m)
            return new MetricDeclared(m.Groups[1].Value);

        if (BestTrial().Match(line) is { Success: true } b)
            return new BestTrialFound(int.Parse(b.Groups[1].Value, CultureInfo.InvariantCulture));

        // The final "Top 5 models" table repeats trial rows already reported live.
        if (!_inSummary && TrialRow().Match(line) is { Success: true } r)
        {
            return new TrialCompleted(
                int.Parse(r.Groups[1].Value, CultureInfo.InvariantCulture),
                r.Groups[2].Value,
                ParseDouble(r.Groups[3].Value),
                ParseDouble(r.Groups[4].Value));
        }

        return null;
    }

    private static double ParseDouble(string s) =>
        double.Parse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);
}
