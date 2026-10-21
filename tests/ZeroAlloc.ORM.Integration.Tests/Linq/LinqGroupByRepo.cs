using System.Data.Async;
using ZeroAlloc.ORM.Linq;

namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// [LinqQuery] GroupBy over the Executions log and the Sessions table: Daedalus' token reports.
[Dialect(SqlDialect.PostgreSql)]
internal sealed partial class LinqGroupByRepo(IAsyncDbConnection connection)
{
    [Command("""
        INSERT INTO "Sessions" ("Id", "OwnerId", "CreatedAt", "TotalInputTokens", "TotalOutputTokens")
        VALUES (@id, @ownerId, @createdAt, @input, @output)
        """)]
    public partial Task<int> InsertSessionAsync(
        Guid id,
        string ownerId,
        DateTimeOffset createdAt,
        long input,
        long output,
        CancellationToken ct);

    [LinqQuery(nameof(TokensPerModelAndDayQuery))]
    public partial Task<List<ModelDayTokens>> TokensPerModelAndDayAsync(DateTime since, CancellationToken ct);

    [LinqQuery(nameof(SessionTokensPerOwnerAndDayQuery))]
    public partial Task<List<OwnerDayTokens>> SessionTokensPerOwnerAndDayAsync(CancellationToken ct);

    [LinqQuery(nameof(CountPerModelQuery))]
    public partial Task<List<ModelCount>> CountPerModelAsync(CancellationToken ct);

    [LinqQuery(nameof(IntCountPerModelQuery))]
    public partial Task<List<ModelCount>> IntCountPerModelAsync(CancellationToken ct);

    [LinqQuery(nameof(AveragesPerModelQuery))]
    public partial Task<List<ModelAverages>> AveragesPerModelAsync(CancellationToken ct);

    // Daedalus Q1: tokens per model and day since a moment, the busiest model first per day.
    private static IQuery<ModelDayTokens> TokensPerModelAndDayQuery(DateTime since) =>
        Sql.Table<ExecutionEntity>()
            .Where(e => e.ExecutedAt >= since)
            .GroupBy(e => new { e.ModelId, Day = e.ExecutedAt.Date })
            .OrderBy(g => g.Key.Day)
            .ThenByDescending(g => g.Sum(e => (long)e.InputTokens))
            .Select(g => new ModelDayTokens(
                g.Key.ModelId,
                g.Key.Day,
                g.Sum(e => (long)e.InputTokens),
                g.Sum(e => (long)e.OutputTokens),
                g.Count()));

    // Daedalus Q2: session tokens per owner and day, over a DateTimeOffset column, whose .Date
    // is the UTC day whatever the session's TimeZone.
    private static IQuery<OwnerDayTokens> SessionTokensPerOwnerAndDayQuery() =>
        Sql.Table<SessionEntity>()
            .GroupBy(s => new { s.OwnerId, Day = s.CreatedAt.Date })
            .OrderBy(g => g.Key.OwnerId)
            .ThenBy(g => g.Key.Day)
            .Select(g => new OwnerDayTokens(
                g.Key.OwnerId,
                g.Key.Day,
                g.Sum(s => s.TotalInputTokens),
                g.Sum(s => s.TotalOutputTokens),
                g.Count()));

    private static IQuery<ModelCount> CountPerModelQuery() =>
        Sql.Table<ExecutionEntity>()
            .GroupBy(e => e.ModelId)
            .OrderBy(g => g.Key)
            .Select(g => new ModelCount(g.Key, g.LongCount(), g.Sum(e => e.Sequence)));

    // g.Count() is an int, count(*)::int, read into the record's long member.
    private static IQuery<ModelCount> IntCountPerModelQuery() =>
        Sql.Table<ExecutionEntity>()
            .GroupBy(e => e.ModelId)
            .OrderBy(g => g.Key)
            .Select(g => new ModelCount(g.Key, g.Count(), g.Sum(e => e.Sequence)));

    private static IQuery<ModelAverages> AveragesPerModelQuery() =>
        Sql.Table<ExecutionEntity>()
            .GroupBy(e => e.ModelId)
            .OrderBy(g => g.Key)
            .Select(g => new ModelAverages(
                g.Key,
                g.Average(e => e.InputTokens),
                g.Average(e => (decimal)e.InputTokens),
                g.Average(e => e.Sequence),
                g.Min(e => e.InputTokens),
                g.Max(e => (long)e.InputTokens),
                g.Max(e => e.ExecutedAt)));
}
