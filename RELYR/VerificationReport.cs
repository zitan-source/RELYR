using System.IO;

namespace RELYR;

/// <summary>
/// Writes consistently formatted verification results and retains the names of
/// failed checks for the test runner's final summary.
/// </summary>
internal sealed class VerificationReport
{
    readonly TextWriter output;
    readonly List<string> failures = [];

    internal VerificationReport(TextWriter output)
    {
        this.output = output;
    }

    internal bool HasNoFailures => failures.Count == 0;

    internal void Check(bool passed, string name)
    {
        output.WriteLine((passed ? "PASS " : "FAIL ") + name);
        if (!passed)
            failures.Add(name);
    }

    internal void RecordException(string failureName, string outputPrefix, Exception exception)
    {
        output.WriteLine(outputPrefix + exception);
        failures.Add(failureName);
    }

    internal int Complete(string passedMessage, string failedMessage, bool includeFailureNames = true)
    {
        output.WriteLine(failures.Count == 0
            ? passedMessage
            : includeFailureNames
                ? failedMessage + string.Join(", ", failures)
                : failedMessage);
        return failures.Count == 0 ? 0 : 1;
    }
}
