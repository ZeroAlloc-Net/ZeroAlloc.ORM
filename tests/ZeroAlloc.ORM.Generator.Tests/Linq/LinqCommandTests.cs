using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;
using ZeroAlloc.ORM.Generator.Linq;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Generator.Tests.Linq;

// [LinqCommand] with ExecuteDelete: the companion's chain is Sql.Table<T>(), Where and WhereIf
// calls, then ExecuteDelete(), and the method takes the [Command] non-query path. Every snapshot
// is also compiled: the whole compilation must have no error and the emitted code no warning.
public class LinqCommandTests
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

        public enum Stage { Todo, Done }

        public sealed class TaskEntity
        {
            public Guid Id { get; private set; }
            public string Title { get; private set; } = "";
            public Stage Stage { get; private set; }
            public int Order { get; private set; }
        }

        public sealed class ExecutionEntity
        {
            public Guid Id { get; private set; }
            public Guid TaskId { get; private set; }
            public DateTime ExecutedAt { get; private set; }
        }

        public sealed class RoleCharterEntity
        {
            public Guid Id { get; private set; }
            public string Role { get; private set; } = "";
            public bool IsActive { get; private set; }
        }

        [TableMap(typeof(TaskEntity), "Tasks")]
        [ColumnMap(nameof(TaskEntity.Stage), EnumAsString = true)]
        internal static class TaskMap;

        [TableMap(typeof(ExecutionEntity), "Executions", Schema = "app")]
        internal static class ExecutionMap;

        [TableMap(typeof(RoleCharterEntity), "RoleCharters")]
        internal static class RoleCharterMap;

        """;

    private const string Repo = """
        [Dialect(SqlDialect.PostgreSql)]
        public sealed partial class TaskStore(IAsyncDbConnection connection)
        {

        """;

    [Fact]
    public void Delete_by_id()
    {
        VerifyCompiled(
            Repo + """
                [LinqCommand(nameof(DeleteQuery))]
                public partial Task<int> Delete(Guid id, CancellationToken ct);

                private static int DeleteQuery(Guid id) =>
                    Sql.Table<TaskEntity>().Where(t => t.Id == id).ExecuteDelete();
            }
            """,
            "DELETE FROM \"Tasks\" WHERE \"Id\" = @id",
            "// LINQ: TaskStore.DeleteQuery");
    }

    // PostgresRoleCharterStore: the charters of roles that are no longer configured. The array
    // parameter renders as = ANY, and the method runs inside the caller's transaction.
    [Fact]
    public void Delete_with_array_contains()
    {
        VerifyCompiled(
            Repo + """
                [LinqCommand(nameof(DeleteStaleQuery))]
                public partial Task DeleteStale(string[] roles, IAsyncDbTransaction tx, CancellationToken ct);

                private static int DeleteStaleQuery(string[] roles) =>
                    Sql.Table<RoleCharterEntity>().Where(r => r.IsActive && !roles.Contains(r.Role)).ExecuteDelete();
            }
            """,
            "DELETE FROM \"RoleCharters\" WHERE \"IsActive\" AND NOT (\"Role\" = ANY(@roles))",
            "__cmd.Transaction = @tx;",
            NpgsqlStub.Source);
    }

    // The subquery binds through its own scope and takes no alias, nor does the DELETE; WhereIf
    // and Where conditions join with AND, and the enum constant takes its column's storage.
    [Fact]
    public void Delete_with_subquery()
    {
        VerifyCompiled(
            Repo + """
                [LinqCommand(nameof(DeleteDoneQuery))]
                public partial ValueTask<int> DeleteDone(bool onlyOld, DateTime before, CancellationToken ct);

                private static int DeleteDoneQuery(bool onlyOld, DateTime before) =>
                    Sql.Table<ExecutionEntity>()
                        .Where(e => Sql.Table<TaskEntity>().Where(t => t.Stage == Stage.Done).Select(t => t.Id).Contains(e.TaskId))
                        .WhereIf(onlyOld, e => e.ExecutedAt < before)
                        .ExecuteDelete();
            }
            """,
            "DELETE FROM \"app\".\"Executions\" WHERE \"TaskId\" IN (SELECT \"Id\" FROM \"Tasks\" WHERE \"Stage\" = 'Done')"
                + " AND ((@onlyOld) IS NOT TRUE OR (\"ExecutedAt\" < @before))",
            "public partial async global::System.Threading.Tasks.ValueTask<int> DeleteDone(");
    }

    // As EF's ExecuteDelete: no Where deletes every row. The generator adds nothing.
    [Fact]
    public void Delete_without_where()
    {
        VerifyCompiled(
            Repo + """
                [LinqCommand(nameof(DeleteAllQuery))]
                public partial ValueTask DeleteAll(CancellationToken ct);

                private static int DeleteAllQuery() => Sql.Table<TaskEntity>().ExecuteDelete();
            }
            """,
            "private const string __sql0_DeleteAll = \"DELETE FROM \"Tasks\"\";",
            "await __cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);");
    }

    // Zero rows affected throws, whether the method returns the count or not, and inside a
    // caller's transaction as well: Task<int>, ValueTask, Task and ValueTask<int>.
    [Fact]
    public void Throw_on_no_rows_emits_the_check()
    {
        VerifyCompiled(
            Repo + """
                [LinqCommand(nameof(DeleteQuery), ThrowOnNoRows = true)]
                public partial Task<int> Delete(Guid id, CancellationToken ct);

                [LinqCommand(nameof(DeleteQuery), ThrowOnNoRows = true)]
                public partial ValueTask Remove(Guid id, CancellationToken ct);

                [LinqCommand(nameof(DeleteQuery), ThrowOnNoRows = true)]
                public partial Task Drop(Guid id, CancellationToken ct);

                [LinqCommand(nameof(DeleteQuery), ThrowOnNoRows = true)]
                public partial ValueTask<int> Discard(Guid id, IAsyncDbTransaction tx, CancellationToken ct);

                private static int DeleteQuery(Guid id) =>
                    Sql.Table<TaskEntity>().Where(t => t.Id == id).ExecuteDelete();
            }
            """,
            "throw new global::ZeroAlloc.ORM.ZeroAllocOrmConcurrencyException(\"'TaskStore.Delete' affected no rows; the row was changed or removed since it was read\");",
            "throw new global::ZeroAlloc.ORM.ZeroAllocOrmConcurrencyException(\"'TaskStore.Remove' affected no rows; the row was changed or removed since it was read\");");
    }

    // A write reads its rows from the table and its conditions alone: anything else before the
    // terminal is reported at the operator.
    [Theory]
    [InlineData(".Join(Sql.Table<ExecutionEntity>(), t => t.Id, e => e.TaskId, (t, e) => t)", "Join")]
    [InlineData(".OrderBy(t => t.Order)", "OrderBy")]
    [InlineData(".Take(5)", "Take")]
    [InlineData(".Select(t => t)", "Select")]
    public void Command_chain_operators_report_ZAO102(string chainOperator, string name)
    {
        var (d, located) = Single(Repo + """
                [LinqCommand(nameof(DeleteQuery))]
                public partial Task<int> Delete(CancellationToken ct);

                private static int DeleteQuery() =>
                    Sql.Table<TaskEntity>().Where(t => t.Order > 0)
            """ + chainOperator + """
            .ExecuteDelete();
            }
            """);

        Assert.Equal("ZAO102", d.Id);
        Assert.Equal(name, located);
        Assert.Equal(
            "'" + name + "' before 'ExecuteDelete' is not supported in a [LinqCommand] chain",
            d.GetMessage(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Command_without_write_terminal_reports_ZAO100()
    {
        var (d, located) = Single(Repo + """
                [LinqCommand(nameof(CountQuery))]
                public partial Task<int> Count(CancellationToken ct);

                private static IQuery<int> CountQuery() => Sql.Table<TaskEntity>().Count();
            }
            """);

        Assert.Equal("ZAO100", d.Id);
        Assert.Equal("CountQuery", located);
        Assert.Equal(
            "Companion 'CountQuery' of [LinqCommand] method 'Count' is invalid: its chain must end in ExecuteDelete or ExecuteUpdate",
            d.GetMessage(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Command_tuple_companion_reports_ZAO100()
    {
        var (d, located) = Single(Repo + """
                [LinqCommand(nameof(DeleteQuery))]
                public partial Task<int> Delete(CancellationToken ct);

                private static (int, int) DeleteQuery() =>
                    (Sql.Table<TaskEntity>().ExecuteDelete(), Sql.Table<RoleCharterEntity>().ExecuteDelete());
            }
            """);

        Assert.Equal("ZAO100", d.Id);
        Assert.Equal("(int, int)", located);
        Assert.Equal(
            "Companion 'DeleteQuery' of [LinqCommand] method 'Delete' is invalid: it must be one chain ending in ExecuteDelete or ExecuteUpdate, not a tuple",
            d.GetMessage(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Query_with_write_terminal_reports_ZAO100()
    {
        var (d, located) = Single(Repo + """
                [LinqQuery(nameof(DeleteQuery))]
                public partial Task<int> Delete(CancellationToken ct);

                private static int DeleteQuery() => Sql.Table<TaskEntity>().ExecuteDelete();
            }
            """);

        Assert.Equal("ZAO100", d.Id);
        Assert.Equal("ExecuteDelete", located);
        Assert.Equal(
            "Companion 'DeleteQuery' of [LinqQuery] method 'Delete' is invalid: its chain is a write; use [LinqCommand]",
            d.GetMessage(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Command_return_shape_reports_ZAO112()
    {
        var (d, located) = Single(Repo + """
                [LinqCommand(nameof(DeleteQuery))]
                public partial Task<string> Delete(CancellationToken ct);

                private static int DeleteQuery() => Sql.Table<TaskEntity>().ExecuteDelete();
            }
            """);

        Assert.Equal("ZAO112", d.Id);
        Assert.Equal("Task<string>", located);
        Assert.StartsWith(
            "[LinqCommand] method 'Delete' returns 'System.Threading.Tasks.Task<string>', which does not fit 'int'; ",
            d.GetMessage(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Calling_a_command_companion_reports_ZAO109()
    {
        var (_, compilation) = GeneratorHarness.RunGeneratorAndGetCompilation(Prelude + Repo + """
                [LinqCommand(nameof(DeleteQuery))]
                public partial Task<int> Delete(CancellationToken ct);

                internal static int DeleteQuery() => Sql.Table<TaskEntity>().ExecuteDelete();
            }

            public static class Caller
            {
                public static int Outside() => TaskStore.DeleteQuery();
            }
            """);
        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new LinqCompanionCallAnalyzer()))
            .GetAnalyzerDiagnosticsAsync()
            .ConfigureAwait(false);

        var d = Assert.Single(diagnostics);
        Assert.Equal("ZAO109", d.Id);
        Assert.Equal(
            "'DeleteQuery' is a [LinqCommand] companion and is never run; call 'Delete' instead",
            d.GetMessage(CultureInfo.InvariantCulture));
    }

    // Runs the generator and returns its single diagnostic plus the source text it points at.
    private static (Diagnostic Diagnostic, string Located) Single(string body)
    {
        var (result, tree) = GeneratorHarness.RunGeneratorOnFile(Prelude + body);
        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Same(tree, diagnostic.Location.SourceTree);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Empty(result.Results[0].GeneratedSources);
        var span = diagnostic.Location.SourceSpan;
        return (diagnostic, tree.ToString().Substring(span.Start, span.Length));
    }

    // Each of `expected` is text the generated files must contain, checked before the snapshot.
    private static void VerifyCompiled(
        string body,
        string expected1,
        string expected2,
        string? additionalSource = null,
        [CallerFilePath] string testFilePath = "",
        [CallerMemberName] string testMethod = "")
    {
        var (result, compilation) = additionalSource is null
            ? GeneratorHarness.RunGeneratorAndGetCompilation(Prelude + body)
            : GeneratorHarness.RunGeneratorAndGetCompilation(Prelude + body, additionalSource);

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
