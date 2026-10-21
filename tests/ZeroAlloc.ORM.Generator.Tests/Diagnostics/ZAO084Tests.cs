using System;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Diagnostics;

// #376 — ZAO084 reports a PostgreSQL array or [Json] string on a repository whose declared
// dialect is not PostgreSql, whether or not Npgsql is referenced. No declared dialect, no report.
public class ZAO084Tests
{
    private const string ArrayMethod = """
            [Query("SELECT COUNT(*) FROM posts WHERE tags && @tags")]
            public partial Task<int> CountTaggedAsync(string[] tags, CancellationToken ct);
        """;

    private const string JsonMethod = """
            [Command("UPDATE docs SET body = @body")]
            public partial Task<int> SaveAsync([Json] string body, CancellationToken ct);
        """;

    private const string ArrayColumnMethod = """
            [Query("SELECT Id, Tags FROM posts")]
            public partial Task<List<TaggedRow>> ListAsync(CancellationToken ct);
        """;

    private static Diagnostic[] Run(string members, string repositoryAttribute, string assemblyAttribute = "")
    {
        var result = GeneratorHarness.RunGenerator(
            $$"""
            using System.Collections.Generic;
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;
            {{assemblyAttribute}}
            namespace TestApp
            {
                public sealed record TaggedRow(int Id, string[] Tags);

                {{repositoryAttribute}}
                public sealed partial class Repo(IAsyncDbConnection connection)
                {
            {{members}}
                }
            }
            """,
            NpgsqlStub.Source);
        return result.Diagnostics.Where(d => string.Equals(d.Id, "ZAO084", StringComparison.Ordinal)).ToArray();
    }

    [Theory]
    [InlineData("Sqlite")]
    [InlineData("SqlServer")]
    [InlineData("MySql")]
    public void Array_parameter_on_a_non_PostgreSql_dialect_reports_ZAO084(string dialect)
    {
        var zao084 = Assert.Single(Run(ArrayMethod, $"[Dialect(SqlDialect.{dialect})]"));

        Assert.Equal(DiagnosticSeverity.Error, zao084.Severity);
        Assert.Equal(
            "Parameter or column 'tags' uses a PostgreSQL array, which only PostgreSQL supports, "
                + $"but repository 'Repo' declares dialect {dialect}",
            zao084.GetMessage(CultureInfo.InvariantCulture));
        Assert.Equal("tags", zao084.Location.SourceTree!.GetText().ToString(zao084.Location.SourceSpan));
    }

    [Fact]
    public void Array_column_under_an_assembly_dialect_reports_ZAO084()
    {
        var zao084 = Assert.Single(Run(ArrayColumnMethod, "", "[assembly: Dialect(SqlDialect.SqlServer)]"));

        Assert.Contains("'Tags'", zao084.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void Json_parameter_on_a_Sqlite_repository_reports_ZAO084()
    {
        var zao084 = Assert.Single(Run(JsonMethod, "[Dialect(SqlDialect.Sqlite)]"));

        Assert.Contains("uses [Json]", zao084.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void PostgreSql_repository_reports_nothing()
        => Assert.Empty(Run(ArrayMethod + JsonMethod, "[Dialect(SqlDialect.PostgreSql)]"));

    [Fact]
    public void Repository_without_a_dialect_reports_nothing()
        => Assert.Empty(Run(ArrayMethod + JsonMethod, ""));

    [Fact]
    public void Type_level_PostgreSql_overrides_an_assembly_dialect()
        => Assert.Empty(Run(ArrayMethod, "[Dialect(SqlDialect.PostgreSql)]", "[assembly: Dialect(SqlDialect.SqlServer)]"));
}
