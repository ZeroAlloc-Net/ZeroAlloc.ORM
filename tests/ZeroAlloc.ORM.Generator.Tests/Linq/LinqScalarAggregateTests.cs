using System;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Xunit;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Generator.Tests.Linq;

// [LinqQuery] scalar aggregates: Sum, Average, Min and Max over a column, read through the
// [Query] scalar shapes. Each renders so the result equals LINQ to Objects', and in exactly the
// CLR type the scalar shape reads. Every snapshot is also compiled: no error anywhere and no
// warning in the emitted code.
public class LinqScalarAggregateTests
{
    private const string Repo = """
        [Dialect(SqlDialect.PostgreSql)]
        public sealed partial class TaskStore(IAsyncDbConnection connection)
        {

        """;

    // LINQ's Sum is 0 for no rows, nullable selectors included, so every Sum is COALESCEd. The
    // cast makes PostgreSQL's type the CLR one: sum(int) is bigint and sum(bigint) numeric. A
    // widening cast in the selector picks the overload and the result cast, not a column cast,
    // except over real: sum(real) adds in float4, so the column is cast to add doubles as LINQ.
    [Fact]
    public void Sum_int_long_decimal_double_and_nullable()
    {
        VerifyCompiled(Repo + """
                [LinqQuery(nameof(SumOrderQuery))]
                public partial Task<int> SumOrder(CancellationToken ct);

                [LinqQuery(nameof(SumSizeQuery))]
                public partial Task<long> SumSize(CancellationToken ct);

                [LinqQuery(nameof(SumOrderWideQuery))]
                public partial Task<long> SumOrderWide(CancellationToken ct);

                [LinqQuery(nameof(SumPriceQuery))]
                public partial ValueTask<decimal> SumPrice(CancellationToken ct);

                [LinqQuery(nameof(SumOrderAsDecimalQuery))]
                public partial Task<decimal> SumOrderAsDecimal(CancellationToken ct);

                [LinqQuery(nameof(SumRatioQuery))]
                public partial Task<double> SumRatio(CancellationToken ct);

                [LinqQuery(nameof(SumWeightQuery))]
                public partial Task<double> SumWeight(CancellationToken ct);

                [LinqQuery(nameof(SumPriorityQuery))]
                public partial Task<int?> SumPriority(CancellationToken ct);

                [LinqQuery(nameof(SumPriorityWideQuery))]
                public partial Task<long?> SumPriorityWide(CancellationToken ct);

                private static IQuery<int> SumOrderQuery() => Sql.Table<TaskEntity>().Sum(t => t.Order);

                private static IQuery<long> SumSizeQuery() => Sql.Table<TaskEntity>().Sum(t => t.Size);

                private static IQuery<long> SumOrderWideQuery() => Sql.Table<TaskEntity>().Sum(t => (long)t.Order);

                private static IQuery<decimal> SumPriceQuery() => Sql.Table<TaskEntity>().Sum(t => t.Price);

                private static IQuery<decimal> SumOrderAsDecimalQuery() => Sql.Table<TaskEntity>().Sum(t => (decimal)t.Order);

                private static IQuery<double> SumRatioQuery() => Sql.Table<TaskEntity>().Sum(t => t.Ratio);

                private static IQuery<double> SumWeightQuery() => Sql.Table<TaskEntity>().Sum(t => (double)t.Weight);

                private static IQuery<int?> SumPriorityQuery() => Sql.Table<TaskEntity>().Sum(t => t.Priority);

                private static IQuery<long?> SumPriorityWideQuery() => Sql.Table<TaskEntity>().Sum(t => (long?)t.Priority);
            }
            """);
    }

    // avg over an integer is numeric in PostgreSQL, so a double result is cast; a decimal one is
    // numeric already. No rows give NULL: null for a nullable result, a throw for a non-null one.
    [Fact]
    public void Average_int_long_double_decimal_and_nullable()
    {
        VerifyCompiled(Repo + """
                [LinqQuery(nameof(AverageOrderQuery))]
                public partial Task<double> AverageOrder(CancellationToken ct);

                [LinqQuery(nameof(AverageSizeQuery))]
                public partial Task<double> AverageSize(CancellationToken ct);

                [LinqQuery(nameof(AverageRatioQuery))]
                public partial Task<double> AverageRatio(CancellationToken ct);

                [LinqQuery(nameof(AveragePriceQuery))]
                public partial Task<decimal> AveragePrice(CancellationToken ct);

                [LinqQuery(nameof(AveragePriorityQuery))]
                public partial Task<double?> AveragePriority(CancellationToken ct);

                [LinqQuery(nameof(AverageOrderNullableQuery))]
                public partial Task<double?> AverageOrderNullable(CancellationToken ct);

                [LinqQuery(nameof(AveragePriceNullableQuery))]
                public partial Task<decimal?> AveragePriceNullable(CancellationToken ct);

                private static IQuery<double> AverageOrderQuery() => Sql.Table<TaskEntity>().Average(t => t.Order);

                private static IQuery<double> AverageSizeQuery() => Sql.Table<TaskEntity>().Average(t => t.Size);

                private static IQuery<double> AverageRatioQuery() => Sql.Table<TaskEntity>().Average(t => t.Ratio);

                private static IQuery<decimal> AveragePriceQuery() => Sql.Table<TaskEntity>().Average(t => t.Price);

                private static IQuery<double?> AveragePriorityQuery() => Sql.Table<TaskEntity>().Average(t => t.Priority);

                private static IQuery<double?> AverageOrderNullableQuery() => Sql.Table<TaskEntity>().Average(t => (double?)t.Order);

                private static IQuery<decimal?> AveragePriceNullableQuery() => Sql.Table<TaskEntity>().Average(t => (decimal?)t.Price);
            }
            """);
    }

    // min and max keep the column's type, so only a widening cast adds a result cast. A
    // reference or nullable result is null for no rows; a non-null one throws.
    [Fact]
    public void Min_max_value_and_nullable()
    {
        VerifyCompiled(Repo + """
                [LinqQuery(nameof(MinOrderQuery))]
                public partial Task<int> MinOrder(CancellationToken ct);

                [LinqQuery(nameof(MaxOrderWideQuery))]
                public partial Task<long> MaxOrderWide(CancellationToken ct);

                [LinqQuery(nameof(MaxPriceQuery))]
                public partial Task<decimal> MaxPrice(CancellationToken ct);

                [LinqQuery(nameof(MaxPriorityQuery))]
                public partial Task<int?> MaxPriority(CancellationToken ct);

                [LinqQuery(nameof(MaxOrderNullableQuery))]
                public partial Task<int?> MaxOrderNullable(CancellationToken ct);

                [LinqQuery(nameof(MinRatioQuery))]
                public partial Task<double?> MinRatio(CancellationToken ct);

                [LinqQuery(nameof(MinTitleQuery))]
                public partial Task<string?> MinTitle(CancellationToken ct);

                [LinqQuery(nameof(MaxNoteQuery))]
                public partial Task<string?> MaxNote(CancellationToken ct);

                [LinqQuery(nameof(MaxDueAtQuery))]
                public partial Task<DateTimeOffset?> MaxDueAt(CancellationToken ct);

                [LinqQuery(nameof(MinCreatedAtQuery))]
                public partial Task<DateTimeOffset?> MinCreatedAt(CancellationToken ct);

                [LinqQuery(nameof(MaxLocalAtQuery))]
                public partial Task<DateTime?> MaxLocalAt(CancellationToken ct);

                private static IQuery<int> MinOrderQuery() => Sql.Table<TaskEntity>().Min(t => t.Order);

                private static IQuery<long> MaxOrderWideQuery() => Sql.Table<TaskEntity>().Max(t => (long)t.Order);

                private static IQuery<decimal> MaxPriceQuery() => Sql.Table<TaskEntity>().Max(t => t.Price);

                private static IQuery<int?> MaxPriorityQuery() => Sql.Table<TaskEntity>().Max(t => t.Priority);

                private static IQuery<int?> MaxOrderNullableQuery() => Sql.Table<TaskEntity>().Max(t => (int?)t.Order);

                private static IQuery<double?> MinRatioQuery() => Sql.Table<TaskEntity>().Min(t => (double?)t.Ratio);

                private static IQuery<string> MinTitleQuery() => Sql.Table<TaskEntity>().Min(t => t.Title);

                private static IQuery<string?> MaxNoteQuery() => Sql.Table<TaskEntity>().Max(t => t.Note);

                private static IQuery<DateTimeOffset?> MaxDueAtQuery() => Sql.Table<TaskEntity>().Max(t => t.DueAt);

                private static IQuery<DateTimeOffset?> MinCreatedAtQuery() => Sql.Table<TaskEntity>().Min(t => (DateTimeOffset?)t.CreatedAt);

                private static IQuery<DateTime?> MaxLocalAtQuery() => Sql.Table<TaskEntity>().Max(t => (DateTime?)t.LocalAt);
            }
            """);
    }

    // A nullable ordering key keeps its NULLS FIRST inside the paged subquery, where it decides
    // which rows the sum reads.
    [Fact]
    public void Paged_aggregate_ordered_by_nullable_key()
    {
        VerifyCompiled(Repo + """
                [LinqQuery(nameof(SumEarliestQuery))]
                public partial Task<long> SumEarliest(int take, CancellationToken ct);

                private static IQuery<long> SumEarliestQuery(int take) =>
                    Sql.Table<TaskEntity>()
                        .OrderBy(t => t.DueAt)
                        .Take(take)
                        .Sum(t => t.Size);
            }
            """);
    }

    // The WHERE applies as for any chain, and a projection before the aggregate is dropped with
    // the parameter only it used. A paged aggregate reads a paged subquery, which keeps the ORDER
    // BY: unlike a count, which rows are in the page changes a sum.
    [Fact]
    public void Scalar_aggregate_with_filter_and_paging()
    {
        VerifyCompiled(Repo + """
                [LinqQuery(nameof(SumOpenQuery))]
                public partial Task<long> SumOpen(int min, CancellationToken ct);

                [LinqQuery(nameof(AveragePageQuery))]
                public partial Task<double?> AveragePage(int skip, int take, CancellationToken ct);

                [LinqQuery(nameof(MaxOfTopQuery))]
                public partial Task<int?> MaxOfTop(CancellationToken ct);

                [LinqQuery(nameof(SumPageQuery))]
                public partial Task<int> SumPage(int take, CancellationToken ct);

                [LinqQuery(nameof(SumWeightPageQuery))]
                public partial Task<double> SumWeightPage(CancellationToken ct);

                private static IQuery<long> SumOpenQuery(int min) =>
                    Sql.Table<TaskEntity>()
                        .Where(t => t.Status == WorkStatus.Open && t.Order >= min)
                        .Sum(t => t.Size);

                private static IQuery<double?> AveragePageQuery(int skip, int take) =>
                    Sql.Table<TaskEntity>()
                        .Where(t => t.IsActive)
                        .OrderBy(t => t.CreatedAt)
                        .ThenBy(t => t.Id)
                        .Skip(skip)
                        .Take(take)
                        .Average(t => t.Priority);

                private static IQuery<int?> MaxOfTopQuery() =>
                    Sql.Table<TaskEntity>()
                        .OrderByDescending(t => t.CreatedAt)
                        .Take(10)
                        .Max(t => t.Priority);

                private static IQuery<int> SumPageQuery(int take) =>
                    Sql.Table<TaskEntity>()
                        .OrderBy(t => t.Order)
                        .Take(take)
                        .Sum(t => t.Order);

                // The subquery selects the bare column; the double precision cast is outside.
                private static IQuery<double> SumWeightPageQuery() =>
                    Sql.Table<TaskEntity>()
                        .OrderBy(t => t.Order)
                        .Take(5)
                        .Sum(t => (double)t.Weight);
            }
            """);
    }

    // The method's return type must be the aggregate's, and one [Query] reads as a scalar.
    [Theory]
    [InlineData("IQuery<Guid>", ".Min(t => t.Id)", "Task<Guid>")]
    [InlineData("IQuery<Guid?>", ".Max(t => (Guid?)t.Id)", "Task<Guid?>")]
    [InlineData("IQuery<DateTime>", ".Max(t => t.LocalAt)", "Task<DateTime>")]
    [InlineData("IQuery<float>", ".Min(t => t.Weight)", "Task<float>")]
    [InlineData("IQuery<short>", ".Max(t => t.Rank)", "Task<short>")]
    [InlineData("IQuery<WorkStatus>", ".Max(t => t.Status)", "Task<WorkStatus>")]
    [InlineData("IQuery<WorkKind>", ".Max(t => t.Kind)", "Task<WorkKind>")]
    [InlineData("IQuery<Origin?>", ".Min(t => (Origin?)t.Origin)", "Task<Origin?>")]
    [InlineData("IQuery<string>", ".Min(t => t.Title)", "Task<string>")]
    [InlineData("IQuery<int?>", ".Sum(t => t.Priority)", "Task<int>")]
    [InlineData("IQuery<double>", ".Average(t => t.Order)", "Task<decimal>")]
    [InlineData("IQuery<double>", ".Average(t => t.Order)", "Task<List<double>>")]
    public void Unsupported_aggregate_result_reports_ZAO112(string queryType, string terminal, string returnType)
    {
        var (result, tree) = GeneratorHarness.RunGeneratorOnFile(LinqWhereTests.Prelude + Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial
            """ + " " + returnType + """
             Find(CancellationToken ct);

                private static
            """ + " " + queryType + """
             FindQuery() =>
                    Sql.Table<TaskEntity>()
            """ + terminal + """
            ;
            }
            """);

        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Equal("ZAO112", diagnostic.Id);
        var span = diagnostic.Location.SourceSpan;
        Assert.Equal(returnType, tree.ToString().Substring(span.Start, span.Length));
    }

    // The aggregated value is a column, or a column cast to a wider type; PostgreSQL has no min
    // or max over boolean.
    [Theory]
    [InlineData("IQuery<int>", ".Sum(t => t.Order + t.Order)", "Task<int>", "t.Order + t.Order")]
    [InlineData("IQuery<int>", ".Sum(t => (int)t.Size)", "Task<int>", "(int)t.Size")]
    [InlineData("IQuery<int>", ".Sum(t => (int)t.Status)", "Task<int>", "(int)t.Status")]
    [InlineData("IQuery<double>", ".Average(t => (double)t.Price)", "Task<double>", "(double)t.Price")]
    [InlineData("IQuery<int>", ".Sum(t => min)", "Task<int>", "min")]
    [InlineData("IQuery<int>", ".Sum(t => 1)", "Task<int>", "1")]
    [InlineData("IQuery<bool>", ".Max(t => t.IsActive)", "Task<bool>", "t.IsActive")]
    [InlineData("IQuery<string>", ".Select(t => new TaskSummary(t.Id, t.Title)).Max(s => s.Title)", "Task<string?>", "Max")]
    [InlineData("IQuery<int>", ".Sum(t => t.Order).Where(n => n > 1)", "Task<int>", "Where")]
    public void Unsupported_aggregate_selector_reports_ZAO102(string queryType, string chain, string returnType, string located)
    {
        var (result, tree) = GeneratorHarness.RunGeneratorOnFile(LinqWhereTests.Prelude + Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial
            """ + " " + returnType + """
             Find(int min, CancellationToken ct);

                private static
            """ + " " + queryType + """
             FindQuery(int min) =>
                    Sql.Table<TaskEntity>()
            """ + chain + """
            ;
            }
            """);

        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Equal("ZAO102", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        var span = diagnostic.Location.SourceSpan;
        Assert.Equal(located, tree.ToString().Substring(span.Start, span.Length));
    }

    // A sum or a max over a page depends on which rows the page holds, so Take without OrderBy
    // warns, as it does for a list. The method is still generated.
    [Fact]
    public void Paged_value_aggregate_without_order_warns_ZAO110()
    {
        var (result, tree) = GeneratorHarness.RunGeneratorOnFile(LinqWhereTests.Prelude + Repo + """
                [LinqQuery(nameof(SumQuery))]
                public partial Task<int> SumTop(CancellationToken ct);

                private static IQuery<int> SumQuery() =>
                    Sql.Table<TaskEntity>().Take(10).Sum(t => t.Order);
            }
            """);

        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Equal("ZAO110", diagnostic.Id);
        var span = diagnostic.Location.SourceSpan;
        Assert.Equal("Take", tree.ToString().Substring(span.Start, span.Length));
        Assert.Single(result.Results[0].GeneratedSources);
    }

    private static void VerifyCompiled(
        string body,
        [CallerFilePath] string testFilePath = "",
        [CallerMemberName] string testMethod = "")
    {
        var (result, compilation) = GeneratorHarness.RunGeneratorAndGetCompilation(LinqWhereTests.Prelude + body);

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

        GeneratorSnapshot.Verify(result, testFilePath, testMethod);
    }
}
