namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// Owned by AnalysisRequestEntity and optional: a request without an outcome has every column NULL.
// ValidationResult is a jsonb document, so a setter binds its value as jsonb.
public sealed class AnalysisOutcome
{
    public string? PullRequestUrl { get; private set; }

    [Json]
    public string? ValidationResult { get; private set; }

    public bool HasFailedValidation { get; private set; }
}
