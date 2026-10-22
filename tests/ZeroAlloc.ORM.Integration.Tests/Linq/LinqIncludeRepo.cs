using System.Data.Async;
using ZeroAlloc.ORM.Linq;

namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// Daedalus' CodeAnalysisRepository Include: one code analysis request, with its owned
// Repository, Location and optional Outcome read as columns, and its iterations, as one tuple
// companion. Plus [Command] inserts to seed it.
[Dialect(SqlDialect.PostgreSql)]
internal sealed partial class LinqIncludeRepo(IAsyncDbConnection connection)
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
    public partial Task<int> InsertRequestAsync(
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

    [Command("""
        INSERT INTO "AnalysisIterations" ("Id", "RequestId", "Number", "Summary")
        VALUES (@id, @requestId, @number, @summary)
        """)]
    public partial Task<int> InsertIterationAsync(Guid id, Guid requestId, int number, string summary, CancellationToken ct);

    [LinqQuery(nameof(RequestWithIterationsQuery))]
    public partial Task<(AnalysisRequestLine? Request, OutcomeFlag? Outcome, List<IterationLine> Iterations)> RequestWithIterationsAsync(
        Guid id, CancellationToken ct);

    // The request's owned columns, its optional outcome's flag, and its iterations in order.
    private static (IQuery<AnalysisRequestLine>, IQuery<OutcomeFlag>, IQuery<IterationLine>) RequestWithIterationsQuery(Guid id) =>
        (Sql.Table<AnalysisRequestEntity>()
            .Where(r => r.Id == id)
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
                r.Outcome!.ValidationResult)),
         Sql.Table<AnalysisRequestEntity>()
            .Where(r => r.Id == id && r.Outcome != null)
            .Select(r => new OutcomeFlag(r.Title, r.Outcome!.HasFailedValidation)),
         Sql.Table<AnalysisIterationEntity>()
            .Where(i => i.RequestId == id)
            .OrderBy(i => i.Number)
            .Select(i => new IterationLine(i.Number, i.Summary)));
}
