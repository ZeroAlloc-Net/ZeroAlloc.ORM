using System;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Xunit;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Generator.Tests.Linq;

// Row locks in a [LinqQuery]: ForUpdate, ForUpdateSkipLocked and ForUpdateNoWait render after
// LIMIT and OFFSET as FOR UPDATE, FOR UPDATE SKIP LOCKED and FOR UPDATE NOWAIT. A lock holds
// until its transaction ends, so a method that locks without an IAsyncDbTransaction parameter
// warns ZAO113: its lock is released as soon as its own statement ends. Every snapshot is also
// compiled: the whole compilation must have no error and the emitted code no warning.
public class LinqLockTests
{
    private const string Prelude = """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using System.Data.Async;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.ORM;
        using ZeroAlloc.ORM.Linq;

        namespace TestApp;

        public sealed class TaskEntity
        {
            public Guid Id { get; private set; }
            public string Title { get; private set; } = "";
            public int Status { get; private set; }
            public int Order { get; private set; }
        }

        public sealed class ExecutionEntity
        {
            public Guid Id { get; private set; }
            public Guid TaskId { get; private set; }
            public long InputTokens { get; private set; }
        }

        public sealed record TaskLine(Guid Id, string Title);

        public sealed record ExecutionLine(Guid Id, long InputTokens);

        public sealed record StatusCount(int Status, int Count);

        [TableMap(typeof(TaskEntity), "Tasks")]
        internal static class TaskMap;

        [TableMap(typeof(ExecutionEntity), "Executions")]
        internal static class ExecutionMap;

        """;

    private const string Repo = """
        [Dialect(SqlDialect.PostgreSql)]
        public sealed partial class TaskStore(IAsyncDbConnection connection)
        {

        """;

    // Daedalus locks a task before it changes it: the single-row shape's LIMIT 1 comes first.
    [Fact]
    public void For_update_by_id()
    {
        VerifyCompiled(
            Repo + """
                [LinqQuery(nameof(LockQuery))]
                public partial Task<TaskLine?> Lock(Guid id, IAsyncDbTransaction tx, CancellationToken ct);

                private static IQuery<TaskLine> LockQuery(Guid id) =>
                    Sql.Table<TaskEntity>().Where(t => t.Id == id).ForUpdate().Select(t => new TaskLine(t.Id, t.Title));
            }
            """,
            "SELECT \"Id\", \"Title\" FROM \"Tasks\" WHERE \"Id\" = @id LIMIT 1 FOR UPDATE\"",
            "__cmd.Transaction = @tx;");
    }

    // A work queue: each worker claims the first rows no other transaction holds.
    [Fact]
    public void Skip_locked_queue_claim()
    {
        VerifyCompiled(
            Repo + """
                [LinqQuery(nameof(ClaimQuery))]
                public partial Task<List<TaskLine>> Claim(int status, int count, IAsyncDbTransaction tx, CancellationToken ct);

                private static IQuery<TaskLine> ClaimQuery(int status, int count) =>
                    Sql.Table<TaskEntity>()
                        .Where(t => t.Status == status)
                        .OrderBy(t => t.Order)
                        .Take(count)
                        .ForUpdateSkipLocked()
                        .Select(t => new TaskLine(t.Id, t.Title));
            }
            """,
            "SELECT \"Id\", \"Title\" FROM \"Tasks\" WHERE \"Status\" = @status ORDER BY \"Order\" LIMIT GREATEST(@count, 0) FOR UPDATE SKIP LOCKED\"",
            "__cmd.Transaction = @tx;");
    }

    // The ordered overload keeps the chain ordered, so ThenBy may follow the lock; a join locks
    // the rows of every joined table, PostgreSQL's default FOR UPDATE.
    [Fact]
    public void No_wait_lock()
    {
        VerifyCompiled(
            Repo + """
                [LinqQuery(nameof(NoWaitQuery))]
                public partial Task<List<TaskLine>> NoWait(int status, IAsyncDbTransaction tx, CancellationToken ct);

                [LinqQuery(nameof(JoinedQuery))]
                public partial Task<List<ExecutionLine>> Joined(Guid id, IAsyncDbTransaction tx, CancellationToken ct);

                private static IQuery<TaskLine> NoWaitQuery(int status) =>
                    Sql.Table<TaskEntity>()
                        .Where(t => t.Status == status)
                        .OrderBy(t => t.Order)
                        .ForUpdateNoWait()
                        .ThenBy(t => t.Id)
                        .Select(t => new TaskLine(t.Id, t.Title));

                private static IQuery<ExecutionLine> JoinedQuery(Guid id) =>
                    Sql.Table<ExecutionEntity>()
                        .Join(Sql.Table<TaskEntity>(), e => e.TaskId, t => t.Id, (e, t) => new { Execution = e, Task = t })
                        .Where(x => x.Task.Id == id)
                        .ForUpdate()
                        .Select(x => new ExecutionLine(x.Execution.Id, x.Execution.InputTokens));
            }
            """,
            "SELECT \"Id\", \"Title\" FROM \"Tasks\" WHERE \"Status\" = @status ORDER BY \"Order\", \"Id\" FOR UPDATE NOWAIT\"",
            "SELECT t0.\"Id\", t0.\"InputTokens\" FROM \"Executions\" AS t0 INNER JOIN \"Tasks\" AS t1 ON t0.\"TaskId\" = t1.\"Id\" WHERE t1.\"Id\" = @id FOR UPDATE\"");
    }

    // A lock applies to its own tuple element: the task is locked, its executions are not.
    [Fact]
    public void Lock_in_tuple_element()
    {
        VerifyCompiled(
            Repo + """
                [LinqQuery(nameof(WithExecutionsQuery))]
                public partial Task<(TaskLine? Task, List<ExecutionLine> Executions)> WithExecutions(Guid id, IAsyncDbTransaction tx, CancellationToken ct);

                private static (IQuery<TaskLine>, IQuery<ExecutionLine>) WithExecutionsQuery(Guid id) =>
                    (Sql.Table<TaskEntity>().Where(t => t.Id == id).ForUpdate().Select(t => new TaskLine(t.Id, t.Title)),
                     Sql.Table<ExecutionEntity>().Where(e => e.TaskId == id).Select(e => new ExecutionLine(e.Id, e.InputTokens)));
            }
            """,
            "SELECT \"Id\", \"Title\" FROM \"Tasks\" WHERE \"Id\" = @id LIMIT 1 FOR UPDATE\"",
            "SELECT \"Id\", \"InputTokens\" FROM \"Executions\" WHERE \"TaskId\" = @id\"");
    }

    // PostgreSQL rejects FOR UPDATE with GROUP BY and with aggregates, and a lock means nothing
    // in an IN subquery or a write; a chain holds one lock. Each is reported at the operator.
    [Theory]
    [InlineData(
        """
        [LinqQuery(nameof(Q))]
        public partial Task<List<StatusCount>> M(CancellationToken ct);
        private static IQuery<StatusCount> Q() =>
            Sql.Table<TaskEntity>().ForUpdate().GroupBy(t => t.Status).Select(g => new StatusCount(g.Key, g.Count()));
        """,
        "GroupBy",
        "'GroupBy' after 'ForUpdate' is not supported in a [LinqQuery] chain")]
    [InlineData(
        """
        [LinqQuery(nameof(Q))]
        public partial Task<List<StatusCount>> M(CancellationToken ct);
        private static IQuery<StatusCount> Q() =>
            Sql.Table<TaskEntity>().GroupBy(t => t.Status).ForUpdate().Select(g => new StatusCount(g.Key, g.Count()));
        """,
        "ForUpdate",
        "'ForUpdate' after 'GroupBy' is not supported in a [LinqQuery] chain")]
    [InlineData(
        """
        [LinqQuery(nameof(Q))]
        public partial Task<int> M(CancellationToken ct);
        private static IQuery<int> Q() => Sql.Table<TaskEntity>().ForUpdate().Count();
        """,
        "Count",
        "'Count' after 'ForUpdate' is not supported in a [LinqQuery] chain")]
    [InlineData(
        """
        [LinqQuery(nameof(Q))]
        public partial Task<long> M(CancellationToken ct);
        private static IQuery<long> Q() => Sql.Table<ExecutionEntity>().ForUpdateSkipLocked().Sum(e => e.InputTokens);
        """,
        "Sum",
        "'Sum' after 'ForUpdateSkipLocked' is not supported in a [LinqQuery] chain")]
    [InlineData(
        """
        [LinqQuery(nameof(Q))]
        public partial Task<List<TaskLine>> M(CancellationToken ct);
        private static IQuery<TaskLine> Q() =>
            Sql.Table<TaskEntity>()
                .Where(t => Sql.Table<ExecutionEntity>().ForUpdate().Select(e => e.TaskId).Contains(t.Id))
                .Select(t => new TaskLine(t.Id, t.Title));
        """,
        "ForUpdate",
        "'ForUpdate' in the source of an IQuery Contains is not supported in a [LinqQuery] chain")]
    [InlineData(
        """
        [LinqQuery(nameof(Q))]
        public partial Task<List<TaskLine>> M(CancellationToken ct);
        private static IQuery<TaskLine> Q() =>
            Sql.Table<TaskEntity>().ForUpdate().ForUpdateNoWait().Select(t => new TaskLine(t.Id, t.Title));
        """,
        "ForUpdateNoWait",
        "'ForUpdateNoWait' after 'ForUpdate', a second lock, is not supported in a [LinqQuery] chain")]
    [InlineData(
        """
        [LinqCommand(nameof(Q))]
        public partial Task<int> M(Guid id, CancellationToken ct);
        private static int Q(Guid id) => Sql.Table<TaskEntity>().Where(t => t.Id == id).ForUpdate().ExecuteDelete();
        """,
        "ForUpdate",
        "'ForUpdate' before 'ExecuteDelete' is not supported in a [LinqCommand] chain")]
    public void Lock_reports_ZAO102(string members, string located, string message)
    {
        var (result, tree) = GeneratorHarness.RunGeneratorOnFile(Prelude + Repo + members + "\n}\n");

        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Equal("ZAO102", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Same(tree, diagnostic.Location.SourceTree);
        var span = diagnostic.Location.SourceSpan;
        Assert.Equal(located, tree.ToString().Substring(span.Start, span.Length));
        Assert.Equal(message, diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    // A lock is released when its transaction ends; without one, when its own statement ends. The
    // method is still generated. A lock in any tuple element counts.
    [Theory]
    [InlineData(
        """
        [LinqQuery(nameof(Q))]
        public partial Task<TaskLine?> Lock(Guid id, CancellationToken ct);
        private static IQuery<TaskLine> Q(Guid id) =>
            Sql.Table<TaskEntity>().Where(t => t.Id == id).ForUpdate().Select(t => new TaskLine(t.Id, t.Title));
        """)]
    [InlineData(
        """
        [LinqQuery(nameof(Q))]
        public partial Task<(List<ExecutionLine> Executions, TaskLine? Task)> Lock(Guid id, CancellationToken ct);
        private static (IQuery<ExecutionLine>, IQuery<TaskLine>) Q(Guid id) =>
            (Sql.Table<ExecutionEntity>().Where(e => e.TaskId == id).Select(e => new ExecutionLine(e.Id, e.InputTokens)),
             Sql.Table<TaskEntity>().Where(t => t.Id == id).ForUpdateNoWait().Select(t => new TaskLine(t.Id, t.Title)));
        """)]
    public void Lock_without_transaction_warns_ZAO113(string members)
    {
        var (result, tree) = GeneratorHarness.RunGeneratorOnFile(Prelude + Repo + members + "\n}\n");

        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Equal("ZAO113", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Same(tree, diagnostic.Location.SourceTree);
        var span = diagnostic.Location.SourceSpan;
        Assert.Equal("Lock", tree.ToString().Substring(span.Start, span.Length));
        Assert.Equal(
            "'Lock' locks rows but takes no IAsyncDbTransaction; the lock is released when its statement ends",
            diagnostic.GetMessage(CultureInfo.InvariantCulture));
        Assert.NotEmpty(result.Results[0].GeneratedSources);
    }

    [Fact]
    public void Lock_with_transaction_has_no_ZAO113()
    {
        var (result, _) = GeneratorHarness.RunGeneratorOnFile(Prelude + Repo + """
                [LinqQuery(nameof(Q))]
                public partial Task<TaskLine?> Lock(Guid id, IAsyncDbTransaction tx, CancellationToken ct);
                private static IQuery<TaskLine> Q(Guid id) =>
                    Sql.Table<TaskEntity>().Where(t => t.Id == id).ForUpdate().Select(t => new TaskLine(t.Id, t.Title));

                [LinqQuery(nameof(Unlocked))]
                public partial Task<TaskLine?> Read(Guid id, CancellationToken ct);
                private static IQuery<TaskLine> Unlocked(Guid id) =>
                    Sql.Table<TaskEntity>().Where(t => t.Id == id).Select(t => new TaskLine(t.Id, t.Title));
            }
            """);

        Assert.Empty(result.Results[0].Diagnostics);
        Assert.NotEmpty(result.Results[0].GeneratedSources);
    }

    // Each of `expected` is text the generated files must contain, checked before the snapshot.
    // A SQL constant ends in a quote, so an expected statement ending in one is the whole statement.
    private static void VerifyCompiled(
        string body,
        string expected1,
        string expected2,
        [CallerFilePath] string testFilePath = "",
        [CallerMemberName] string testMethod = "")
    {
        var (result, compilation) = GeneratorHarness.RunGeneratorAndGetCompilation(Prelude + body);

        Assert.Empty(result.Results[0].Diagnostics);
        var generatedPaths = result.GeneratedTrees.Select(t => t.FilePath).ToHashSet(StringComparer.Ordinal);
        var problems = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error
                || (d.Severity == DiagnosticSeverity.Warning
                    && d.Location.SourceTree is { } tree
                    && generatedPaths.Contains(tree.FilePath)))
            .Select(d => d.Id + ": " + d.GetMessage(CultureInfo.InvariantCulture))
            .ToList();
        Assert.Empty(problems);

        // The SQL sits in C# string literals, which escape its quoted identifiers.
        var generated = string.Concat(result.GeneratedTrees.Select(t => t.ToString()))
            .Replace("\\\"", "\"", StringComparison.Ordinal);
        Assert.Contains(expected1, generated, StringComparison.Ordinal);
        Assert.Contains(expected2, generated, StringComparison.Ordinal);

        GeneratorSnapshot.Verify(result, testFilePath, testMethod);
    }
}
