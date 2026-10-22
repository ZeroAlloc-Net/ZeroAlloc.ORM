namespace ZeroAlloc.ORM.Integration.Tests.Linq;

internal sealed record AnalysisRequestLine(
    string Title,
    string Url,
    string? Branch,
    string? CommitSha,
    RepositoryPlatform Platform,
    string FilePath,
    int? StartLine,
    int? EndLine,
    string? PullRequestUrl,
    string? ValidationResult);
