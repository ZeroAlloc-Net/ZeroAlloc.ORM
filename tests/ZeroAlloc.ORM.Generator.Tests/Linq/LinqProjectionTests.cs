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
