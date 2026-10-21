using System.Collections.Immutable;
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
                private const string __sql_CountAsync = "SELECT count(*) FROM \"orders\" WHERE \"id\" = @id";

                [global::System.CodeDom.Compiler.GeneratedCode(
            """.Replace("\r\n", "\n", System.StringComparison.Ordinal);
        Assert.Contains(expected, source.Replace("\r\n", "\n", System.StringComparison.Ordinal), System.StringComparison.Ordinal);
        Assert.Contains("__cmd.CommandText = __sql_CountAsync;", source, System.StringComparison.Ordinal);
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

        Assert.DoesNotContain("__sql_", source, System.StringComparison.Ordinal);
        Assert.Contains("__cmd.CommandText = \"SELECT count(*)", source, System.StringComparison.Ordinal);
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
    {
        var method = new QueryMethodModel(
            MethodName: "CountAsync",
            ContainingTypeFullName: "global::TestApp.Repo",
            Sql: Sql,
            Shape: EmitShape.ScalarInt,
            Strategy: BatchEmitStrategy.SingleCommand,
            ReturnTypeDisplay: "global::System.Threading.Tasks.Task<int>",
            NullableScalarReaderMethod: null,
            Materialization: null,
            MultiResultMaterialization: null,
            MethodParameters: new EquatableArray<ParameterInfo>(ImmutableArray.Create(
                new ParameterInfo("id", "int", IsCancellationToken: false, IsTransaction: false),
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
        return new QueryRepositoryModel(
            ContainingTypeFullName: "global::TestApp.Repo",
            ContainingTypeName: "Repo",
            Namespace: "TestApp",
            ConnectionAccess: "connection",
            ConnectionResolved: true,
            ContainingTypePartial: true,
            ContainingTypeLocation: null,
            Methods: new EquatableArray<QueryMethodModel>(ImmutableArray.Create(method)),
            ContainingTypeChain: default,
            HintName: "TestApp.Repo.g.cs");
    }
}
