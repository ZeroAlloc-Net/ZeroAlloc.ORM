using System;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Xunit;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Generator.Tests.Linq;

// A parent with its children: a companion returns a tuple of IQuery<T>, one complete chain per
// element, and the method returns the matching tuple through the [Query] multi-result path. Each
// element is its own statement of one `;`-joined command; a Row element is the first row or null, a List
// element every row, a Scalar element the value of a terminal.
public class LinqMultiResultTests
{
    private const string Prelude = """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using System.Data.Async;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.ORM;
        using ZeroAlloc.ORM.Linq;

        namespace TestApp;

        public sealed class TaskEntity
        {
            public Guid Id { get; private set; }
            public string Title { get; private set; } = "";
            public DateTimeOffset CreatedAt { get; private set; }
        }

        public sealed class ExecutionEntity
        {
            public Guid Id { get; private set; }
            public Guid TaskId { get; private set; }
            public int InputTokens { get; private set; }
            public DateTime ExecutedAt { get; private set; }
        }

        public sealed class BrainstormSession
        {
            public Guid Id { get; private set; }
            public string Topic { get; private set; } = "";
        }

        public sealed class BrainstormMessage
        {
            public Guid Id { get; private set; }
            public Guid SessionId { get; private set; }
            public string Role { get; private set; } = "";
            public string Content { get; private set; } = "";
            public DateTimeOffset CreatedAt { get; private set; }
        }

        [TableMap(typeof(TaskEntity), "Tasks")]
        internal static class TaskMap;

        [TableMap(typeof(ExecutionEntity), "Executions")]
        internal static class ExecutionMap;

        [TableMap(typeof(BrainstormSession), "BrainstormSessions")]
        internal static class BrainstormSessionMap;

        [TableMap(typeof(BrainstormMessage), "BrainstormMessages")]
        internal static class BrainstormMessageMap;

        public sealed record TaskHead(Guid Id, string Title);

        public sealed record ExecutionLine(Guid Id, int InputTokens);

        public sealed record SessionHead(Guid Id, string Topic);

        public sealed record MessageLine(Guid Id, string Role, string Content);

        """;

    // Row + List: the head is the first row or null, under LIMIT 1; the lines are every row.
    // #427 — both statements run as one `;`-joined command, not an NpgsqlBatch.
    [Fact]
    public void Head_and_lines_by_id()
    {
        VerifyCompiled(Prelude + """
            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class TaskStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(WithExecutionsQuery))]
                public partial Task<(TaskHead? Head, List<ExecutionLine> Executions)> WithExecutions(Guid id, CancellationToken ct);

                private static (IQuery<TaskHead>, IQuery<ExecutionLine>) WithExecutionsQuery(Guid id) =>
                    (Sql.Table<TaskEntity>().Where(t => t.Id == id).Select(t => new TaskHead(t.Id, t.Title)),
                     Sql.Table<ExecutionEntity>()
                        .Where(e => e.TaskId == id)
                        .OrderBy(e => e.ExecutedAt)
                        .Select(e => new ExecutionLine(e.Id, e.InputTokens)));
            }
            """,
            "// LINQ: TaskStore.WithExecutionsQuery",
            "SELECT \"Id\", \"Title\" FROM \"Tasks\" WHERE \"Id\" = @id LIMIT 1;\\nSELECT \"Id\", \"InputTokens\" FROM \"Executions\" WHERE \"TaskId\" = @id ORDER BY \"ExecutedAt\"",
            "__cmd.CommandText = __sql0_WithExecutions;",
            "global::TestApp.TaskHead? __elem0 = null;",
            generated => Assert.DoesNotContain("CreateBatch", generated, StringComparison.Ordinal));
    }

    // Row + List + Scalar: a Count is always one value; a non-nullable Average over no rows
    // throws as LINQ does.
    [Fact]
    public void Head_lines_and_count()
    {
        VerifyCompiled(Prelude + """
            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class TaskStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(WithStatsQuery))]
                public partial ValueTask<(TaskHead? Head, IReadOnlyList<ExecutionLine> Executions, int Count, double AverageInput)> WithStats(Guid id, CancellationToken ct);

                private static (IQuery<TaskHead>, IQuery<ExecutionLine>, IQuery<int>, IQuery<double>) WithStatsQuery(Guid id) =>
                    (Sql.Table<TaskEntity>().Where(t => t.Id == id).Select(t => new TaskHead(t.Id, t.Title)),
                     Sql.Table<ExecutionEntity>().Where(e => e.TaskId == id).OrderBy(e => e.ExecutedAt).Select(e => new ExecutionLine(e.Id, e.InputTokens)),
                     Sql.Table<ExecutionEntity>().Where(e => e.TaskId == id).Count(),
                     Sql.Table<ExecutionEntity>().Where(e => e.TaskId == id).Average(e => e.InputTokens));
            }
            """,
            "SELECT count(*)::int FROM \"Executions\" WHERE \"TaskId\" = @id",
            "FROM \"Executions\" WHERE \"TaskId\" = @id;\\nSELECT avg(",
            "if (__reader.IsDBNull(0)) throw new global::System.InvalidOperationException(\"Sequence contains no elements\");",
            "return (__elem0, __elem1, __elem2, __elem3);");
    }

    // The Brainstorm shape: one session and its messages, filtered and in order. The role is
    // read only by the child statement, so only that command binds it.
    [Fact]
    public void Ordered_and_filtered_children()
    {
        VerifyCompiled(Prelude + """
            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class BrainstormStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(SessionQuery))]
                public partial Task<(SessionHead? Session, List<MessageLine> Messages)> Session(Guid id, string role, CancellationToken ct);

                private static (IQuery<SessionHead>, IQuery<MessageLine>) SessionQuery(Guid id, string role) =>
                    (Sql.Table<BrainstormSession>().Where(s => s.Id == id).Select(s => new SessionHead(s.Id, s.Topic)),
                     Sql.Table<BrainstormMessage>()
                        .Where(m => m.SessionId == id && m.Role != role)
                        .OrderBy(m => m.CreatedAt)
                        .ThenBy(m => m.Id)
                        .Select(m => new MessageLine(m.Id, m.Role, m.Content)));
            }
            """,
            "WHERE \"SessionId\" = @id AND \"Role\" <> @role ORDER BY \"CreatedAt\", \"Id\"",
            "var __p_role = __cmd.CreateParameter();",
            "var __p_id = __cmd.CreateParameter();",
            "__cmd.CommandText = __sql0_Session;");
    }

    // Every terminal value a single LINQ method reads as a scalar is a tuple element too, emitted
    // through the [Query] multi-result path rather than left without an implementation.
    [Theory]
    [InlineData("bool", "Any()")]
    [InlineData("long", "LongCount()")]
    [InlineData("decimal", "Sum(r => r.Cost)")]
    [InlineData("int?", "Max(r => r.Sequence)")]
    [InlineData("DateTimeOffset?", "Max(r => r.ReadAt)")]
    [InlineData("string?", "Max(r => r.Label)")]
    public void Scalar_terminal_elements_compile(string valueType, string terminal)
    {
        var source = Prelude + """
            public sealed class Reading
            {
                public Guid Id { get; private set; }
                public Guid TaskId { get; private set; }
                public decimal Cost { get; private set; }
                public int? Sequence { get; private set; }
                public DateTimeOffset? ReadAt { get; private set; }
                public string Label { get; private set; } = "";
            }

            [TableMap(typeof(Reading), "Readings")]
            internal static class ReadingMap;

            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class TaskStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(WithValueQuery))]
                public partial Task<(TaskHead? Head, VALUE Value)> WithValue(Guid id, CancellationToken ct);

                private static (IQuery<TaskHead>, IQuery<VALUE>) WithValueQuery(Guid id) =>
                    (Sql.Table<TaskEntity>().Where(t => t.Id == id).Select(t => new TaskHead(t.Id, t.Title)),
                     Sql.Table<Reading>().Where(r => r.TaskId == id).TERMINAL);
            }
            """.Replace("VALUE", valueType, StringComparison.Ordinal).Replace("TERMINAL", terminal, StringComparison.Ordinal);

        var (result, compilation) = GeneratorHarness.RunGeneratorAndGetCompilation(source);

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
        Assert.Contains(result.GeneratedTrees, t => t.ToString().Contains("__elem1", StringComparison.Ordinal));
    }

    // A one-column Select is a list element of its values, and a non-null DateTime Max a scalar
    // element that throws over no rows.
    [Fact]
    public void Scalar_list_and_non_null_date_elements()
    {
        VerifyCompiled(Prelude + """
            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class TaskStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(TokensQuery))]
                public partial Task<(List<int> Tokens, DateTime Last)> Tokens(Guid id, CancellationToken ct);

                private static (IQuery<int>, IQuery<DateTime>) TokensQuery(Guid id) =>
                    (Sql.Table<ExecutionEntity>().Where(e => e.TaskId == id).OrderBy(e => e.ExecutedAt).Select(e => e.InputTokens),
                     Sql.Table<ExecutionEntity>().Where(e => e.TaskId == id).Max(e => e.ExecutedAt));
            }
            """,
            "__elem0.Add(__reader.GetInt32(0));",
            "if (__reader.IsDBNull(0)) throw new global::System.InvalidOperationException(\"Sequence contains no elements\");",
            "SELECT max(\"ExecutedAt\") FROM \"Executions\"",
            "WHERE \"TaskId\" = @id ORDER BY \"ExecutedAt\"");
    }

    // #427 — the joined command binds each parameter once, whichever statements read it.
    [Fact]
    public void Each_parameter_binds_once_on_the_joined_command()
    {
        var (result, _) = GeneratorHarness.RunGeneratorAndGetCompilation(Prelude + """
            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class TaskStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(WithExecutionsQuery))]
                public partial Task<(TaskHead? Head, List<ExecutionLine> Executions)> WithExecutions(Guid id, string title, CancellationToken ct);

                private static (IQuery<TaskHead>, IQuery<ExecutionLine>) WithExecutionsQuery(Guid id, string title) =>
                    (Sql.Table<TaskEntity>().Where(t => t.Id == id && t.Title == title).Select(t => new TaskHead(t.Id, t.Title)),
                     Sql.Table<ExecutionEntity>().Where(e => e.TaskId == id).OrderBy(e => e.Id).Select(e => new ExecutionLine(e.Id, e.InputTokens)));
            }
            """);

        Assert.Empty(result.Results[0].Diagnostics);
        var generated = string.Concat(result.GeneratedTrees.Select(t => t.ToString()));
        Assert.Equal(1, Occurrences(generated, "var __p_title = __cmd.CreateParameter();"));
        Assert.Equal(1, Occurrences(generated, "var __p_id = __cmd.CreateParameter();"));
        Assert.DoesNotContain("__p_id_1", generated, StringComparison.Ordinal);
    }

    // #427 — a literal Take(n) sizes the list the emit fills, on every list shape: a list of
    // rows, a list of scalars and both tuple element kinds. A Take past the cap sizes the list
    // at the cap; a parameter Take, or no Take, keeps the unsized list.
    [Fact]
    public void Literal_take_sizes_the_list()
    {
        var (result, compilation) = GeneratorHarness.RunGeneratorAndGetCompilation(Prelude + """
            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class TaskStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(TopQuery))]
                public partial Task<List<TaskHead>> Top(CancellationToken ct);

                [LinqQuery(nameof(TopIdsQuery))]
                public partial Task<List<Guid>> TopIds(CancellationToken ct);

                [LinqQuery(nameof(ManyQuery))]
                public partial Task<IReadOnlyList<TaskHead>> Many(CancellationToken ct);

                [LinqQuery(nameof(PageQuery))]
                public partial Task<List<TaskHead>> Page(int take, CancellationToken ct);

                [LinqQuery(nameof(WithExecutionsQuery))]
                public partial Task<(List<ExecutionLine> Executions, List<int> Tokens, List<ExecutionLine> All)> WithExecutions(Guid id, CancellationToken ct);

                private static IQuery<TaskHead> TopQuery() =>
                    Sql.Table<TaskEntity>().OrderBy(t => t.CreatedAt).Take(25).Select(t => new TaskHead(t.Id, t.Title));

                private static IQuery<Guid> TopIdsQuery() =>
                    Sql.Table<TaskEntity>().OrderBy(t => t.CreatedAt).Take(7).Select(t => t.Id);

                private static IQuery<TaskHead> ManyQuery() =>
                    Sql.Table<TaskEntity>().OrderBy(t => t.CreatedAt).Take(50000).Select(t => new TaskHead(t.Id, t.Title));

                private static IQuery<TaskHead> PageQuery(int take) =>
                    Sql.Table<TaskEntity>().OrderBy(t => t.CreatedAt).Take(take).Select(t => new TaskHead(t.Id, t.Title));

                private static (IQuery<ExecutionLine>, IQuery<int>, IQuery<ExecutionLine>) WithExecutionsQuery(Guid id) =>
                    (Sql.Table<ExecutionEntity>().Where(e => e.TaskId == id).OrderBy(e => e.Id).Take(10).Select(e => new ExecutionLine(e.Id, e.InputTokens)),
                     Sql.Table<ExecutionEntity>().Where(e => e.TaskId == id).OrderBy(e => e.Id).Take(3).Select(e => e.InputTokens),
                     Sql.Table<ExecutionEntity>().Where(e => e.TaskId == id).OrderBy(e => e.Id).Select(e => new ExecutionLine(e.Id, e.InputTokens)));
            }
            """);

        Assert.Empty(result.Results[0].Diagnostics);
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = string.Concat(result.GeneratedTrees.Select(t => t.ToString()));
        Assert.Contains("new global::System.Collections.Generic.List<global::TestApp.TaskHead>(25);", generated, StringComparison.Ordinal);
        Assert.Contains("new global::System.Collections.Generic.List<global::System.Guid>(7);", generated, StringComparison.Ordinal);
        Assert.Contains("new global::System.Collections.Generic.List<global::TestApp.TaskHead>(1024);", generated, StringComparison.Ordinal);
        Assert.Contains("new global::System.Collections.Generic.List<global::TestApp.TaskHead>();", generated, StringComparison.Ordinal);
        Assert.Contains("new global::System.Collections.Generic.List<global::TestApp.ExecutionLine>(10);", generated, StringComparison.Ordinal);
        Assert.Contains("new global::System.Collections.Generic.List<int>(3);", generated, StringComparison.Ordinal);
        Assert.Contains("new global::System.Collections.Generic.List<global::TestApp.ExecutionLine>();", generated, StringComparison.Ordinal);
    }

    // A nullable outer tuple keeps [Query]'s meaning: null when the first result set is empty.
    [Fact]
    public void Nullable_outer_tuple()
    {
        VerifyCompiled(Prelude + """
            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class TaskStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(WithExecutionsQuery))]
                public partial Task<(TaskHead? Head, List<ExecutionLine> Executions)?> WithExecutions(Guid id, CancellationToken ct);

                private static (IQuery<TaskHead>, IQuery<ExecutionLine>) WithExecutionsQuery(Guid id) =>
                    (Sql.Table<TaskEntity>().Where(t => t.Id == id).Select(t => new TaskHead(t.Id, t.Title)),
                     Sql.Table<ExecutionEntity>().Where(e => e.TaskId == id).OrderBy(e => e.Id).Select(e => new ExecutionLine(e.Id, e.InputTokens)));
            }
            """,
            "LIMIT 1;\\nSELECT",
            "    return null;",
            "__cmd.CommandText = __sql0_WithExecutions;",
            "await __reader.NextResultAsync(ct)");
    }

    // Each element of the method's tuple must fit its companion element; the message names the
    // element that does not.
    [Theory]
    [InlineData("Task<(TaskHead Head, List<ExecutionLine> Executions)>", "TaskHead Head", "tuple element 'Head'")]
    [InlineData("Task<(TaskHead? Head, List<TaskHead> Executions)>", "List<TaskHead> Executions", "tuple element 'Executions'")]
    [InlineData("Task<(TaskHead? Head, ExecutionLine[] Executions)>", "ExecutionLine[] Executions", "tuple element 'Executions'")]
    [InlineData("Task<(TaskHead? Head, List<ExecutionLine> Executions, int Count)>", "Task<(TaskHead? Head, List<ExecutionLine> Executions, int Count)>", "3 elements")]
    [InlineData("Task<List<ExecutionLine>>", "Task<List<ExecutionLine>>", "a tuple")]
    public void Tuple_shape_mismatch_reports_ZAO112(string returnType, string expectedLocation, string expectedMessage)
    {
        var (d, located) = Single(Prelude + """
            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class TaskStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(WithExecutionsQuery))]
                public partial RETURN WithExecutions(Guid id, CancellationToken ct);

                private static (IQuery<TaskHead>, IQuery<ExecutionLine>) WithExecutionsQuery(Guid id) =>
                    (Sql.Table<TaskEntity>().Where(t => t.Id == id).Select(t => new TaskHead(t.Id, t.Title)),
                     Sql.Table<ExecutionEntity>().Where(e => e.TaskId == id).OrderBy(e => e.Id).Select(e => new ExecutionLine(e.Id, e.InputTokens)));
            }
            """.Replace("RETURN", returnType, StringComparison.Ordinal));

        Assert.Equal("ZAO112", d.Id);
        Assert.Equal(expectedLocation, located);
        Assert.Contains(expectedMessage, d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    // A Sum, Min, Max or Average element gives one value, which a list element cannot read.
    [Fact]
    public void Value_terminal_into_a_list_element_reports_ZAO112()
    {
        var (d, located) = Single(Prelude + """
            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class TaskStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(StatsQuery))]
                public partial Task<(TaskHead? Head, List<double> Average)> Stats(Guid id, CancellationToken ct);

                private static (IQuery<TaskHead>, IQuery<double>) StatsQuery(Guid id) =>
                    (Sql.Table<TaskEntity>().Where(t => t.Id == id).Select(t => new TaskHead(t.Id, t.Title)),
                     Sql.Table<ExecutionEntity>().Where(e => e.TaskId == id).Average(e => e.InputTokens));
            }
            """);

        Assert.Equal("ZAO112", d.Id);
        Assert.Equal("List<double> Average", located);
        Assert.Contains("tuple element 'Average'", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    // The generator reads each chain from the tuple literal; a tuple built any other way has no
    // chains to read.
    [Theory]
    [InlineData("Pair(id)")]
    [InlineData("default")]
    public void Non_tuple_companion_body_reports_ZAO100(string body)
    {
        var (d, located) = Single(Prelude + """
            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class TaskStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(WithExecutionsQuery))]
                public partial Task<(TaskHead? Head, List<ExecutionLine> Executions)> WithExecutions(Guid id, CancellationToken ct);

                private static (IQuery<TaskHead>, IQuery<ExecutionLine>) WithExecutionsQuery(Guid id) => BODY;

                private static (IQuery<TaskHead>, IQuery<ExecutionLine>) Pair(Guid id) =>
                    (Sql.Table<TaskEntity>().Where(t => t.Id == id).Select(t => new TaskHead(t.Id, t.Title)),
                     Sql.Table<ExecutionEntity>().Where(e => e.TaskId == id).OrderBy(e => e.Id).Select(e => new ExecutionLine(e.Id, e.InputTokens)));
            }
            """.Replace("BODY", body, StringComparison.Ordinal));

        Assert.Equal("ZAO100", d.Id);
        Assert.Equal("WithExecutionsQuery", located);
        Assert.Contains("tuple literal", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    // A tuple element that is no IQuery<T> is not a chain.
    [Fact]
    public void Tuple_companion_with_a_non_query_element_reports_ZAO100()
    {
        var (d, located) = Single(Prelude + """
            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class TaskStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(WithExecutionsQuery))]
                public partial Task<(TaskHead? Head, int Count)> WithExecutions(Guid id, CancellationToken ct);

                private static (IQuery<TaskHead>, int) WithExecutionsQuery(Guid id) =>
                    (Sql.Table<TaskEntity>().Where(t => t.Id == id).Select(t => new TaskHead(t.Id, t.Title)), 0);
            }
            """);

        Assert.Equal("ZAO100", d.Id);
        Assert.Equal("(IQuery<TaskHead>, int)", located);
    }

    // A parameter binds one way for every statement, so an enum parameter compared with a
    // column that stores names in one statement and integers in another cannot bind for both.
    [Fact]
    public void Enum_parameter_stored_differently_across_statements_reports_ZAO111()
    {
        var (d, _) = Single(Prelude + """
            public enum Stage { Draft, Done }

            public sealed class Ticket
            {
                public Guid Id { get; private set; }
                public Stage Stage { get; private set; }
            }

            public sealed class TicketEvent
            {
                public Guid Id { get; private set; }
                public Stage Stage { get; private set; }
            }

            [TableMap(typeof(Ticket), "Tickets")]
            [ColumnMap(nameof(Ticket.Stage), EnumAsString = true)]
            internal static class TicketMap;

            [TableMap(typeof(TicketEvent), "TicketEvents")]
            internal static class TicketEventMap;

            public sealed record TicketLine(Guid Id);

            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class TicketStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(ByStageQuery))]
                public partial Task<(List<TicketLine> Tickets, List<TicketLine> Events)> ByStage(Stage stage, CancellationToken ct);

                private static (IQuery<TicketLine>, IQuery<TicketLine>) ByStageQuery(Stage stage) =>
                    (Sql.Table<Ticket>().Where(t => t.Stage == stage).OrderBy(t => t.Id).Select(t => new TicketLine(t.Id)),
                     Sql.Table<TicketEvent>().Where(e => e.Stage == stage).OrderBy(e => e.Id).Select(e => new TicketLine(e.Id)));
            }
            """);

        Assert.Equal("ZAO111", d.Id);
        Assert.Contains("stage", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private static (Diagnostic Diagnostic, string Located) Single(string source)
    {
        var (result, tree) = GeneratorHarness.RunGeneratorOnFile(source);
        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Same(tree, diagnostic.Location.SourceTree);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        var span = diagnostic.Location.SourceSpan;
        return (diagnostic, tree.ToString().Substring(span.Start, span.Length));
    }

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private static void VerifyCompiled(
        string source,
        string expected1,
        string expected2,
        string expected3,
        string expected4,
        [CallerFilePath] string testFilePath = "",
        [CallerMemberName] string testMethod = "")
        => VerifyCompiled(source, expected1, expected2, expected3, expected4, static _ => { }, testFilePath, testMethod);

    // Each of `expected` is text the generated files must contain, checked before the snapshot.
    private static void VerifyCompiled(
        string source,
        string expected1,
        string expected2,
        string expected3,
        string expected4,
        Action<string> check,
        [CallerFilePath] string testFilePath = "",
        [CallerMemberName] string testMethod = "")
    {
        var (result, compilation) = GeneratorHarness.RunGeneratorAndGetCompilation(source);

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
        Assert.Contains(expected3, generated, StringComparison.Ordinal);
        Assert.Contains(expected4, generated, StringComparison.Ordinal);
        check(generated);

        GeneratorSnapshot.Verify(result, testFilePath, testMethod);
    }
}
