using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Diagnostics;

// ZAO082 fires when a parameter uses a type only Npgsql can bind, here a
// PostgreSQL array, and the compilation does not reference Npgsql.
public class ZAO082Tests
{
    private const string ArrayParameterSource = """
        using System.Data.Async;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.ORM;

        namespace TestApp;

        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Query("SELECT COUNT(*) FROM posts WHERE tags && @tags")]
            public partial Task<int> CountTaggedAsync(string[] tags, CancellationToken ct);
        }
        """;

    [Fact]
    public void Array_parameter_without_npgsql_reports_ZAO082()
    {
        var result = GeneratorHarness.RunGenerator(ArrayParameterSource);
        var diagnostics = result.Results[0].Diagnostics;

        var zao082 = Assert.Single(diagnostics, d => string.Equals(d.Id, "ZAO082", System.StringComparison.Ordinal));
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, zao082.Severity);
        Assert.Equal(
            "Parameter or column 'tags' uses a PostgreSQL array, which needs Npgsql. "
            + "Reference the Npgsql package, or bind it another way.",
            zao082.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        // The array is classified, so the generic no-binding-strategy error stays quiet.
        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO041", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Array_parameter_with_npgsql_reports_nothing()
    {
        var (result, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(ArrayParameterSource, NpgsqlStub.Source);

        Assert.Empty(result.Results[0].Diagnostics);
        Assert.Empty(compileDiagnostics);
    }

    private const string BulkInsertArrayPropertySource = """
        using System.Collections.Generic;
        using System.Data.Async;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.ORM;

        namespace TestApp;

        public sealed record PostRow(int Id, string[] Tags);

        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Command("INSERT INTO posts (Id, Tags) VALUES (@Id, @Tags)", Kind = CommandKind.BulkInsert)]
            public partial Task<int> InsertPostsAsync(IReadOnlyList<PostRow> posts, CancellationToken ct);
        }
        """;

    [Fact]
    public void Bulk_insert_row_array_property_without_npgsql_reports_ZAO082()
    {
        var result = GeneratorHarness.RunGenerator(BulkInsertArrayPropertySource);
        var diagnostics = result.Results[0].Diagnostics;

        var zao082 = Assert.Single(diagnostics, d => string.Equals(d.Id, "ZAO082", System.StringComparison.Ordinal));
        Assert.Equal(
            "Parameter or column 'Tags' uses a PostgreSQL array, which needs Npgsql. "
            + "Reference the Npgsql package, or bind it another way.",
            zao082.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Bulk_insert_row_array_property_with_npgsql_reports_nothing()
    {
        var result = GeneratorHarness.RunGenerator(BulkInsertArrayPropertySource, NpgsqlStub.Source);

        Assert.Empty(result.Results[0].Diagnostics);
    }

    [Fact]
    public void Bulk_insert_collection_keeps_its_meaning()
    {
        // The row collection and the identity list are collections too, but neither is a
        // PostgreSQL array: the bulk-insert shape check runs first and claims them.
        var source = """
            using System.Collections.Generic;
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderRow(int CustomerId, decimal Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO Orders (CustomerId, Total) VALUES (@CustomerId, @Total) RETURNING Id", Kind = CommandKind.BulkInsert)]
                public partial Task<IReadOnlyList<int>> InsertOrdersAsync(IReadOnlyList<OrderRow> orders, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);

        Assert.Empty(result.Results[0].Diagnostics);
    }

    private const string ColumnUsings = """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using System.Data.Async;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.ORM;

        namespace TestApp;

        """;

    private const string RowColumnSource = """
        public sealed record TaggedRow(Guid Id, string[] Tags);

        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Query("SELECT id, tags FROM posts WHERE id = @id")]
            public partial Task<TaggedRow?> GetAsync(Guid id, CancellationToken ct);
        }
        """;

    private const string ListRowColumnSource = """
        public sealed record TaggedRow(Guid Id, List<string> Tags);

        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Query("SELECT id, tags FROM posts")]
            public partial Task<IReadOnlyList<TaggedRow>> ListAsync(CancellationToken ct);
        }
        """;

    private const string StreamingColumnSource = """
        public sealed record TaggedRow(Guid Id, string[] Tags);

        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Query("SELECT id, tags FROM posts")]
            public partial IAsyncEnumerable<TaggedRow> StreamAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct);
        }
        """;

    private const string CompositeColumnSource = """
        public readonly record struct Labels(string Name, IReadOnlyList<string> Tags);
        public sealed record PostRow(Guid Id, Labels Labels);

        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Query("SELECT id, name, tags FROM posts WHERE id = @id")]
            public partial Task<PostRow?> GetAsync(Guid id, CancellationToken ct);
        }
        """;

    private const string TupleScalarSource = """
        public sealed record PostRow(Guid Id, string Title);

        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Query("SELECT id, title FROM posts WHERE id = @id; SELECT tags FROM drafts WHERE id = @id;")]
            public partial Task<(PostRow? Post, long[] DraftIds)> GetAsync(Guid id, CancellationToken ct);
        }
        """;

    private const string ScalarQuerySource = """
        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Query("SELECT tags FROM posts WHERE id = @id")]
            public partial Task<string[]> GetTagsAsync(Guid id, CancellationToken ct);
        }
        """;

    private const string NullableScalarQuerySource = """
        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Query("SELECT links FROM posts WHERE id = @id")]
            public partial Task<Guid[]?> GetLinksAsync(Guid id, CancellationToken ct);
        }
        """;

    private const string ScalarCommandSource = """
        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Command("SELECT scores FROM posts WHERE id = @id", Kind = CommandKind.Scalar)]
            public partial Task<int[]> GetScoresAsync(Guid id, CancellationToken ct);
        }
        """;

    private const string CompositeParameterMemberSource = """
        public readonly record struct TagFilter(string Name, string[] Tags);

        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Query("SELECT COUNT(*) FROM posts WHERE name = @filter_Name AND tags && @filter_Tags")]
            public partial Task<int> CountAsync(TagFilter filter, CancellationToken ct);
        }
        """;

    // Each source reads or binds one PostgreSQL array that is not a method parameter,
    // with the name ZAO082 reports for it.
    public static TheoryData<string, string> ArrayColumnSources => new()
    {
        { RowColumnSource, "Tags" },
        { ListRowColumnSource, "Tags" },
        { StreamingColumnSource, "Tags" },
        { CompositeColumnSource, "Tags" },
        { TupleScalarSource, "DraftIds" },
        { ScalarQuerySource, "GetTagsAsync" },
        { NullableScalarQuerySource, "GetLinksAsync" },
        { ScalarCommandSource, "GetScoresAsync" },
        { CompositeParameterMemberSource, "filter_Tags" },
    };

    [Theory]
    [MemberData(nameof(ArrayColumnSources))]
    public void Array_column_without_npgsql_reports_ZAO082(string body, string name)
    {
        var result = GeneratorHarness.RunGenerator(ColumnUsings + body);
        var diagnostics = result.Results[0].Diagnostics;

        var zao082 = Assert.Single(diagnostics);
        Assert.Equal("ZAO082", zao082.Id);
        Assert.Equal(
            $"Parameter or column '{name}' uses a PostgreSQL array, which needs Npgsql. "
            + "Reference the Npgsql package, or bind it another way.",
            zao082.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [MemberData(nameof(ArrayColumnSources))]
    public void Array_column_with_npgsql_reports_nothing(string body, string name)
    {
        _ = name;
        var (result, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(ColumnUsings + body, NpgsqlStub.Source);

        Assert.Empty(result.Results[0].Diagnostics);
        Assert.Empty(compileDiagnostics);
    }

    // `Task<List<string>>` is a list of rows, the existing list-result shape, not one
    // array column. A string is no row, so it stays unsupported the way it was, with
    // or without Npgsql, and is never reported as an array.
    [Fact]
    public void List_of_string_return_is_a_list_of_rows_not_an_array_column()
    {
        var source = ColumnUsings + """
            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT tag FROM tags")]
                public partial Task<List<string>> ListTagsAsync(CancellationToken ct);
            }
            """;

        var without = GeneratorHarness.RunGenerator(source).Results[0].Diagnostics;
        var with = GeneratorHarness.RunGenerator(source, NpgsqlStub.Source).Results[0].Diagnostics;

        Assert.Equal("ZAO022", Assert.Single(without).Id);
        Assert.Equal("ZAO022", Assert.Single(with).Id);
    }
}
