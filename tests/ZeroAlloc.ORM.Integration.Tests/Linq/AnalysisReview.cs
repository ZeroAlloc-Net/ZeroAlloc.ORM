namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// Owned by AnalysisRequestEntity as a positional record, optional: a request nobody reviewed
// has both columns NULL.
public sealed record AnalysisReview(string? Reviewer, bool Approved);
