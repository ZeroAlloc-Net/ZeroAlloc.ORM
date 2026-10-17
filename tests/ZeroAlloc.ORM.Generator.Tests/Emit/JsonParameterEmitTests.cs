using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Xunit;

using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// `[Json]` marks a string as a PostgreSQL jsonb document. Every binding site sets
// NpgsqlDbType.Jsonb right after `.Value`, so the SQL needs no `::jsonb` cast. Reads
// are unchanged: a `[Json]` column reads with the string reader. Every case runs with
// the Npgsql stub, since without Npgsql ZAO082 rejects `[Json]`, and each snapshot is
// compiled: the emitted code must build with no error and no warning.
public class JsonParameterEmitTests
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
    public void Json_parameter_sets_NpgsqlDbType_Jsonb()
    {
        VerifyCompiled(Usings + """
            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("UPDATE posts SET doc = @doc, meta = @meta, title = @title WHERE id = @id")]
                public partial Task<int> UpdateAsync(Guid id, [Json] string doc, [Json] string? meta, string title, CancellationToken ct);
            }
            """);
    }

    // BatchMode.Always binds through EmitBatchCommandParameterBinding, and the joined
    // fallback through EmitParameterBindingWithIndent.
    [Fact]
    public void Json_parameter_in_batch_sets_NpgsqlDbType_Jsonb()
    {
        VerifyCompiled(Usings + """
            public sealed record PostRow(Guid Id, string Title);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, Title FROM posts WHERE doc @> @filter; SELECT COUNT(*) FROM posts WHERE doc @> @filter;")]
                public partial Task<(List<PostRow> Posts, int Total)> SearchAsync([Json] string filter, CancellationToken ct);

                [Query("SELECT Id, Title FROM posts WHERE doc @> @filter; SELECT COUNT(*) FROM posts WHERE doc @> @filter;", Batch = BatchMode.Always)]
                public partial Task<(List<PostRow> Posts, int Total)> SearchBatchedAsync([Json] string filter, CancellationToken ct);
            }
            """);
    }

    // A ctor parameter of a composite carries the marker. The nullable composite sets
    // Jsonb in both branches, its DBNull one included, the way a null string does.
    [Fact]
    public void Json_composite_member_sets_NpgsqlDbType_Jsonb()
    {
        VerifyCompiled(Usings + """
            public readonly record struct Payload(string Name, [Json] string Doc);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO docs (name, doc) VALUES (@payload_Name, @payload_Doc)")]
                public partial Task<int> InsertAsync(Payload payload, CancellationToken ct);

                [Command("INSERT INTO docs (name, doc) VALUES (@payload_Name, @payload_Doc)")]
                public partial Task<int> InsertMaybeAsync(Payload? payload, CancellationToken ct);
            }
            """,
            expectedDiagnostic: "ZAO050");
    }

    [Fact]
    public void Json_bulk_insert_row_property_sets_NpgsqlDbType_Jsonb()
    {
        VerifyCompiled(Usings + """
            public sealed record DocRow(Guid Id, [Json] string Doc, [Json] string? Meta);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO docs (id, doc, meta) VALUES (@Id, @Doc, @Meta)", Kind = CommandKind.BulkInsert)]
                public partial Task<int> InsertAsync(IReadOnlyList<DocRow> rows, CancellationToken ct);
            }
            """);
    }

    // `[property: Json]` marks the synthesized property, for a bulk-insert row and a
    // composite alike.
    [Fact]
    public void Json_property_target_on_record_parameter_sets_NpgsqlDbType_Jsonb()
    {
        VerifyCompiled(Usings + """
            public sealed record DocRow(Guid Id, [property: Json] string Doc);
            public readonly record struct Payload(string Name, [property: Json] string Doc);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO docs (id, doc) VALUES (@Id, @Doc)", Kind = CommandKind.BulkInsert)]
                public partial Task<int> InsertRowsAsync(IReadOnlyList<DocRow> rows, CancellationToken ct);

                [Command("INSERT INTO docs (name, doc) VALUES (@payload_Name, @payload_Doc)")]
                public partial Task<int> InsertAsync(Payload payload, CancellationToken ct);
            }
            """);
    }

    // A hand-written row class: the ctor parameter `doc` feeds property `Doc`, matched
    // case-insensitively through the one public ctor the row materializes with.
    [Fact]
    public void Json_class_ctor_parameter_marks_its_property()
    {
        VerifyCompiled(Usings + """
            public sealed class DocRow
            {
                public DocRow(Guid id, [Json] string doc) { Id = id; Doc = doc; }
                public Guid Id { get; }
                public string Doc { get; }
            }

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO docs (id, doc) VALUES (@Id, @Doc)", Kind = CommandKind.BulkInsert)]
                public partial Task<int> InsertAsync(IReadOnlyList<DocRow> rows, CancellationToken ct);
            }
            """);
    }

    // Only the materialization ctor counts: a marker on a private ctor's parameter of
    // the same name does not mark the property.
    [Fact]
    public void Json_on_other_ctor_parameter_does_not_mark_the_property()
    {
        var text = GeneratedText(Usings + """
            public sealed class DocRow
            {
                public DocRow(Guid id, string doc) { Id = id; Doc = doc; }
                private DocRow([Json] string doc) { Doc = doc; }
                public Guid Id { get; }
                public string Doc { get; }
            }

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO docs (id, doc) VALUES (@Id, @Doc)", Kind = CommandKind.BulkInsert)]
                public partial Task<int> InsertAsync(IReadOnlyList<DocRow> rows, CancellationToken ct);
            }
            """, NpgsqlStub.Source);

        Assert.DoesNotContain("Npgsql", text, System.StringComparison.Ordinal);
    }

    // An InputOutput parameter sends its initial value, so it gets Jsonb; an
    // Output-only one carries no value and gets none.
    [Fact]
    public void Json_inout_sproc_parameter_sets_NpgsqlDbType_Jsonb()
    {
        VerifyCompiled(Usings + """
            using System.Data;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [StoredProcedure("usp_MergeDoc")]
                public partial Task<(string Doc, string Log)> MergeAsync(
                    [Param(Direction = ParameterDirection.InputOutput)][Json] string doc,
                    [Json] string log,
                    CancellationToken ct);
            }
            """);
    }

    private const string JsonColumnSource = """
        public sealed record DocRow(Guid Id, [Json] string Doc);

        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Query("SELECT id, doc FROM docs WHERE id = @id")]
            public partial Task<DocRow?> GetAsync(Guid id, CancellationToken ct);
        }
        """;

    [Fact]
    public void Json_column_reads_as_string()
    {
        var marked = GeneratedText(Usings + JsonColumnSource, NpgsqlStub.Source);
        var unmarked = GeneratedText(Usings + JsonColumnSource.Replace("[Json] ", "", System.StringComparison.Ordinal), NpgsqlStub.Source);

        Assert.Equal(unmarked, marked);
        VerifyCompiled(Usings + JsonColumnSource);
    }

    // An unmarked string binds exactly as before, with or without Npgsql: no
    // NpgsqlDbType line, no Npgsql type named.
    [Fact]
    public void Unmarked_string_unchanged()
    {
        var source = Usings + """
            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("UPDATE posts SET doc = @doc, meta = @meta WHERE id = @id")]
                public partial Task<int> UpdateAsync(Guid id, string doc, string? meta, CancellationToken ct);
            }
            """;

        var withNpgsql = GeneratedText(source, NpgsqlStub.Source);
        var withoutNpgsql = GeneratedText(source);

        Assert.Equal(withoutNpgsql, withNpgsql);
        Assert.DoesNotContain("Npgsql", withNpgsql, System.StringComparison.Ordinal);
        Assert.Contains("__p_doc.Value = @doc;", withNpgsql, System.StringComparison.Ordinal);
    }

    private static string GeneratedText(string source, params string[] additionalSources)
    {
        var result = GeneratorHarness.RunGenerator(source, additionalSources);
        Assert.Empty(result.Results[0].Diagnostics);
        return Assert.Single(result.GeneratedTrees).ToString();
    }

    private static void VerifyCompiled(
        string source,
        string? expectedDiagnostic = null,
        [CallerFilePath] string testFilePath = "",
        [CallerMemberName] string testMethod = "")
    {
        var (result, compilation) = GeneratorHarness.RunGeneratorAndGetCompilation(source, NpgsqlStub.Source);

        if (expectedDiagnostic is null)
            Assert.Empty(result.Results[0].Diagnostics);
        else
            Assert.Equal(expectedDiagnostic, Assert.Single(result.Results[0].Diagnostics).Id);
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
