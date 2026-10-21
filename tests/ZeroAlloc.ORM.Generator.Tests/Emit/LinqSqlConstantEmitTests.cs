using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.ORM.Generator.Model;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// [LinqQuery] foundation. No pipeline produces a model with LinqSourceComment or
// LINQ facets yet, so these tests drive the emitter and the facet merge directly
// with hand-built models. The end-to-end snapshots arrive with the translator.
public class LinqSqlConstantEmitTests
{
    private const string Sql = "SELECT count(*) FROM \"orders\" WHERE \"id\" = @id";

    [Fact]
    public void Linq_method_emits_the_sql_as_a_constant_under_its_source_comment()
    {
        var source = OrmGenerator.RenderRepository(Repository("orders.Where(o => o.Id == id).Count()"));

        var expected = """
                // orders.Where(o => o.Id == id).Count()
                private const string __sql0_CountAsync = "SELECT count(*) FROM \"orders\" WHERE \"id\" = @id";

                [global::System.CodeDom.Compiler.GeneratedCode(
            """.Replace("\r\n", "\n", System.StringComparison.Ordinal);
        Assert.Contains(expected, source.Replace("\r\n", "\n", System.StringComparison.Ordinal), System.StringComparison.Ordinal);
        Assert.Contains("__cmd.CommandText = __sql0_CountAsync;", source, System.StringComparison.Ordinal);
        Assert.DoesNotContain("CommandText = \"", source, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Line_breaks_in_the_source_comment_do_not_end_the_comment()
    {
        var source = OrmGenerator.RenderRepository(Repository("orders\r\n.Where(o => o.Id == id)\n.Count()"));

        Assert.Contains("    // orders .Where(o => o.Id == id) .Count()\n", source.Replace("\r\n", "\n", System.StringComparison.Ordinal), System.StringComparison.Ordinal);
    }

    [Fact]
    public void Method_without_source_comment_keeps_the_inline_literal()
    {
        var source = OrmGenerator.RenderRepository(Repository(comment: null));

        Assert.DoesNotContain("__sql", source, System.StringComparison.Ordinal);
        Assert.Contains("__cmd.CommandText = \"SELECT count(*)", source, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Linq_overloads_get_distinct_constants_and_compile()
    {
        var byId = Method("orders.Count(o => o.Id == id)", Sql,
            new ParameterInfo("id", "int", IsCancellationToken: false, IsTransaction: false));
        var byName = Method("orders.Count(o => o.Name == name)", "SELECT count(*) FROM \"orders\" WHERE \"name\" = @name",
            new ParameterInfo("name", "string", IsCancellationToken: false, IsTransaction: false));
        var source = OrmGenerator.RenderRepository(Repository(byId, byName));

        Assert.Contains("private const string __sql0_CountAsync = ", source, System.StringComparison.Ordinal);
        Assert.Contains("private const string __sql1_CountAsync = ", source, System.StringComparison.Ordinal);
        Assert.Contains("__cmd.CommandText = __sql0_CountAsync;", source, System.StringComparison.Ordinal);
        Assert.Contains("__cmd.CommandText = __sql1_CountAsync;", source, System.StringComparison.Ordinal);

        var declarations = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                public partial Task<int> CountAsync(int id, CancellationToken ct);
                public partial Task<int> CountAsync(string name, CancellationToken ct);
            }
            """;
        var compilation = GeneratorHarness.CreateCompilation(new[]
        {
            CSharpSyntaxTree.ParseText(declarations),
            CSharpSyntaxTree.ParseText(source),
        });
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToArray();
        Assert.Empty(errors);
    }

    [Fact]
    public void Linq_facet_fills_a_parameter_without_a_written_dbtype()
    {
        var parameters = ImmutableArray.Create(
            new ParameterInfo("id", "int", IsCancellationToken: false, IsTransaction: false),
            new ParameterInfo("name", "string", IsCancellationToken: false, IsTransaction: false,
                Facets: new ParamFacets(null, 40, null, null, null, null)));
        var facets = new EquatableArray<LinqParameterFacet>(ImmutableArray.Create(
            new LinqParameterFacet("id", "global::System.Data.DbType.Int32", 11),
            new LinqParameterFacet("name", "global::System.Data.DbType.String", 16)));

        var result = OrmGenerator.ApplyLinqFacets(parameters, facets);

        Assert.Equal(new ParamFacets("global::System.Data.DbType.Int32", null, null, null, null, 11), result[0].Facets);
        Assert.Equal(new ParamFacets("global::System.Data.DbType.String", 40, null, null, null, 16), result[1].Facets);
    }

    [Fact]
    public void Written_param_dbtype_wins_over_the_linq_facet()
    {
        var written = new ParamFacets("global::System.Data.DbType.AnsiString", null, null, null, null, 0);
        var parameters = ImmutableArray.Create(
            new ParameterInfo("name", "string", IsCancellationToken: false, IsTransaction: false, Facets: written),
            new ParameterInfo("ct", "global::System.Threading.CancellationToken", IsCancellationToken: true, IsTransaction: false));
        var facets = new EquatableArray<LinqParameterFacet>(ImmutableArray.Create(
            new LinqParameterFacet("name", "global::System.Data.DbType.String", 16)));

        var result = OrmGenerator.ApplyLinqFacets(parameters, facets);

        Assert.Same(written, result[0].Facets);
        Assert.Null(result[1].Facets);
    }

    private static QueryRepositoryModel Repository(string? comment)
        => Repository(Method(comment, Sql,
            new ParameterInfo("id", "int", IsCancellationToken: false, IsTransaction: false)));

    private static QueryMethodModel Method(string? comment, string sql, ParameterInfo valueParameter)
        => new(
            MethodName: "CountAsync",
            ContainingTypeFullName: "global::TestApp.Repo",
            Sql: sql,
            Shape: EmitShape.ScalarInt,
            Strategy: BatchEmitStrategy.SingleCommand,
            ReturnTypeDisplay: "global::System.Threading.Tasks.Task<int>",
            NullableScalarReaderMethod: null,
            Materialization: null,
            MultiResultMaterialization: null,
            MethodParameters: new EquatableArray<ParameterInfo>(ImmutableArray.Create(
                valueParameter,
                new ParameterInfo("ct", "global::System.Threading.CancellationToken", IsCancellationToken: true, IsTransaction: false))),
            CancellationTokenParameterName: "ct",
            TransactionParameterName: null,
            Diagnostics: default,
            IsCommand: false,
            CommandKind: CommandKindModel.NonQuery,
            HasReturnValue: true,
            IsStoredProcedure: false,
            ProcedureName: string.Empty,
            SprocOutputParamsMaterialization: null,
            MethodAccessibilityKeyword: "public",
            LinqSourceComment: comment);

    private static QueryRepositoryModel Repository(params QueryMethodModel[] methods)
        => new(
            ContainingTypeFullName: "global::TestApp.Repo",
            ContainingTypeName: "Repo",
            Namespace: "TestApp",
            ConnectionAccess: "connection",
            ConnectionResolved: true,
            ContainingTypePartial: true,
            ContainingTypeLocation: null,
            Methods: new EquatableArray<QueryMethodModel>(ImmutableArray.Create(methods)),
            ContainingTypeChain: default,
            HintName: "TestApp.Repo.g.cs");
}
