using System;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.ORM.Generator.Linq;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Generator.Tests.Linq;

// [LinqQuery] Where clauses: one snapshot per row of the translation table, the DbType facet
// each companion parameter binds with, and the enum storage rule behind ZAO111. Every
// snapshot is also compiled: no error anywhere and no warning in the emitted code.
public class LinqWhereTests
{
    internal const string Prelude = """
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

        public enum WorkStatus { Open, Done }

        public enum WorkKind { Bug, Feature }

        [StoreAsString]
        public enum Origin { Manual, Imported }

        public enum Level : byte { Low, High }

        public enum Tier : byte { Free, Paid }

        public readonly record struct ProjectId(Guid Value)
        {
            public static ProjectId From(Guid value) => new(value);
        }

        public sealed class TaskEntity
        {
            public Guid Id { get; private set; }
            public ProjectId ProjectId { get; private set; }
            public string Title { get; private set; } = "";
            public string? Note { get; private set; }
            public WorkStatus Status { get; private set; }
            public WorkKind Kind { get; private set; }
            public WorkKind PreviousKind { get; private set; }
            public Origin Origin { get; private set; }
            public bool IsActive { get; private set; }
            public DateTimeOffset CreatedAt { get; private set; }
            public DateTimeOffset? DueAt { get; private set; }
            public int Order { get; private set; }
            public long Size { get; private set; }
            public short Rank { get; private set; }
            public decimal Price { get; private set; }
            public double Ratio { get; private set; }
            public float Weight { get; private set; }
            public DateTime LocalAt { get; private set; }
            public Level Level { get; private set; }
            public Tier Tier { get; private set; }
            public DateOnly DueOn { get; private set; }
            public TimeSpan Spent { get; private set; }
            public TimeOnly StartsAt { get; private set; }
            public int? Priority { get; private set; }
            public WorkKind? Category { get; private set; }
        }

        [TableMap(typeof(TaskEntity), "Tasks")]
        [ColumnMap(nameof(TaskEntity.Kind), EnumAsString = true)]
        [ColumnMap(nameof(TaskEntity.Level), EnumAsString = true)]
        internal static class TaskMap;

        public sealed record TaskSummary(Guid Id, string Title);

        """;

    private const string Repo = """
        [Dialect(SqlDialect.PostgreSql)]
        public sealed partial class TaskStore(IAsyncDbConnection connection)
        {

        """;

    [Fact]
    public void Equality_and_inequality()
    {
        VerifyCompiled(Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskSummary>> Find(int order, string title, CancellationToken ct);

                private static IQuery<TaskSummary> FindQuery(int order, string title) =>
                    Sql.Table<TaskEntity>()
                        .Where(t => t.Order == order && t.Title != title)
                        .Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);
    }

    [Fact]
    public void Relational_operators()
    {
        VerifyCompiled(Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskSummary>> Find(int min, int max, long big, DateTimeOffset since, CancellationToken ct);

                private static IQuery<TaskSummary> FindQuery(int min, int max, long big, DateTimeOffset since) =>
                    Sql.Table<TaskEntity>()
                        .Where(t => t.Order > min && t.Order <= max && t.Size >= big && t.Size < 10 && t.CreatedAt < since)
                        .Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);
    }

    [Fact]
    public void Null_checks()
    {
        VerifyCompiled(Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskSummary>> Find(CancellationToken ct);

                private static IQuery<TaskSummary> FindQuery() =>
                    Sql.Table<TaskEntity>()
                        .Where(t => t.DueAt == null || null != t.Note)
                        .Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);
    }

    [Fact]
    public void Logical_operators_are_parenthesised()
    {
        VerifyCompiled(Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskSummary>> Find(int a, int b, CancellationToken ct);

                private static IQuery<TaskSummary> FindQuery(int a, int b) =>
                    Sql.Table<TaskEntity>()
                        .Where(t => (t.Order == a || t.Order == b) && !(t.Size > 3 && t.Rank < 2))
                        .Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);
    }

    [Fact]
    public void Bool_column_alone()
    {
        VerifyCompiled(Repo + """
                [LinqQuery(nameof(ActiveQuery))]
                public partial Task<List<TaskSummary>> Active(CancellationToken ct);

                [LinqQuery(nameof(InactiveQuery))]
                public partial Task<List<TaskSummary>> Inactive(CancellationToken ct);

                private static IQuery<TaskSummary> ActiveQuery() =>
                    Sql.Table<TaskEntity>().Where(t => t.IsActive).Select(t => new TaskSummary(t.Id, t.Title));

                private static IQuery<TaskSummary> InactiveQuery() =>
                    Sql.Table<TaskEntity>().Where(t => !t.IsActive).Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);
    }

    [Fact]
    public void Parameter_renders_by_its_sql_name()
    {
        VerifyCompiled(Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskSummary>> Find([Param(Name = "@o")] int order, CancellationToken ct);

                private static IQuery<TaskSummary> FindQuery(int order) =>
                    Sql.Table<TaskEntity>().Where(t => t.Order == order).Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);
    }

    [Fact]
    public void Constants_inline_as_literals()
    {
        VerifyCompiled(Repo + """
                private const int Limit = 3;

                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskSummary>> Find(CancellationToken ct);

                private static IQuery<TaskSummary> FindQuery() =>
                    Sql.Table<TaskEntity>()
                        .Where(t => t.Status == WorkStatus.Done
                            && t.Title != "it's"
                            && t.Order > Limit
                            && t.Ratio < 0.5
                            && t.IsActive == true
                            && t.Origin == Origin.Imported
                            && t.Kind == WorkKind.Feature
                            && t.PreviousKind != WorkKind.Feature)
                        .Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);
    }

    // A float constant is a real: written as a bare number it would be numeric, which PostgreSQL
    // compares with a real column as double precision, so `0.1f` would never match a stored 0.1.
    [Fact]
    public void Float_literal_compares_as_real()
    {
        var method = BindOnly(Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskSummary>> Find(CancellationToken ct);

                private static IQuery<TaskSummary> FindQuery() =>
                    Sql.Table<TaskEntity>().Where(t => t.Weight == 0.1f || t.Weight < -2.5f || t.Ratio == 0.1).Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);

        Assert.Equal(
            "SELECT \"Id\", \"Title\" FROM \"Tasks\" WHERE ((\"Weight\" = 0.1::real OR \"Weight\" < -2.5::real) OR \"Ratio\" = 0.1)",
            method.Sql);
    }

    [Fact]
    public void Float_literal_renders_with_a_real_cast()
    {
        VerifyCompiled(Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskSummary>> Find(CancellationToken ct);

                private static IQuery<TaskSummary> FindQuery() =>
                    Sql.Table<TaskEntity>().Where(t => t.Weight == 0.1f).Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);
    }

    [Fact]
    public void Contains_on_array_parameter()
    {
        VerifyCompiled(
            Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskSummary>> Find(
                    Guid[] ids, List<string> titles, IReadOnlyList<int> orders, long[] sizes, float[] weights, double[] ratios,
                    CancellationToken ct);

                private static IQuery<TaskSummary> FindQuery(
                    Guid[] ids, List<string> titles, IReadOnlyList<int> orders, long[] sizes, float[] weights, double[] ratios) =>
                    Sql.Table<TaskEntity>()
                        .Where(t => ids.Contains(t.Id) && titles.Contains(t.Title) && orders.Contains(t.Order))
                        .Where(t => Enumerable.Contains(sizes, t.Size))
                        .Where(t => weights.Contains(t.Weight) && ratios.Contains(t.Ratio))
                        .Select(t => new TaskSummary(t.Id, t.Title));
            }
            """,
            NpgsqlStub.Source);
    }

    [Fact]
    public void String_Contains_escapes_like_wildcards()
    {
        VerifyCompiled(Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskSummary>> Find(string text, CancellationToken ct);

                private static IQuery<TaskSummary> FindQuery(string text) =>
                    Sql.Table<TaskEntity>()
                        .Where(t => t.Title.Contains(text) || t.Title.Contains("50%"))
                        .Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);
    }

    [Fact]
    public void StartsWith_escapes_like_wildcards()
    {
        VerifyCompiled(Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskSummary>> Find(string prefix, CancellationToken ct);

                private static IQuery<TaskSummary> FindQuery(string prefix) =>
                    Sql.Table<TaskEntity>().Where(t => t.Title.StartsWith(prefix)).Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);
    }

    [Fact]
    public void EndsWith_escapes_like_wildcards()
    {
        VerifyCompiled(Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskSummary>> Find(string suffix, CancellationToken ct);

                private static IQuery<TaskSummary> FindQuery(string suffix) =>
                    Sql.Table<TaskEntity>().Where(t => t.Title.EndsWith(suffix)).Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);
    }

    // The forms MA0006, MA0074 and CA1310 ask for translate like the plain ones, so a companion
    // in a project with those analyzers needs no suppression.
    [Fact]
    public void Ordinal_string_comparisons_translate_like_their_plain_forms()
    {
        VerifyCompiled(Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskSummary>> Find(string title, string part, CancellationToken ct);

                private static IQuery<TaskSummary> FindQuery(string title, string part) =>
                    Sql.Table<TaskEntity>()
                        .Where(t => string.Equals(t.Title, title, StringComparison.Ordinal) || !string.Equals(t.Note, title))
                        .Where(t => t.Title.StartsWith(part, StringComparison.Ordinal)
                            || t.Title.EndsWith(part, StringComparison.Ordinal)
                            || t.Title.Contains(part, StringComparison.Ordinal))
                        .Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);
    }

    [Fact]
    public void ILike_passes_the_pattern_through()
    {
        VerifyCompiled(Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskSummary>> Find(string pattern, CancellationToken ct);

                private static IQuery<TaskSummary> FindQuery(string pattern) =>
                    Sql.Table<TaskEntity>().Where(t => Sql.ILike(t.Title, pattern)).Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);
    }

    [Fact]
    public void WhereIf_applies_the_predicate_only_when_the_condition_holds()
    {
        VerifyCompiled(Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskSummary>> Find(WorkStatus? status, bool activeOnly, CancellationToken ct);

                private static IQuery<TaskSummary> FindQuery(WorkStatus? status, bool activeOnly) =>
                    Sql.Table<TaskEntity>()
                        .WhereIf(status != null, t => t.Status == status)
                        .WhereIf(activeOnly, t => t.IsActive)
                        .Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);
    }

    // C# skips the filter when the condition is null, as `minOrder > 0` is for a null minOrder:
    // NOT (NULL) would be NULL and drop every row, so the condition reads IS NOT TRUE.
    [Fact]
    public void WhereIf_condition_that_is_null_skips_the_filter()
    {
        var method = BindOnly(Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskSummary>> Find(int? minOrder, CancellationToken ct);

                private static IQuery<TaskSummary> FindQuery(int? minOrder) =>
                    Sql.Table<TaskEntity>().WhereIf(minOrder > 0, t => t.Order >= minOrder).Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);

        Assert.Equal(
            "SELECT \"Id\", \"Title\" FROM \"Tasks\" WHERE ((@minOrder > 0) IS NOT TRUE OR (\"Order\" >= @minOrder))",
            method.Sql);
    }

    // Comparisons follow C#: null equals null only, and a comparison with a null operand is
    // false, so its negation is true. EF-style compensation adds the IS NULL terms where an
    // operand can be null; `!` over something that can be NULL reads IS NOT TRUE.
    [Theory]
    [InlineData("t => t.DueAt == due", "(\"DueAt\" = @due OR (\"DueAt\" IS NULL AND @due IS NULL))")]
    [InlineData("t => t.DueAt != due", "((\"DueAt\" <> @due OR (\"DueAt\" IS NULL AND @due IS NOT NULL)) OR (\"DueAt\" IS NOT NULL AND @due IS NULL))")]
    [InlineData("t => t.Note != title", "(\"Note\" <> @title OR \"Note\" IS NULL)")]
    [InlineData("t => title != t.Note", "(@title <> \"Note\" OR \"Note\" IS NULL)")]
    [InlineData("t => t.Order != maybe", "(\"Order\" <> @maybe OR @maybe IS NULL)")]
    [InlineData("t => t.Note == title", "\"Note\" = @title")]
    [InlineData("t => t.Order == maybe", "\"Order\" = @maybe")]
    [InlineData("t => t.Note == note", "(\"Note\" = @note OR (\"Note\" IS NULL AND @note IS NULL))")]
    [InlineData("t => string.Equals(t.Note, note)", "(\"Note\" = @note OR (\"Note\" IS NULL AND @note IS NULL))")]
    [InlineData("t => !string.Equals(t.Note, title)", "(\"Note\" = @title) IS NOT TRUE")]
    [InlineData("t => t.Category != WorkKind.Feature", "(\"Category\" <> 1 OR \"Category\" IS NULL)")]
    [InlineData("t => t.Kind != kind", "(\"Kind\" <> @kind OR @kind IS NULL)")]
    [InlineData("t => !(t.Priority > order)", "(\"Priority\" > @order) IS NOT TRUE")]
    [InlineData("t => !(t.Order > maybe)", "(\"Order\" > @maybe) IS NOT TRUE")]
    [InlineData("t => !(t.IsActive && t.Priority > order)", "(\"IsActive\" AND \"Priority\" > @order) IS NOT TRUE")]
    [InlineData("t => !(t.Order > order)", "NOT (\"Order\" > @order)")]
    [InlineData("t => !(t.DueAt == null)", "NOT (\"DueAt\" IS NULL)")]
    [InlineData("t => t.Priority > order", "\"Priority\" > @order")]
    public void Comparisons_follow_CSharp_null_semantics(string predicate, string where)
    {
        var method = BindOnly(Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskSummary>> Find(DateTimeOffset? due, string? note, string title, int order, int? maybe, WorkKind? kind, CancellationToken ct);

                private static IQuery<TaskSummary> FindQuery(DateTimeOffset? due, string? note, string title, int order, int? maybe, WorkKind? kind) =>
                    Sql.Table<TaskEntity>().Where(
            """ + predicate + """
            ).Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);

        Assert.Equal("SELECT \"Id\", \"Title\" FROM \"Tasks\" WHERE " + where, method.Sql);
    }

    // The same rules inside a WhereIf condition, which tests parameters only.
    [Fact]
    public void WhereIf_condition_compares_nullable_parameters_as_CSharp_does()
    {
        var method = BindOnly(Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskSummary>> Find(int? a, int? b, CancellationToken ct);

                private static IQuery<TaskSummary> FindQuery(int? a, int? b) =>
                    Sql.Table<TaskEntity>().WhereIf(a == b, t => t.IsActive).Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);

        Assert.Equal(
            "SELECT \"Id\", \"Title\" FROM \"Tasks\" WHERE (((@a = @b OR (@a IS NULL AND @b IS NULL))) IS NOT TRUE OR (\"IsActive\"))",
            method.Sql);
    }

    // A NULL item makes a row comparison NULL, and keyset paging over a nullable key would skip
    // or repeat rows, so a Sql.Row item that can be null is rejected rather than compensated.
    [Theory]
    [InlineData("t => Sql.Row(t.DueAt, t.Id) > Sql.Row(due, id)", "t.DueAt")]
    [InlineData("t => Sql.Row<DateTimeOffset?, Guid>(t.CreatedAt, t.Id) > Sql.Row(due, id)", "due")]
    public void Sql_Row_item_that_can_be_null_reports_ZAO102(string predicate, string located)
    {
        var (result, tree) = GeneratorHarness.RunGeneratorOnFile(Prelude + Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskSummary>> Find(DateTimeOffset? due, Guid id, CancellationToken ct);

                private static IQuery<TaskSummary> FindQuery(DateTimeOffset? due, Guid id) =>
                    Sql.Table<TaskEntity>().Where(
            """ + predicate + """
            ).OrderBy(t => t.CreatedAt).ThenBy(t => t.Id).Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);

        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Equal("ZAO102", diagnostic.Id);
        var span = diagnostic.Location.SourceSpan;
        Assert.Equal(located, tree.ToString().Substring(span.Start, span.Length));
        Assert.Contains("can be null", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    // A value object column's .Value is the column itself, wherever a column is read: compared
    // with a Guid parameter, ordered by and projected.
    [Fact]
    public void Value_of_a_value_object_column_is_the_column()
    {
        VerifyCompiled(Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskProjectRow>> Find(Guid project, CancellationToken ct);

                private static IQuery<TaskProjectRow> FindQuery(Guid project) =>
                    Sql.Table<TaskEntity>()
                        .Where(t => t.ProjectId.Value == project)
                        .OrderBy(t => t.ProjectId.Value)
                        .Select(t => new TaskProjectRow(t.Id, t.ProjectId.Value));
            }

            public sealed record TaskProjectRow(Guid Id, Guid Project);
            """);
    }

    // HasValue on a nullable companion parameter is its null test; a parameter without a DbType
    // facet gets the same cast as `p != null`.
    [Fact]
    public void HasValue_on_a_parameter_is_a_null_test()
    {
        var method = BindOnly(Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskSummary>> Find(int? minOrder, DateTime? at, CancellationToken ct);

                private static IQuery<TaskSummary> FindQuery(int? minOrder, DateTime? at) =>
                    Sql.Table<TaskEntity>()
                        .WhereIf(minOrder.HasValue, t => t.Order >= minOrder)
                        .Where(t => !minOrder.HasValue || t.Order < minOrder)
                        .Where(t => !at.HasValue || t.LocalAt < at)
                        .Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);

        Assert.Equal(
            "SELECT \"Id\", \"Title\" FROM \"Tasks\" WHERE ((@minOrder IS NOT NULL) IS NOT TRUE OR (\"Order\" >= @minOrder))"
                + " AND (@minOrder IS NULL OR \"Order\" < @minOrder) AND (@at::timestamptz IS NULL OR \"LocalAt\" < @at)",
            method.Sql);
    }

    // Any other member of a companion parameter, such as Value, is ZAO102 with a hint; it is a
    // parameter, so ZAO103's "pass it as a companion parameter" would be wrong.
    [Theory]
    [InlineData("t => t.Order >= minOrder.Value", "minOrder.Value", "'Value'")]
    [InlineData("t => title.Length > 2", "title.Length", "'Length'")]
    public void Other_member_of_a_parameter_reports_ZAO102_with_a_hint(string predicate, string located, string member)
    {
        var (result, tree) = GeneratorHarness.RunGeneratorOnFile(Prelude + Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskSummary>> Find(int? minOrder, string title, CancellationToken ct);

                private static IQuery<TaskSummary> FindQuery(int? minOrder, string title) =>
                    Sql.Table<TaskEntity>().Where(
            """ + predicate + """
            ).Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);

        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Equal("ZAO102", diagnostic.Id);
        var span = diagnostic.Location.SourceSpan;
        Assert.Equal(located, tree.ToString().Substring(span.Start, span.Length));
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains(member, message, StringComparison.Ordinal);
        Assert.Contains("rather than the parameter itself", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Several_Wheres_join_with_AND()
    {
        VerifyCompiled(Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskSummary>> Find(int order, CancellationToken ct);

                private static IQuery<TaskSummary> FindQuery(int order) =>
                    Sql.Table<TaskEntity>()
                        .Where(t => t.Order == order || t.Order == 0)
                        .Where(t => t.IsActive)
                        .Where(t => t.Note != null)
                        .Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);
    }

    // Every companion parameter binds with the DbType of its CLR type, so a null value is
    // typed. Arrays and DateTime get none; the CancellationToken and transaction bind nothing.
    [Fact]
    public void Parameters_get_dbtype_facets()
    {
        VerifyCompiled(
            Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskSummary>> Find(
                    int order, long size, short rank, bool active, Guid id, string title, decimal price,
                    double ratio, float weight, DateTimeOffset since, WorkStatus status, Origin origin,
                    ProjectId project, int? maybeOrder, Guid[] ids, DateTime localAt,
                    IAsyncDbTransaction tx, CancellationToken ct);

                private static IQuery<TaskSummary> FindQuery(
                    int order, long size, short rank, bool active, Guid id, string title, decimal price,
                    double ratio, float weight, DateTimeOffset since, WorkStatus status, Origin origin,
                    ProjectId project, int? maybeOrder, Guid[] ids, DateTime localAt) =>
                    Sql.Table<TaskEntity>()
                        .Where(t => t.Order == order && t.Size == size && t.Rank == rank && t.IsActive == active)
                        .Where(t => t.Id == id && t.Title == title && t.Price == price && t.Ratio == ratio)
                        .Where(t => t.Weight == weight && t.CreatedAt >= since && t.Status == status)
                        .Where(t => t.Origin == origin && t.ProjectId == project && t.Order == maybeOrder)
                        .Where(t => ids.Contains(t.Id) && t.LocalAt < localAt)
                        .Select(t => new TaskSummary(t.Id, t.Title));
            }
            """,
            NpgsqlStub.Source);
    }

    // An enum without [StoreAsString] compared with an EnumAsString column binds as its member
    // name, with the String facet, whether nullable or not, and a projected use reads it back
    // by name too.
    [Fact]
    public void Enum_parameter_against_EnumAsString_column_binds_its_name()
    {
        VerifyCompiled(Repo + """
                public sealed record KindRow(Guid Id, WorkKind Asked);

                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<KindRow>> Find(WorkKind kind, WorkKind? other, CancellationToken ct);

                private static IQuery<KindRow> FindQuery(WorkKind kind, WorkKind? other) =>
                    Sql.Table<TaskEntity>()
                        .Where(t => t.Kind == kind)
                        .WhereIf(other != null, t => t.Kind != other)
                        .Select(t => new KindRow(t.Id, kind));
            }
            """);
    }

    // Typed ids compare by their underlying value and bind through the value-object convention.
    [Fact]
    public void Typed_id_and_nullable_enum_parameters_bind_their_underlying_value()
    {
        VerifyCompiled(Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskSummary>> Find(ProjectId? project, WorkStatus? status, Origin? origin, CancellationToken ct);

                private static IQuery<TaskSummary> FindQuery(ProjectId? project, WorkStatus? status, Origin? origin) =>
                    Sql.Table<TaskEntity>()
                        .Where(t => project == null || t.ProjectId == project)
                        .Where(t => status == null || t.Status == status)
                        .Where(t => origin == null || t.Origin == origin)
                        .Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);
    }

    [Fact]
    public void Enum_parameter_compared_with_both_storages_reports_ZAO111()
    {
        var (result, tree) = GeneratorHarness.RunGeneratorOnFile(Prelude + Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskSummary>> Find(WorkKind kind, CancellationToken ct);

                private static IQuery<TaskSummary> FindQuery(WorkKind kind) =>
                    Sql.Table<TaskEntity>()
                        .Where(t => t.Kind == kind || t.PreviousKind == kind)
                        .Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);

        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Equal("ZAO111", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Same(tree, diagnostic.Location.SourceTree);
        Assert.Equal(
            "Parameter 'kind' is compared with or set into columns that store its enum differently",
            diagnostic.GetMessage(CultureInfo.InvariantCulture));
        var span = diagnostic.Location.SourceSpan;
        Assert.Equal("kind", tree.ToString().Substring(span.Start, span.Length));
        Assert.Contains("t.PreviousKind == kind", tree.GetText().Lines.GetLineFromPosition(span.Start).ToString(), StringComparison.Ordinal);
        Assert.Empty(result.Results[0].GeneratedSources);
    }

    // Each generated facet is GetDbTypeNameFromReader of the very reader the parameter binds as,
    // and no PostgreSQL row of DialectDbTypeRejections rejects that pair, so a generated facet
    // cannot itself trip ZAO015. What reaches ZAO015 is the DbTypeCheck the facet records; this
    // pins that each value parameter records one with the reader it binds as.
    [Fact]
    public void Generated_facets_record_the_DbTypeCheck_ZAO015_runs()
    {
        var method = BindOnly(Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskSummary>> Find(int order, WorkKind kind, ProjectId project, Level level, IAsyncDbTransaction tx, CancellationToken ct);

                private static IQuery<TaskSummary> FindQuery(int order, WorkKind kind, ProjectId project, Level level) =>
                    Sql.Table<TaskEntity>()
                        .Where(t => t.Order == order && t.Kind == kind && t.ProjectId == project && t.Level == level)
                        .Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);

        var checks = string.Join(", ", method.DbTypeChecks.Select(c => c.ParameterName + ":" + c.BoundReader + ":" + c.DbTypeDisplay));
        Assert.Equal("order:GetInt32:Int32, kind:GetString:String, project:GetGuid:Guid", checks);
        Assert.All(method.DbTypeChecks, c => Assert.NotNull(c.Location));

        // A byte enum gets no facet of its own; bound by name, it gets String like any other.
        var level = Assert.Single(method.MethodParameters, p => string.Equals(p.Name, "level", StringComparison.Ordinal));
        Assert.Equal("global::System.Data.DbType.String", level.Facets?.DbTypeExpression);
        Assert.Equal("GetString", level.Convention?.UnderlyingReader);
    }

    // A written [Param(DbType)] wins over the facet, and is checked against the reader the
    // parameter finally binds as: an Int32 on an enum that binds by name is rejected.
    [Fact]
    public void Written_dbtype_on_enum_bound_by_name_reports_ZAO015()
    {
        var (result, tree) = GeneratorHarness.RunGeneratorOnFile(Prelude + Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskSummary>> Find([Param(DbType = System.Data.DbType.Int32)] WorkKind kind, CancellationToken ct);

                private static IQuery<TaskSummary> FindQuery(WorkKind kind) =>
                    Sql.Table<TaskEntity>().Where(t => t.Kind == kind).Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);

        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Equal("ZAO015", diagnostic.Id);
        Assert.Same(tree, diagnostic.Location.SourceTree);
        Assert.Contains("'kind'", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Contains("string", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("t => t.Title.ToUpper() == title", "t.Title.ToUpper()")]
    [InlineData("t => t.Order + 1 > 2", "t.Order + 1")]
    [InlineData("t => t.Title.StartsWith(title, StringComparison.OrdinalIgnoreCase)", "t.Title.StartsWith(title, StringComparison.OrdinalIgnoreCase)")]
    [InlineData("t => string.Equals(t.Title, title, StringComparison.CurrentCulture)", "string.Equals(t.Title, title, StringComparison.CurrentCulture)")]
    [InlineData("t => t.Order > 1 & t.IsActive", "t.Order > 1 & t.IsActive")]
    [InlineData("t => t.DueAt > null", "t.DueAt > null")]
    [InlineData("t => kinds.Contains(t.Status)", "kinds.Contains(t.Status)")]
    [InlineData("t => t.Origin > Origin.Manual", "t.Origin > Origin.Manual")]
    [InlineData("t => Sql.ILike(t.Title, null)", "Sql.ILike(t.Title, null)")]
    [InlineData("t => t.Title.Contains(null!)", "t.Title.Contains(null!)")]
    [InlineData("t => t.Title.StartsWith(null!, StringComparison.Ordinal)", "t.Title.StartsWith(null!, StringComparison.Ordinal)")]
    [InlineData("t => t.Title.EndsWith(null!, StringComparison.Ordinal)", "t.Title.EndsWith(null!, StringComparison.Ordinal)")]
    [InlineData("t => t.Kind == (WorkKind)5", "(WorkKind)5")]
    [InlineData("t => string.Equals(null, null)", "string.Equals(null, null)")]
    public void Unsupported_predicate_reports_ZAO102(string predicate, string located)
    {
        var (result, tree) = GeneratorHarness.RunGeneratorOnFile(Prelude + Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskSummary>> Find(string title, WorkStatus[] kinds, CancellationToken ct);

                private static IQuery<TaskSummary> FindQuery(string title, WorkStatus[] kinds) =>
                    Sql.Table<TaskEntity>().Where(
            """ + predicate + """
            ).Select(t => new TaskSummary(t.Id, t.Title));
            }
            """, NpgsqlStub.Source);

        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Equal("ZAO102", diagnostic.Id);
        Assert.Same(tree, diagnostic.Location.SourceTree);
        var span = diagnostic.Location.SourceSpan;
        Assert.Equal(located, tree.ToString().Substring(span.Start, span.Length));
    }

    // The map stores Kind by name, so ordering it would order the names: reported when bound.
    [Fact]
    public void Ordering_an_EnumAsString_column_reports_ZAO102()
    {
        var (result, tree) = GeneratorHarness.RunGeneratorOnFile(Prelude + Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskSummary>> Find(CancellationToken ct);

                private static IQuery<TaskSummary> FindQuery() =>
                    Sql.Table<TaskEntity>().Where(t => t.Kind < WorkKind.Feature).Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);

        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Equal("ZAO102", diagnostic.Id);
        Assert.Same(tree, diagnostic.Location.SourceTree);
        var span = diagnostic.Location.SourceSpan;
        Assert.Equal("t.Kind", tree.ToString().Substring(span.Start, span.Length));
        Assert.Contains("stores enum member names", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    // An enum constant compared with a parameter that binds by name is written by name too, and
    // a parameter compared with a name-bound one binds by name as well.
    [Fact]
    public void Enum_literal_and_parameter_against_a_name_bound_parameter_bind_by_name()
    {
        VerifyCompiled(Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskSummary>> Find(WorkKind kind, WorkKind other, CancellationToken ct);

                private static IQuery<TaskSummary> FindQuery(WorkKind kind, WorkKind other) =>
                    Sql.Table<TaskEntity>()
                        .WhereIf(kind != WorkKind.Bug, t => t.Kind == kind)
                        .WhereIf(other == kind, t => t.IsActive)
                        .Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);
    }

    // A parameter without a DbType facet gets a cast in a null test, so a null is typed.
    // A name-bound byte enum gets the String facet when bound, and no cast; so does a short,
    // whose Int16 facet types it already. A char parameter never gets this far: the core
    // rejects it with ZAO041.
    [Fact]
    public void Null_test_on_a_parameter_without_a_facet_casts_it()
    {
        VerifyCompiled(
            Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskSummary>> Find(
                    Guid[]? ids, List<string>? titles, IReadOnlyList<int>? orders, long[]? sizes, float[]? weights, double[]? ratios,
                    DateTime? at, DateOnly? dueOn, TimeSpan? spent, Tier? tier, Level? level, TimeOnly? startsAt,
                    short? rank, CancellationToken ct);

                private static IQuery<TaskSummary> FindQuery(
                    Guid[]? ids, List<string>? titles, IReadOnlyList<int>? orders, long[]? sizes, float[]? weights, double[]? ratios,
                    DateTime? at, DateOnly? dueOn, TimeSpan? spent, Tier? tier, Level? level, TimeOnly? startsAt,
                    short? rank) =>
                    Sql.Table<TaskEntity>()
                        .WhereIf(ids != null, t => ids!.Contains(t.Id))
                        .WhereIf(titles != null, t => titles!.Contains(t.Title))
                        .WhereIf(orders != null, t => orders!.Contains(t.Order))
                        .WhereIf(sizes != null, t => sizes!.Contains(t.Size))
                        .WhereIf(weights != null, t => weights!.Contains(t.Weight))
                        .WhereIf(ratios != null, t => ratios!.Contains(t.Ratio))
                        .Where(t => at == null || t.LocalAt < at)
                        .Where(t => dueOn == null || t.DueOn == dueOn)
                        .Where(t => spent == null || t.Spent > spent)
                        .Where(t => tier == null || t.Tier == tier)
                        .Where(t => level == null || t.Level == level)
                        .Where(t => startsAt == null || t.StartsAt == startsAt)
                        .Where(t => rank == null || t.Rank == rank)
                        .Select(t => new TaskSummary(t.Id, t.Title));
            }
            """,
            NpgsqlStub.Source);
    }

    // A value with no member name cannot be compared by name: here the parameter binds by name
    // because it is compared with the EnumAsString column.
    [Fact]
    public void Nameless_enum_value_against_a_name_bound_parameter_reports_ZAO102()
    {
        var (result, tree) = GeneratorHarness.RunGeneratorOnFile(Prelude + Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskSummary>> Find(WorkKind kind, CancellationToken ct);

                private static IQuery<TaskSummary> FindQuery(WorkKind kind) =>
                    Sql.Table<TaskEntity>()
                        .WhereIf(kind != (WorkKind)5, t => t.Kind == kind)
                        .Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);

        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Equal("ZAO102", diagnostic.Id);
        var span = diagnostic.Location.SourceSpan;
        Assert.Equal("(WorkKind)5", tree.ToString().Substring(span.Start, span.Length));
        Assert.Contains("compared with a value stored by member name", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void Null_test_on_an_untyped_parameter_without_a_cast_reports_ZAO102()
    {
        var (result, tree) = GeneratorHarness.RunGeneratorOnFile(Prelude + Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskSummary>> Find(byte[]? blob, CancellationToken ct);

                private static IQuery<TaskSummary> FindQuery(byte[]? blob) =>
                    Sql.Table<TaskEntity>().WhereIf(blob != null, t => t.IsActive).Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);

        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Equal("ZAO102", diagnostic.Id);
        var span = diagnostic.Location.SourceSpan;
        Assert.Equal("blob != null", tree.ToString().Substring(span.Start, span.Length));
        Assert.Contains("'byte[]'", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    // string.Equals with a null constant is a null test, like `== null`.
    [Fact]
    public void String_Equals_null_is_a_null_test()
    {
        VerifyCompiled(Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskSummary>> Find(CancellationToken ct);

                private static IQuery<TaskSummary> FindQuery() =>
                    Sql.Table<TaskEntity>()
                        .Where(t => string.Equals(t.Note, null) || !string.Equals(null, t.Note, StringComparison.Ordinal))
                        .Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);
    }

    // Kind stores names and PreviousKind integers: comparing them would compare text with a number.
    [Fact]
    public void Comparing_enum_columns_stored_differently_reports_ZAO102()
    {
        var (result, tree) = GeneratorHarness.RunGeneratorOnFile(Prelude + Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskSummary>> Find(CancellationToken ct);

                private static IQuery<TaskSummary> FindQuery() =>
                    Sql.Table<TaskEntity>().Where(t => t.Kind == t.PreviousKind).Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);

        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Equal("ZAO102", diagnostic.Id);
        var span = diagnostic.Location.SourceSpan;
        Assert.Equal("t.Kind", tree.ToString().Substring(span.Start, span.Length));
        Assert.Contains("store the enum differently", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    // Two parameters compared with each other bind alike; decided differently, that is ZAO111.
    // The message names the C# parameter, not its [Param(Name)].
    [Fact]
    public void Enum_parameters_compared_with_each_other_and_stored_differently_report_ZAO111()
    {
        var (result, tree) = GeneratorHarness.RunGeneratorOnFile(Prelude + Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskSummary>> Find(WorkKind kind, [Param(Name = "@o")] WorkKind other, CancellationToken ct);

                private static IQuery<TaskSummary> FindQuery(WorkKind kind, WorkKind other) =>
                    Sql.Table<TaskEntity>()
                        .Where(t => t.Kind == kind && t.PreviousKind == other && kind == other)
                        .Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);

        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Equal("ZAO111", diagnostic.Id);
        Assert.Equal(
            "Parameter 'other' is compared with or set into columns that store its enum differently",
            diagnostic.GetMessage(CultureInfo.InvariantCulture));
        var span = diagnostic.Location.SourceSpan;
        Assert.Equal("other", tree.ToString().Substring(span.Start, span.Length));
        Assert.EndsWith("kind == ", tree.ToString().Substring(0, span.Start), StringComparison.Ordinal);
    }

    [Fact]
    public void Captured_value_in_a_predicate_reports_ZAO103()
    {
        var (result, tree) = GeneratorHarness.RunGeneratorOnFile(Prelude + Repo + """
                private static readonly int Threshold = 3;

                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskSummary>> Find(CancellationToken ct);

                private static IQuery<TaskSummary> FindQuery() =>
                    Sql.Table<TaskEntity>().Where(t => t.Order > Threshold).Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);

        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Equal("ZAO103", diagnostic.Id);
        var span = diagnostic.Location.SourceSpan;
        Assert.Equal("Threshold", tree.ToString().Substring(span.Start, span.Length));
    }

    // Runs the generator with step tracking and returns the bound [LinqQuery] method's model.
    private static ZeroAlloc.ORM.Generator.Model.QueryMethodModel BindOnly(string body)
    {
        var compilation = GeneratorHarness.CreateCompilation([CSharpSyntaxTree.ParseText(Prelude + body, path: "/src/Store.cs")]);
        var driver = CSharpGeneratorDriver.Create(
            new[] { new OrmGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        var run = driver.RunGenerators(compilation).GetRunResult().Results[0];
        Assert.Empty(run.Diagnostics);
        var output = Assert.Single(Assert.Single(run.TrackedSteps["LinqQueryMethods"]).Outputs);
        var result = Assert.IsType<LinqMethodResult>(output.Value);
        return result.Method!.Method;
    }

    private static void VerifyCompiled(
        string body,
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

        GeneratorSnapshot.Verify(result, testFilePath, testMethod);
    }
}
