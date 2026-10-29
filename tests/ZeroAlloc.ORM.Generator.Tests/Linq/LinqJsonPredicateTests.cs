using System;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Xunit;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Generator.Tests.Linq;

// A string parameter compared with a jsonb column in a condition binds as jsonb, as the [Query]
// path binds a [Json] parameter. Left as text, PostgreSQL rejects `jsonb = text` at run time,
// 42883. The column is jsonb by the map's Json = true or by [Json] on its property or on the
// record constructor parameter that feeds it. A parameter that would also meet a text column,
// or be used in a form that is not a comparison, cannot bind both ways and is ZAO102.
public class LinqJsonPredicateTests
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

        public sealed class NoteEntity
        {
            public Guid Id { get; private set; }
            public string? Body { get; private set; }
            public string? Title { get; private set; }
            [Json]
            public string? Extra { get; private set; }
        }

        public sealed record DocEntity(Guid Id, [Json] string? Body, string? Summary);

        public sealed record NoteLine(Guid Id, string? Title);

        public sealed class AnalysisOutcome
        {
            public string? PullRequestUrl { get; private set; }
            [Json]
            public string? ValidationResult { get; private set; }
        }

        public sealed class CodeAnalysisRequest
        {
            public Guid Id { get; private set; }
            public AnalysisOutcome? Outcome { get; private set; }
        }

        [TableMap(typeof(NoteEntity), "Notes")]
        [ColumnMap(nameof(NoteEntity.Body), Json = true)]
        internal static class NoteMap;

        [TableMap(typeof(DocEntity), "Docs")]
        internal static class DocMap;

        [TableMap(typeof(CodeAnalysisRequest), "AnalysisRequests")]
        [OwnedMap(nameof(CodeAnalysisRequest.Outcome))]
        [ColumnMap("Outcome.ValidationResult", Json = true)]
        internal static class CodeAnalysisRequestMap;

        [Dialect(SqlDialect.PostgreSql)]
        public sealed partial class NoteStore(IAsyncDbConnection connection)
        {

        """;

    private const string JsonbBinding = "__np_doc.NpgsqlDbType = global::NpgsqlTypes.NpgsqlDbType.Jsonb;";

    [Fact]
    public void Query_column_from_the_map_compared_with_a_parameter_binds_jsonb()
    {
        VerifyCompiled("""
            [LinqQuery(nameof(ByBodyQuery))]
            public partial Task<List<NoteLine>> ByBody(string doc, CancellationToken ct);

            private static IQuery<NoteLine> ByBodyQuery(string doc) =>
                Sql.Table<NoteEntity>().Where(n => n.Body == doc).Select(n => new NoteLine(n.Id, n.Title));
            }
            """,
            "WHERE \"Body\" = @doc",
            JsonbBinding);
    }

    [Fact]
    public void Query_column_marked_json_on_its_property_compared_with_a_parameter_binds_jsonb()
    {
        VerifyCompiled("""
            [LinqQuery(nameof(ByExtraQuery))]
            public partial Task<List<NoteLine>> ByExtra(string doc, CancellationToken ct);

            private static IQuery<NoteLine> ByExtraQuery(string doc) =>
                Sql.Table<NoteEntity>().Where(n => doc != n.Extra).Select(n => new NoteLine(n.Id, n.Title));
            }
            """,
            "(@doc <> \"Extra\" OR \"Extra\" IS NULL)",
            JsonbBinding);
    }

    [Fact]
    public void Query_record_constructor_parameter_marked_json_binds_jsonb()
    {
        VerifyCompiled("""
            [LinqQuery(nameof(ByBodyQuery))]
            public partial Task<List<NoteLine>> ByBody(string doc, CancellationToken ct);

            private static IQuery<NoteLine> ByBodyQuery(string doc) =>
                Sql.Table<DocEntity>().Where(d => d.Body == doc).Select(d => new NoteLine(d.Id, d.Summary));
            }
            """,
            "WHERE \"Body\" = @doc",
            JsonbBinding);
    }

    // The case of the issue: an owned member whose column the map declares jsonb.
    [Fact]
    public void Query_owned_member_json_column_compared_with_a_parameter_binds_jsonb()
    {
        VerifyCompiled("""
            [LinqQuery(nameof(ByResultQuery))]
            public partial Task<List<NoteLine>> ByResult(string doc, CancellationToken ct);

            private static IQuery<NoteLine> ByResultQuery(string doc) =>
                Sql.Table<CodeAnalysisRequest>().Where(r => r.Outcome!.ValidationResult == doc).Select(r => new NoteLine(r.Id, null));
            }
            """,
            "\"Outcome_ValidationResult\" = @doc",
            JsonbBinding);
    }

    [Fact]
    public void Update_condition_on_a_json_column_binds_jsonb()
    {
        VerifyCompiled("""
            [LinqCommand(nameof(RenameQuery))]
            public partial Task<int> Rename(string doc, string title, CancellationToken ct);

            private static int RenameQuery(string doc, string title) =>
                Sql.Table<NoteEntity>().Where(n => n.Body == doc).ExecuteUpdate(set => set.SetProperty(n => n.Title, title));
            }
            """,
            "UPDATE \"Notes\" SET \"Title\" = @title WHERE \"Body\" = @doc",
            JsonbBinding);
    }

    [Fact]
    public void Delete_condition_on_a_json_column_binds_jsonb()
    {
        VerifyCompiled("""
            [LinqCommand(nameof(RemoveQuery))]
            public partial Task<int> Remove(string doc, CancellationToken ct);

            private static int RemoveQuery(string doc) =>
                Sql.Table<NoteEntity>().Where(n => n.Body == doc).ExecuteDelete();
            }
            """,
            "DELETE FROM \"Notes\" WHERE \"Body\" = @doc",
            JsonbBinding);
    }

    // A nullable parameter takes C#'s null semantics, which test the parameter for NULL as well.
    // That test sits beside the comparison and is no second use that forbids the jsonb binding.
    [Fact]
    public void Nullable_parameter_compared_with_a_json_column_binds_jsonb()
    {
        VerifyCompiled("""
            [LinqQuery(nameof(ByBodyQuery))]
            public partial Task<List<NoteLine>> ByBody(string? doc, CancellationToken ct);

            private static IQuery<NoteLine> ByBodyQuery(string? doc) =>
                Sql.Table<NoteEntity>().Where(n => n.Body == doc).Select(n => new NoteLine(n.Id, n.Title));
            }
            """,
            "@doc",
            JsonbBinding);
    }

    // A column that is not jsonb keeps its parameter a plain string.
    [Fact]
    public void Text_column_comparison_does_not_bind_jsonb()
    {
        var (result, _) = GeneratorHarness.RunGeneratorAndGetCompilation(Prelude + """
            [LinqQuery(nameof(ByTitleQuery))]
            public partial Task<List<NoteLine>> ByTitle(string title, CancellationToken ct);

            private static IQuery<NoteLine> ByTitleQuery(string title) =>
                Sql.Table<NoteEntity>().Where(n => n.Title == title).Select(n => new NoteLine(n.Id, n.Title));
            }
            """, NpgsqlStub.Source);

        Assert.Empty(result.Results[0].Diagnostics);
        Assert.DoesNotContain("Jsonb", Generated(result), StringComparison.Ordinal);
    }

    // Without Npgsql the jsonb binding has no type to name: ZAO082, as for a [Json] parameter.
    [Fact]
    public void Json_comparison_without_npgsql_reports_ZAO082()
    {
        var (result, _) = GeneratorHarness.RunGeneratorAndGetCompilation(Prelude + """
            [LinqQuery(nameof(ByBodyQuery))]
            public partial Task<List<NoteLine>> ByBody(string doc, CancellationToken ct);

            private static IQuery<NoteLine> ByBodyQuery(string doc) =>
                Sql.Table<NoteEntity>().Where(n => n.Body == doc).Select(n => new NoteLine(n.Id, n.Title));
            }
            """);

        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Equal("ZAO082", diagnostic.Id);
        Assert.Empty(result.GeneratedTrees);
    }

    // One parameter that meets a jsonb column and a text column cannot bind as both.
    [Fact]
    public void Parameter_compared_with_json_and_text_columns_reports_ZAO102()
    {
        var (result, _) = GeneratorHarness.RunGeneratorAndGetCompilation(Prelude + """
            [LinqQuery(nameof(EitherQuery))]
            public partial Task<List<NoteLine>> Either(string doc, CancellationToken ct);

            private static IQuery<NoteLine> EitherQuery(string doc) =>
                Sql.Table<NoteEntity>().Where(n => n.Body == doc || n.Title == doc).Select(n => new NoteLine(n.Id, n.Title));
            }
            """, NpgsqlStub.Source);

        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Equal("ZAO102", diagnostic.Id);
        Assert.Contains("doc", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Empty(result.GeneratedTrees);
    }

    private static string Generated(GeneratorDriverRunResult result)
        => string.Concat(result.GeneratedTrees.Select(t => t.ToString())).Replace("\\\"", "\"", StringComparison.Ordinal);

    private static void VerifyCompiled(
        string body,
        string expectedSql,
        string expectedBinding,
        [CallerFilePath] string testFilePath = "",
        [CallerMemberName] string testMethod = "")
    {
        var (result, compilation) = GeneratorHarness.RunGeneratorAndGetCompilation(Prelude + body, NpgsqlStub.Source);

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

        var generated = Generated(result);
        Assert.Contains(expectedSql, generated, StringComparison.Ordinal);
        Assert.Contains(expectedBinding, generated, StringComparison.Ordinal);

        GeneratorSnapshot.Verify(result, testFilePath, testMethod);
    }
}
