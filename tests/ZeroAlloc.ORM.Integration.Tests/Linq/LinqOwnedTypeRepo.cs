using System.Data.Async;
using ZeroAlloc.ORM.Linq;

namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// [LinqQuery] over AnalysisRequests' owned columns, plus a [Command] insert to seed it.
[Dialect(SqlDialect.PostgreSql)]
internal sealed partial class LinqOwnedTypeRepo(IAsyncDbConnection connection)
{
    [Command("""
        INSERT INTO "AnalysisRequests" (
            "Id", "Title", "RepositoryUrl", "RepositoryBranch", "Repository_CommitSha", "Repository_Platform",
            "FilePath", "Location_StartLine", "Location_EndLine",
            "Outcome_PullRequestUrl", "Outcome_ValidationResult", "Outcome_HasFailedValidation")
        VALUES (
            @id, @title, @url, @branch, @commitSha, @platform,
            @filePath, @startLine, @endLine,
            @pullRequestUrl, CAST(@validationResult AS jsonb), @hasFailedValidation)
        """)]
    public partial Task<int> InsertAsync(
        Guid id,
        string title,
        string url,
        string? branch,
        string? commitSha,
        RepositoryPlatform platform,
        string filePath,
        int? startLine,
        int? endLine,
        string? pullRequestUrl,
        string? validationResult,
        bool? hasFailedValidation,
        CancellationToken ct);

    [LinqQuery(nameof(AllQuery))]
    public partial Task<List<AnalysisRequestLine>> AllAsync(CancellationToken ct);

    [LinqQuery(nameof(FailedQuery))]
    public partial Task<List<AnalysisRequestLine>> FailedAsync(RepositoryPlatform platform, CancellationToken ct);

    [LinqQuery(nameof(WithoutPullRequestQuery))]
    public partial Task<List<AnalysisRequestLine>> WithoutPullRequestAsync(CancellationToken ct);

    private static IQuery<AnalysisRequestLine> AllQuery() =>
        Sql.Table<AnalysisRequestEntity>()
            .OrderBy(r => r.Title)
            .Select(r => new AnalysisRequestLine(
                r.Title,
                r.Repository.Url,
                r.Repository.Branch,
                r.Repository.CommitSha,
                r.Repository.Platform,
                r.Location.FilePath,
                r.Location.StartLine,
                r.Location.EndLine,
                r.Outcome!.PullRequestUrl,
                r.Outcome!.ValidationResult));

    // A request without an outcome has HasFailedValidation NULL, which is not TRUE: it is left out,
    // where C# would throw on the null Outcome.
    private static IQuery<AnalysisRequestLine> FailedQuery(RepositoryPlatform platform) =>
        Sql.Table<AnalysisRequestEntity>()
            .Where(r => r.Repository.Platform == platform && r.Outcome!.HasFailedValidation == true)
            .OrderBy(r => r.Location.StartLine)
            .Select(r => new AnalysisRequestLine(
                r.Title,
                r.Repository.Url,
                r.Repository.Branch,
                r.Repository.CommitSha,
                r.Repository.Platform,
                r.Location.FilePath,
                r.Location.StartLine,
                r.Location.EndLine,
                r.Outcome!.PullRequestUrl,
                r.Outcome!.ValidationResult));

    // A request without an outcome reads its PullRequestUrl as NULL, so it matches.
    private static IQuery<AnalysisRequestLine> WithoutPullRequestQuery() =>
        Sql.Table<AnalysisRequestEntity>()
            .Where(r => r.Outcome!.PullRequestUrl == null)
            .OrderBy(r => r.Title)
            .Select(r => new AnalysisRequestLine(
                r.Title,
                r.Repository.Url,
                r.Repository.Branch,
                r.Repository.CommitSha,
                r.Repository.Platform,
                r.Location.FilePath,
                r.Location.StartLine,
                r.Location.EndLine,
                r.Outcome!.PullRequestUrl,
                r.Outcome!.ValidationResult));
}
