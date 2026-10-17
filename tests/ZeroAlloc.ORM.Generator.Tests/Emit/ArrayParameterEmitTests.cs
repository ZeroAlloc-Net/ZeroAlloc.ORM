using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Xunit;

using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// PostgreSQL arrays: `T[]`, `List<T>` and `IReadOnlyList<T>` of string, int, long or
// Guid, bound as one parameter and read as one column. Every case runs with the
// Npgsql stub, since without Npgsql ZAO082 rejects them. Each snapshot is also
// compiled: the emitted code must build with no error and no warning.
public class ArrayParameterEmitTests
{
    private const string Usings = """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using System.Data.Async;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.ORM;

        namespace TestApp;

        """;

    [Fact]
    public void String_array_parameter_binds_value()
    {
        VerifyCompiled(Usings + """
            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT COUNT(*) FROM posts WHERE tags && @tags")]
                public partial Task<int> CountTaggedAsync(string[] tags, CancellationToken ct);
            }
            """);
    }

    [Fact]
    public void List_of_guid_parameter_binds_value()
    {
        VerifyCompiled(Usings + """
            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT COUNT(*) FROM posts WHERE id = ANY(@ids)")]
                public partial Task<int> CountAsync(List<Guid> ids, CancellationToken ct);
            }
            """);
    }

    [Fact]
    public void ReadOnlyList_parameter_binds_with_fallback_copy()
    {
        VerifyCompiled(Usings + """
            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT COUNT(*) FROM posts WHERE tags && @tags")]
                public partial Task<int> CountTaggedAsync(IReadOnlyList<string> tags, CancellationToken ct);
            }
            """);
    }

    [Fact]
    public void Nullable_array_parameters_bind_DBNull()
    {
        VerifyCompiled(Usings + """
            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("UPDATE posts SET tags = @tags, scores = @scores, ids = @ids WHERE id = @id")]
                public partial Task<int> UpdateAsync(Guid id, string[]? tags, List<int>? scores, IReadOnlyList<long>? ids, CancellationToken ct);
            }
            """);
    }

    // Nullable string elements, as written, compile without a nullable warning.
    [Fact]
    public void Nullable_element_arrays_bind_and_read()
    {
        VerifyCompiled(Usings + """
            public sealed record LabelRow(Guid Id, string?[] Labels, List<string?> Notes, IReadOnlyList<string?>? Extra);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("UPDATE posts SET labels = @labels, notes = @notes WHERE id = @id")]
                public partial Task<int> UpdateAsync(Guid id, IReadOnlyList<string?> labels, List<string?> notes, CancellationToken ct);

                [Query("SELECT id, labels, notes, extra FROM posts WHERE id = @id")]
                public partial Task<LabelRow?> GetAsync(Guid id, CancellationToken ct);
            }
            """);
    }

    // EmitScalarMaterialization binds through EmitParameterBindingWithIndent.
    [Fact]
    public void ReadOnlyList_parameter_in_scalar_command_binds_with_fallback_copy()
    {
        VerifyCompiled(Usings + """
            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("SELECT COUNT(*) FROM posts WHERE id = ANY(@ids)", Kind = CommandKind.Scalar)]
                public partial Task<long> CountAsync(IReadOnlyList<Guid> ids, CancellationToken ct);
            }
            """);
    }

    // BatchWithFallback binds through both indented variants: the batch commands and
    // the joined single command.
    [Fact]
    public void ReadOnlyList_parameter_in_batch_with_fallback_binds_with_fallback_copy()
    {
        VerifyCompiled(Usings + """
            public sealed record PostRow(Guid Id, string Title);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, Title FROM posts WHERE tags && @tags; SELECT COUNT(*) FROM posts WHERE tags && @tags;")]
                public partial Task<(List<PostRow> Posts, int Total)> SearchAsync(IReadOnlyList<string> tags, CancellationToken ct);
            }
            """);
    }

    // BatchMode.Always binds through EmitBatchCommandParameterBinding.
    [Fact]
    public void ReadOnlyList_parameter_in_batch_always_binds_with_fallback_copy()
    {
        VerifyCompiled(Usings + """
            public sealed record PostRow(Guid Id, string Title);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, Title FROM posts WHERE tags && @tags; SELECT COUNT(*) FROM posts WHERE tags && @tags;", Batch = BatchMode.Always)]
                public partial Task<(List<PostRow> Posts, int Total)> SearchAsync(IReadOnlyList<string> tags, CancellationToken ct);
            }
            """);
    }

    [Fact]
    public void Composite_parameter_with_array_member_binds_value()
    {
        VerifyCompiled(Usings + """
            public readonly record struct TagFilter(string Name, IReadOnlyList<string> Tags);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT COUNT(*) FROM posts WHERE name = @filter_Name AND tags && @filter_Tags")]
                public partial Task<int> CountAsync(TagFilter filter, CancellationToken ct);
            }
            """);
    }

    [Fact]
    public void Bulk_insert_row_with_array_property_binds_value()
    {
        VerifyCompiled(Usings + """
            public sealed record PostRow(Guid Id, string[] Tags, IReadOnlyList<int> Scores);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO posts (id, tags, scores) VALUES (@Id, @Tags, @Scores)", Kind = CommandKind.BulkInsert)]
                public partial Task<int> InsertAsync(IReadOnlyList<PostRow> posts, CancellationToken ct);
            }
            """);
    }

    [Fact]
    public void Row_with_string_array_column_reads_GetFieldValue()
    {
        VerifyCompiled(Usings + """
            public sealed record TaggedRow(Guid Id, string[] Tags, IReadOnlyList<int> Scores);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT id, tags, scores FROM posts WHERE id = @id")]
                public partial Task<TaggedRow?> GetAsync(Guid id, CancellationToken ct);
            }
            """);
    }

    [Fact]
    public void Row_with_list_column_reads_into_new_list()
    {
        VerifyCompiled(Usings + """
            public sealed record TaggedListRow(Guid Id, List<string> Tags, List<Guid> Links);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT id, tags, links FROM posts WHERE id = @id")]
                public partial Task<TaggedListRow?> GetAsync(Guid id, CancellationToken ct);
            }
            """);
    }

    [Fact]
    public void Nullable_array_column_guards_IsDBNull()
    {
        VerifyCompiled(Usings + """
            public sealed record MaybeTaggedRow(Guid Id, string[]? Tags, List<long>? Ids, IReadOnlyList<int>? Scores);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT id, tags, ids, scores FROM posts WHERE id = @id")]
                public partial Task<MaybeTaggedRow?> GetAsync(Guid id, CancellationToken ct);
            }
            """);
    }

    [Fact]
    public void Domain_entity_with_array_column_reads_by_name()
    {
        VerifyCompiled(Usings + """
            public sealed class Post
            {
                public Post(Guid id, List<string> tags) { Id = id; Tags = tags; }
                public Guid Id { get; }
                public List<string> Tags { get; }
            }

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, Tags FROM posts WHERE id = @id")]
                public partial Task<Post?> GetAsync(Guid id, CancellationToken ct);
            }
            """);
    }

    [Fact]
    public void List_of_rows_with_array_column_reads_each_row()
    {
        VerifyCompiled(Usings + """
            public sealed record TaggedRow(Guid Id, string[] Tags);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT id, tags FROM posts")]
                public partial Task<List<TaggedRow>> ListAsync(CancellationToken ct);
            }
            """);
    }

    [Fact]
    public void Composite_column_with_array_member_reads_GetFieldValue()
    {
        VerifyCompiled(Usings + """
            public readonly record struct Labels(string Name, List<string> Tags);
            public sealed record PostRow(Guid Id, Labels Labels);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT id, name, tags FROM posts WHERE id = @id")]
                public partial Task<PostRow?> GetAsync(Guid id, CancellationToken ct);
            }
            """);
    }

    [Fact]
    public void Multi_result_with_array_row_and_scalar_reads_GetFieldValue()
    {
        VerifyCompiled(Usings + """
            public sealed record TaggedRow(Guid Id, string[] Tags);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT id, tags FROM posts WHERE id = @id; SELECT tags FROM drafts WHERE id = @id; SELECT links FROM posts WHERE id = @id;")]
                public partial Task<(TaggedRow? Post, string[] DraftTags, Guid[]? Links)> GetAsync(Guid id, CancellationToken ct);
            }
            """);
    }

    [Fact]
    public void Scalar_query_returning_array_reads_GetFieldValue()
    {
        VerifyCompiled(Usings + """
            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT tags FROM posts WHERE id = @id")]
                public partial Task<string[]> GetTagsAsync(Guid id, CancellationToken ct);
            }
            """);
    }

    [Fact]
    public void Nullable_scalar_query_returning_array_guards_IsDBNull()
    {
        VerifyCompiled(Usings + """
            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT links FROM posts WHERE id = @id")]
                public partial Task<Guid[]?> GetLinksAsync(Guid id, CancellationToken ct);
            }
            """);
    }

    [Fact]
    public void Scalar_command_returning_array_casts_the_result()
    {
        VerifyCompiled(Usings + """
            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("UPDATE posts SET tags = array_append(tags, @tag) WHERE id = @id RETURNING tags", Kind = CommandKind.Scalar)]
                public partial Task<string[]> AddTagAsync(Guid id, string tag, CancellationToken ct);

                [Command("SELECT scores FROM posts WHERE id = @id", Kind = CommandKind.Scalar)]
                public partial Task<int[]?> GetScoresAsync(Guid id, CancellationToken ct);
            }
            """);
    }

    // A List<T> or IReadOnlyList<T> return reads as rows, so a scalar command keeps
    // rejecting it, with Npgsql too. Only `T[]` is an array at scalar position.
    [Fact]
    public void Scalar_command_returning_list_stays_unsupported()
    {
        var result = GeneratorHarness.RunGenerator(Usings + """
            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("SELECT scores FROM posts WHERE id = @id", Kind = CommandKind.Scalar)]
                public partial Task<List<int>> GetScoresAsync(Guid id, CancellationToken ct);
            }
            """, NpgsqlStub.Source);

        Assert.Equal("ZAO002", Assert.Single(result.Results[0].Diagnostics).Id);
    }

    // The generator reports nothing, the emitted code compiles without an error or a
    // warning, and the snapshot matches.
    private static void VerifyCompiled(
        string source,
        [CallerFilePath] string testFilePath = "",
        [CallerMemberName] string testMethod = "")
    {
        var (result, compilation) = GeneratorHarness.RunGeneratorAndGetCompilation(source, NpgsqlStub.Source);

        Assert.Empty(result.Results[0].Diagnostics);
        var generatedPaths = result.GeneratedTrees.Select(t => t.FilePath).ToHashSet(System.StringComparer.Ordinal);
        var emittedProblems = compilation.GetDiagnostics()
            .Where(d => d.Severity >= DiagnosticSeverity.Warning
                && d.Location.SourceTree is { } tree
                && generatedPaths.Contains(tree.FilePath))
            .Select(d => d.Id + ": " + d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))
            .ToList();
        Assert.Empty(emittedProblems);

        GeneratorSnapshot.Verify(result, testFilePath, testMethod);
    }
}
