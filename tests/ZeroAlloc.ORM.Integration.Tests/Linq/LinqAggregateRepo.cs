using System.Data.Async;
using ZeroAlloc.ORM.Linq;

namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// [LinqQuery] scalar aggregates over the Executions log. Each nullable result is read with the
// strict reader of its CLR type, so these also pin the PostgreSQL type each aggregate renders.
[Dialect(SqlDialect.PostgreSql)]
internal sealed partial class LinqAggregateRepo(IAsyncDbConnection connection)
{
    [Command("""
        INSERT INTO "Executions" ("Id", "TaskId", "ModelId", "InputTokens", "OutputTokens", "ExecutedAt", "Sequence", "TotalTokens", "Cost", "Weight")
        VALUES (@id, @taskId, @modelId, @inputTokens, @outputTokens, @executedAt, @sequence, @totalTokens, @cost, @weight)
        """)]
    public partial Task<int> InsertAsync(
        Guid id,
        Guid taskId,
        string? modelId,
        int inputTokens,
        int outputTokens,
        DateTime executedAt,
        int? sequence,
        long? totalTokens,
        decimal? cost,
        float? weight,
        CancellationToken ct);

    [Command("""
        INSERT INTO "Tasks" ("Id", "ProjectId", "Title", "Status", "Kind", "CreatedAt", "DueAt", "Tags", "Order")
        VALUES (@id, @projectId, 't', 0, 'Bug', @createdAt, @dueAt, '{}', 0)
        """)]
    public partial Task<int> InsertTaskAsync(Guid id, Guid projectId, DateTimeOffset createdAt, DateTimeOffset? dueAt, CancellationToken ct);

    [LinqQuery(nameof(SumInputQuery))]
    public partial Task<int> SumInputAsync(Guid taskId, CancellationToken ct);

    [LinqQuery(nameof(SumInputWideQuery))]
    public partial Task<long> SumInputWideAsync(Guid taskId, CancellationToken ct);

    [LinqQuery(nameof(SumOutputDoubleQuery))]
    public partial Task<double> SumOutputDoubleAsync(Guid taskId, CancellationToken ct);

    [LinqQuery(nameof(SumSequenceQuery))]
    public partial Task<int?> SumSequenceAsync(Guid taskId, CancellationToken ct);

    [LinqQuery(nameof(SumSequenceWideQuery))]
    public partial Task<long?> SumSequenceWideAsync(Guid taskId, CancellationToken ct);

    [LinqQuery(nameof(SumSequenceDecimalQuery))]
    public partial Task<decimal?> SumSequenceDecimalAsync(Guid taskId, CancellationToken ct);

    [LinqQuery(nameof(SumSmallestInputQuery))]
    public partial Task<int> SumSmallestInputAsync(int take, CancellationToken ct);

    [LinqQuery(nameof(AverageInputQuery))]
    public partial Task<double> AverageInputAsync(Guid taskId, CancellationToken ct);

    [LinqQuery(nameof(AverageInputDecimalQuery))]
    public partial Task<decimal> AverageInputDecimalAsync(Guid taskId, CancellationToken ct);

    [LinqQuery(nameof(AverageSequenceDecimalQuery))]
    public partial Task<decimal?> AverageSequenceDecimalAsync(Guid taskId, CancellationToken ct);

    [LinqQuery(nameof(AverageOutputQuery))]
    public partial Task<double?> AverageOutputAsync(Guid taskId, CancellationToken ct);

    [LinqQuery(nameof(MaxSequenceQuery))]
    public partial Task<int?> MaxSequenceAsync(Guid taskId, CancellationToken ct);

    [LinqQuery(nameof(MaxInputQuery))]
    public partial Task<int> MaxInputAsync(Guid taskId, CancellationToken ct);

    [LinqQuery(nameof(MaxInputWideQuery))]
    public partial Task<long?> MaxInputWideAsync(Guid taskId, CancellationToken ct);

    [LinqQuery(nameof(MinModelQuery))]
    public partial Task<string?> MinModelAsync(Guid taskId, CancellationToken ct);

    [LinqQuery(nameof(LastExecutedQuery))]
    public partial Task<DateTime?> LastExecutedAsync(Guid taskId, CancellationToken ct);

    [LinqQuery(nameof(SumWeightQuery))]
    public partial Task<double?> SumWeightAsync(Guid taskId, CancellationToken ct);

    [LinqQuery(nameof(SumTotalQuery))]
    public partial Task<long?> SumTotalAsync(Guid taskId, CancellationToken ct);

    [LinqQuery(nameof(MaxInputDoubleQuery))]
    public partial Task<double?> MaxInputDoubleAsync(Guid taskId, CancellationToken ct);

    [LinqQuery(nameof(MinInputDecimalQuery))]
    public partial Task<decimal?> MinInputDecimalAsync(Guid taskId, CancellationToken ct);

    [LinqQuery(nameof(MinWeightDoubleQuery))]
    public partial Task<double?> MinWeightDoubleAsync(Guid taskId, CancellationToken ct);

    [LinqQuery(nameof(MaxCostQuery))]
    public partial Task<decimal?> MaxCostAsync(Guid taskId, CancellationToken ct);

    [LinqQuery(nameof(LatestDueQuery))]
    public partial Task<DateTimeOffset?> LatestDueAsync(CancellationToken ct);

    [LinqQuery(nameof(EarliestCreatedQuery))]
    public partial Task<DateTimeOffset?> EarliestCreatedAsync(CancellationToken ct);

    // sum(real) adds in float4; the column is cast so the doubles add as LINQ adds them.
    private static IQuery<double?> SumWeightQuery(Guid taskId) =>
        Sql.Table<ExecutionEntity>().Where(e => e.TaskId == taskId).Sum(e => (double?)e.Weight);

    // sum(bigint) is numeric, cast back to bigint.
    private static IQuery<long?> SumTotalQuery(Guid taskId) =>
        Sql.Table<ExecutionEntity>().Where(e => e.TaskId == taskId).Sum(e => e.TotalTokens);

    private static IQuery<double?> MaxInputDoubleQuery(Guid taskId) =>
        Sql.Table<ExecutionEntity>().Where(e => e.TaskId == taskId).Max(e => (double?)e.InputTokens);

    private static IQuery<decimal?> MinInputDecimalQuery(Guid taskId) =>
        Sql.Table<ExecutionEntity>().Where(e => e.TaskId == taskId).Min(e => (decimal?)e.InputTokens);

    private static IQuery<double?> MinWeightDoubleQuery(Guid taskId) =>
        Sql.Table<ExecutionEntity>().Where(e => e.TaskId == taskId).Min(e => (double?)e.Weight);

    private static IQuery<decimal?> MaxCostQuery(Guid taskId) =>
        Sql.Table<ExecutionEntity>().Where(e => e.TaskId == taskId).Max(e => e.Cost);

    private static IQuery<DateTimeOffset?> LatestDueQuery() =>
        Sql.Table<TaskEntity>().Max(t => t.DueAt);

    private static IQuery<DateTimeOffset?> EarliestCreatedQuery() =>
        Sql.Table<TaskEntity>().Min(t => (DateTimeOffset?)t.CreatedAt);

    private static IQuery<int> SumInputQuery(Guid taskId) =>
        Sql.Table<ExecutionEntity>().Where(e => e.TaskId == taskId).Sum(e => e.InputTokens);

    private static IQuery<long> SumInputWideQuery(Guid taskId) =>
        Sql.Table<ExecutionEntity>().Where(e => e.TaskId == taskId).Sum(e => (long)e.InputTokens);

    private static IQuery<double> SumOutputDoubleQuery(Guid taskId) =>
        Sql.Table<ExecutionEntity>().Where(e => e.TaskId == taskId).Sum(e => (double)e.OutputTokens);

    private static IQuery<int?> SumSequenceQuery(Guid taskId) =>
        Sql.Table<ExecutionEntity>().Where(e => e.TaskId == taskId).Sum(e => e.Sequence);

    private static IQuery<long?> SumSequenceWideQuery(Guid taskId) =>
        Sql.Table<ExecutionEntity>().Where(e => e.TaskId == taskId).Sum(e => (long?)e.Sequence);

    private static IQuery<decimal?> SumSequenceDecimalQuery(Guid taskId) =>
        Sql.Table<ExecutionEntity>().Where(e => e.TaskId == taskId).Sum(e => (decimal?)e.Sequence);

    private static IQuery<int> SumSmallestInputQuery(int take) =>
        Sql.Table<ExecutionEntity>().OrderBy(e => e.InputTokens).ThenBy(e => e.Id).Take(take).Sum(e => e.InputTokens);

    private static IQuery<double> AverageInputQuery(Guid taskId) =>
        Sql.Table<ExecutionEntity>().Where(e => e.TaskId == taskId).Average(e => e.InputTokens);

    private static IQuery<decimal> AverageInputDecimalQuery(Guid taskId) =>
        Sql.Table<ExecutionEntity>().Where(e => e.TaskId == taskId).Average(e => (decimal)e.InputTokens);

    private static IQuery<decimal?> AverageSequenceDecimalQuery(Guid taskId) =>
        Sql.Table<ExecutionEntity>().Where(e => e.TaskId == taskId).Average(e => (decimal?)e.Sequence);

    // Daedalus q5: the mean output of the executions that produced any.
    private static IQuery<double?> AverageOutputQuery(Guid taskId) =>
        Sql.Table<ExecutionEntity>()
            .Where(e => e.TaskId == taskId && e.OutputTokens > 0)
            .Average(e => (double?)e.OutputTokens);

    // Daedalus q6: the task's highest execution sequence, null before its first.
    private static IQuery<int?> MaxSequenceQuery(Guid taskId) =>
        Sql.Table<ExecutionEntity>().Where(e => e.TaskId == taskId).Max(e => (int?)e.Sequence);

    private static IQuery<int> MaxInputQuery(Guid taskId) =>
        Sql.Table<ExecutionEntity>().Where(e => e.TaskId == taskId).Max(e => e.InputTokens);

    private static IQuery<long?> MaxInputWideQuery(Guid taskId) =>
        Sql.Table<ExecutionEntity>().Where(e => e.TaskId == taskId).Max(e => (long?)e.InputTokens);

    private static IQuery<string?> MinModelQuery(Guid taskId) =>
        Sql.Table<ExecutionEntity>().Where(e => e.TaskId == taskId).Min(e => e.ModelId);

    private static IQuery<DateTime?> LastExecutedQuery(Guid taskId) =>
        Sql.Table<ExecutionEntity>().Where(e => e.TaskId == taskId).Max(e => (DateTime?)e.ExecutedAt);
}
