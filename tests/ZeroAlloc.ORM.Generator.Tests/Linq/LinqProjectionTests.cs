using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Xunit;

using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Generator.Tests.Linq;

// [LinqQuery] projections: the companion's Select becomes the SELECT list, in the record
// constructor's parameter order, and the method takes the [Query] path. Every snapshot is
// also compiled: the whole compilation must have no error and the emitted code no warning.
public class LinqProjectionTests
{
    internal const string Prelude = """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using System.Data.Async;
        using System.Runtime.CompilerServices;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.ORM;
        using ZeroAlloc.ORM.Linq;

        namespace TestApp;

        public enum WorkStatus { Open, Done }

        public enum WorkKind { Bug, Feature }

        public sealed class TaskEntity
        {
            public Guid Id { get; private set; }
            public string Title { get; private set; } = "";
            public WorkStatus Status { get; private set; }
            public WorkKind Kind { get; private set; }
            public DateTimeOffset? DueAt { get; private set; }
            public int Order { get; private set; }
        }

        [TableMap(typeof(TaskEntity), "Tasks")]
        [ColumnMap(nameof(TaskEntity.Kind), EnumAsString = true)]
        internal static class TaskMap;

        public sealed record TaskSummary(Guid Id, string Title);

        """;

    [Fact]
    public void Select_into_record_list()
    {
        VerifyCompiled(Prelude + """
            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class TaskStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(SummariesQuery))]
                public partial Task<List<TaskSummary>> Summaries(CancellationToken ct);

                private static IQuery<TaskSummary> SummariesQuery() =>
                    Sql.Table<TaskEntity>().Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);
    }

    [Fact]
    public void Select_into_nullable_single_appends_limit_1()
    {
        VerifyCompiled(Prelude + """
            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class TaskStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(FirstQuery))]
                public partial Task<TaskSummary?> First(CancellationToken ct);

                private static IQuery<TaskSummary> FirstQuery()
                {
                    return Sql.Table<TaskEntity>().Select(t => new TaskSummary(t.Id, t.Title));
                }
            }
            """);
    }

    [Fact]
    public void Select_streaming()
    {
        VerifyCompiled(Prelude + """
            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class TaskStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(StreamQuery))]
                public partial IAsyncEnumerable<TaskSummary> Stream([EnumeratorCancellation] CancellationToken ct);

                private static IQuery<TaskSummary> StreamQuery() =>
                    Sql.Table<TaskEntity>().Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);
    }

    [Fact]
    public void ValueTask_list_and_single_row()
    {
        VerifyCompiled(Prelude + """
            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class TaskStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(SummariesQuery))]
                public partial ValueTask<IReadOnlyList<TaskSummary>> Summaries(CancellationToken ct);

                [LinqQuery(nameof(SummariesQuery))]
                public partial ValueTask<TaskSummary?> First(CancellationToken ct);

                private static IQuery<TaskSummary> SummariesQuery() =>
                    Sql.Table<TaskEntity>().Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);
    }

    [Fact]
    public void Named_projection_arguments_follow_ctor_order()
    {
        VerifyCompiled(Prelude + """
            public sealed record TaskLine(Guid Id, string Title, int Order);

            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class TaskStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(LinesQuery))]
                public partial Task<IReadOnlyList<TaskLine>> Lines(CancellationToken ct);

                private static IQuery<TaskLine> LinesQuery() =>
                    Sql.Table<TaskEntity>().Select(t => new TaskLine(Order: t.Order, Title: t.Title, Id: t.Id));
            }
            """);
    }

    [Fact]
    public void Reserved_and_mixed_case_identifiers_are_quoted()
    {
        VerifyCompiled("""
            #nullable enable
            using System;
            using System.Collections.Generic;
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;
            using ZeroAlloc.ORM.Linq;

            namespace TestApp;

            public sealed class Item
            {
                public Guid Id { get; private set; }
                public int Order { get; private set; }
                public string Name { get; private set; } = "";
            }

            [TableMap(typeof(Item), "Work\"Items", Schema = "app")]
            [ColumnMap(nameof(Item.Name), Column = "display \"name\"")]
            internal static class ItemMap;

            public sealed record ItemRow(Guid Id, int Order, string Name);

            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class ItemStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(AllQuery))]
                public partial Task<IList<ItemRow>> All(CancellationToken ct);

                private static IQuery<ItemRow> AllQuery() =>
                    Sql.Table<Item>().Select(i => new ItemRow(i.Id, i.Order, i.Name));
            }
            """);
    }

    [Fact]
    public void EnumAsString_column_reads_through_Enum_Parse()
    {
        VerifyCompiled(Prelude + """
            public sealed record TaskKinds(Guid Id, WorkStatus Status, WorkKind Kind, WorkKind? MaybeKind);

            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class TaskStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(KindsQuery))]
                public partial Task<List<TaskKinds>> Kinds(CancellationToken ct);

                private static IQuery<TaskKinds> KindsQuery() =>
                    Sql.Table<TaskEntity>().Select(t => new TaskKinds(t.Id, t.Status, t.Kind, t.Kind));
            }
            """);
    }

    [Fact]
    public void Projection_takes_parameters_and_literals()
    {
        VerifyCompiled(Prelude + """
            [StoreAsString]
            public enum Origin { Manual, Imported }

            public sealed record Tagged(
                Guid Id, string Label, int Rank, long Big, bool Flag, string Quote, string? Missing,
                WorkStatus Status, Origin Origin, double Ratio);

            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class TaskStore(IAsyncDbConnection connection)
            {
                private const int Answer = 42;

                [LinqQuery(nameof(TaggedQuery))]
                public partial Task<List<Tagged>> Tag(string label, [Param(Name = "@r")] int rank, CancellationToken ct);

                private static IQuery<Tagged> TaggedQuery(string label, int rank) =>
                    Sql.Table<TaskEntity>().Select(t => new Tagged(
                        t.Id, label, rank, Answer, true, "it's", null, WorkStatus.Done, Origin.Imported, 0.5));
            }
            """);
    }

    [Fact]
    public void Linq_method_beside_query_method_shares_the_repository()
    {
        VerifyCompiled(Prelude + """
            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class TaskStore(IAsyncDbConnection connection)
            {
                [Query("SELECT count(*)::int FROM \"Tasks\"")]
                public partial Task<int> CountAsync(CancellationToken ct);

                [LinqQuery(nameof(SummariesQuery))]
                public partial Task<List<TaskSummary>> Summaries(IAsyncDbTransaction tx, CancellationToken ct);

                private static IQuery<TaskSummary> SummariesQuery() =>
                    Sql.Table<TaskEntity>().Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);
    }

    // A parameter without a DbType facet that can be null is cast in the SELECT list. PostgreSQL
    // reads the SELECT list first and would type a null untyped parameter as text there, which
    // then clashes with a later use, such as a comparison with a timestamptz column.
    private const string StampStore = """
        public sealed record Stamp(Guid Id, DateTime? At, DateOnly? On, Level? Level, DateTime Fixed);

        public enum Level : byte { Low, High }

        [Dialect(SqlDialect.PostgreSql)]
        public sealed partial class TaskStore(IAsyncDbConnection connection)
        {
            [LinqQuery(nameof(StampsQuery))]
            public partial Task<List<Stamp>> Stamps(DateTime? at, DateOnly? on, Level? level, DateTime fixedAt, CancellationToken ct);

            private static IQuery<Stamp> StampsQuery(DateTime? at, DateOnly? on, Level? level, DateTime fixedAt) =>
                Sql.Table<TaskEntity>().Select(t => new Stamp(t.Id, at, on, level, fixedAt));
        }
        """;

    [Fact]
    public void Projected_parameter_without_a_facet_is_cast()
    {
        var result = GeneratorHarness.RunGenerator(Prelude + StampStore);

        Assert.Empty(result.Results[0].Diagnostics);
        var generated = string.Concat(result.GeneratedTrees.Select(t => t.ToString()));
        Assert.Contains(
            "SELECT \\\"Id\\\", @at::timestamptz, @on::date, @level::smallint, @fixedAt FROM \\\"Tasks\\\"",
            generated,
            System.StringComparison.Ordinal);
    }

    [Fact]
    public void Projected_parameters_cast_where_a_null_needs_a_type()
    {
        VerifyCompiled(Prelude + StampStore);
    }

    // A parameter that can be null, has no facet and no PostgreSQL type to cast to would fail
    // at run time when null, so it is rejected.
    [Fact]
    public void Projected_parameter_without_a_type_reports_ZAO102()
    {
        var (result, tree) = GeneratorHarness.RunGeneratorOnFile(Prelude + """
            public sealed record Blob(Guid Id, byte[]? Data);

            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class TaskStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(BlobsQuery))]
                public partial Task<List<Blob>> Blobs(byte[]? data, CancellationToken ct);

                private static IQuery<Blob> BlobsQuery(byte[]? data) =>
                    Sql.Table<TaskEntity>().Select(t => new Blob(t.Id, data));
            }
            """);

        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Equal("ZAO102", diagnostic.Id);
        var span = diagnostic.Location.SourceSpan;
        Assert.Equal("data", tree.ToString().Substring(span.Start, span.Length));
        Assert.Contains("'byte[]'", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), System.StringComparison.Ordinal);
    }

    internal static void VerifyCompiled(
        string source,
        [CallerFilePath] string testFilePath = "",
        [CallerMemberName] string testMethod = "")
    {
        var (result, compilation) = GeneratorHarness.RunGeneratorAndGetCompilation(source);

        Assert.Empty(result.Results[0].Diagnostics);
        var generatedPaths = result.GeneratedTrees.Select(t => t.FilePath).ToHashSet(System.StringComparer.Ordinal);
        var problems = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error
                || (d.Severity == DiagnosticSeverity.Warning
                    && d.Location.SourceTree is { } tree
                    && generatedPaths.Contains(tree.FilePath)))
            .Select(d => d.Id + ": " + d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))
            .ToList();
        Assert.Empty(problems);

        GeneratorSnapshot.Verify(result, testFilePath, testMethod);
    }
}
