namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// Owned by AnalysisRequestEntity and optional: a request without an outcome has every column NULL.
// ValidationResult is a jsonb document, which the map declares, so the class needs no attribute.
public sealed class AnalysisOutcome
{
    public string? PullRequestUrl { get; private set; }
    public string? ValidationResult { get; private set; }

    public bool HasFailedValidation { get; private set; }
}
