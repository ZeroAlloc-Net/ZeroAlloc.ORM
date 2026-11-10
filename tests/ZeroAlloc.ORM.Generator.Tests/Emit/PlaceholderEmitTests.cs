using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Xunit;
using ZeroAlloc.TestHelpers;
using ZeroAlloc.TypeConversions;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// #464: a [Query] or [Command] whose SQL holds a {name} placeholder reads its CommandText from a
// per-method cache: a static ConcurrentDictionary keyed by the member values, and an instance field
// filled from it on first use. Every other method's output stays byte-identical.
public class PlaceholderEmitTests
{
    private const string Usings = """
        using System.Collections.Generic;
        using System.Data.Async;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.ORM;

        namespace TestApp;

        """;

    private const string CountRepo = Usings + """
        public sealed partial class Repo(IAsyncDbConnection connection, string table)
        {
            [Query("SELECT COUNT(*) FROM {table}")]
            public partial Task<int> CountAsync(CancellationToken ct);
        }
        """;

    [Fact]
    public void Placeholder_Method_EmitsCacheAndUsesIt()
    {
        var source = GeneratedSource(CountRepo);

        Assert.Contains("private __ZaoPlaceholderSql? __sql0_CountAsync;", source, StringComparison.Ordinal);
        Assert.Contains(
            "private static readonly global::System.Collections.Concurrent.ConcurrentDictionary<string, __ZaoPlaceholderSql> __sqlCache0_CountAsync",
            source, StringComparison.Ordinal);
        Assert.Contains("private __ZaoPlaceholderSql __Sql0_CountAsync()", source, StringComparison.Ordinal);
        Assert.Contains("global::System.Threading.Volatile.Write(ref __sql0_CountAsync, __sql);", source, StringComparison.Ordinal);
        // One immutable holder per repository, holding Full and Parts.
        Assert.Equal(2, source.Split("private sealed class __ZaoPlaceholderSql").Length);
        Assert.Contains("public string[] Parts { get; }", source, StringComparison.Ordinal);
        Assert.Contains("__cmd.CommandText = __Sql0_CountAsync().Full;", source, StringComparison.Ordinal);
        Assert.Contains("global::System.ArgumentException.ThrowIfNullOrWhiteSpace(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("{table}", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Placeholder_Method_Snapshot()
    {
        GeneratorSnapshot.Verify(AssertCompilesWithoutWarnings(CountRepo));
    }

    [Fact]
    public void Literal_Method_EmitIsUnchanged()
    {
        // An existing fixture still renders exactly its committed snapshot.
        var fixture = Usings + """
            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT 1")]
                public partial Task<int> GetOneAsync(CancellationToken ct);
            }
            """;
        var snapshot = ReadSnapshot("ScalarIntTests.Scalar_int_query_emits_ExecuteScalar_call#TestApp.Repo.g.verified.cs");

        Assert.Equal(snapshot, Normalise(GeneratedSource(fixture)));
    }

    [Fact]
    public void Literal_Method_BesidePlaceholderMethod_EmitIsUnchanged()
    {
        var literalOnly = GeneratedSource(Usings + """
            public sealed partial class Repo(IAsyncDbConnection connection, string table)
            {
                [Query("SELECT 1")]
                public partial Task<int> GetOneAsync(CancellationToken ct);
            }
            """);
        var mixed = GeneratedSource(Usings + """
            public sealed partial class Repo(IAsyncDbConnection connection, string table)
            {
                [Query("SELECT 1")]
                public partial Task<int> GetOneAsync(CancellationToken ct);

                [Query("SELECT COUNT(*) FROM {table}")]
                public partial Task<int> CountAsync(CancellationToken ct);
            }
            """);

        // The literal-only class body, without its closing brace, is the start of the mixed one.
        var body = Normalise(literalOnly);
        var classStart = body.IndexOf("partial class Repo", StringComparison.Ordinal);
        var literalMethod = body.Substring(classStart, body.LastIndexOf('}') - classStart);
        Assert.Contains(literalMethod, Normalise(mixed), StringComparison.Ordinal);
        Assert.Contains("__cmd.CommandText = \"SELECT 1\";", mixed, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoMembers_KeyOnAValueTupleInOrderOfFirstAppearance()
    {
        var source = Usings + """
            public sealed partial class Repo(IAsyncDbConnection connection, string table, string schema)
            {
                [Command("DELETE FROM {schema}.{table} WHERE Id IN (SELECT Id FROM {schema}.{table})")]
                public partial Task<int> DeleteAsync(CancellationToken ct);
            }
            """;
        AssertCompilesWithoutWarnings(source);
        var generated = GeneratedSource(source);

        Assert.Contains("ConcurrentDictionary<(string, string), __ZaoPlaceholderSql> __sqlCache0_DeleteAsync", generated, StringComparison.Ordinal);
        Assert.Contains("var __v0 = @schema;", generated, StringComparison.Ordinal);
        Assert.Contains("var __v1 = @table;", generated, StringComparison.Ordinal);
        Assert.Contains("GetOrAdd((__v0, __v1), static __k =>", generated, StringComparison.Ordinal);
        Assert.Contains("\"DELETE FROM \" + __k.Item1 + \".\" + __k.Item2 + \" WHERE", generated, StringComparison.Ordinal);
        Assert.Contains("__cmd.CommandText = __Sql0_DeleteAsync().Full;", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void ColumnPlaceholder_EmitsBareName()
    {
        var source = Usings + """
            [Dialect(SqlDialect.Sqlite)]
            public sealed partial class Repo(IAsyncDbConnection connection, string table)
            {
                [Query("SELECT COUNT({col:Id}) FROM {table}")]
                public partial Task<int> CountAsync(CancellationToken ct);
            }
            """;
        AssertCompilesWithoutWarnings(source);

        Assert.Contains("\"SELECT COUNT(Id) FROM \" + __k", GeneratedSource(source), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Sqlite", "\"Id\"")]
    [InlineData("PostgreSql", "\"Id\"")]
    [InlineData("SqlServer", "[Id]")]
    [InlineData("MySql", "`Id`")]
    public void Column_QuotedPerDialect(string dialect, string quoted)
    {
        var source = Usings + $$"""
            [Dialect(SqlDialect.{{dialect}})]
            public sealed partial class Repo(IAsyncDbConnection connection, string table, bool quoteColumns)
            {
                [Query("SELECT {col:Id} FROM {table}")]
                public partial Task<int> GetAsync(CancellationToken ct);
            }
            """;
        AssertCompilesWithoutWarnings(source);
        var generated = GeneratedSource(source);

        Assert.Contains("ConcurrentDictionary<(string, bool), __ZaoPlaceholderSql> __sqlCache0_GetAsync", generated, StringComparison.Ordinal);
        Assert.Contains("var __v1 = @quoteColumns;", generated, StringComparison.Ordinal);
        Assert.Contains("GetOrAdd((__v0, __v1), static __k =>", generated, StringComparison.Ordinal);
        Assert.Contains(
            "\"SELECT \" + (__k.Item2 ? " + Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(quoted, quote: true) + " : \"Id\") + \" FROM \" + __k.Item1",
            generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Column_QuoteFlagOnly_KeysOnABool()
    {
        var source = Usings + """
            [Dialect(SqlDialect.SqlServer)]
            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                private readonly bool _quoteColumns = true;

                [Query("SELECT {col:Id}, {col:Name} FROM T; SELECT {col:Id} FROM T")]
                public partial Task<(IReadOnlyList<int> A, IReadOnlyList<int> B)> GetAsync(CancellationToken ct);
            }
            """;
        AssertCompilesWithoutWarnings(source);
        var generated = GeneratedSource(source);

        Assert.Contains("ConcurrentDictionary<bool, __ZaoPlaceholderSql> __sqlCache0_GetAsync", generated, StringComparison.Ordinal);
        Assert.Contains("var __v0 = @_quoteColumns;", generated, StringComparison.Ordinal);
        Assert.Contains("GetOrAdd(__v0, static __k =>", generated, StringComparison.Ordinal);
        Assert.Contains("(__k ? \"[Id]\" : \"Id\")", generated, StringComparison.Ordinal);
        Assert.Contains("(__k ? \"[Name]\" : \"Name\")", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("ThrowIfNullOrWhiteSpace(__v0", generated, StringComparison.Ordinal);
        // The split Parts hold the same expression for each statement.
        Assert.Contains("new string[] { \"SELECT \" + (__k ? \"[Id]\" : \"Id\") + \", \" + (__k ? \"[Name]\" : \"Name\") + \" FROM T\", \"SELECT \" + (__k ? \"[Id]\" : \"Id\") + \" FROM T\" }", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Column_TwoStringMembersAndFlag_KeysOnATripleWithFlagAsItem3()
    {
        var source = Usings + """
            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class Repo(IAsyncDbConnection connection, string schema, string table, bool quoteColumns)
            {
                [Query("SELECT {col:Id} FROM {schema}.{table}")]
                public partial Task<int> GetAsync(CancellationToken ct);
            }
            """;
        AssertCompilesWithoutWarnings(source);
        var generated = GeneratedSource(source);

        Assert.Contains("ConcurrentDictionary<(string, string, bool), __ZaoPlaceholderSql> __sqlCache0_GetAsync", generated, StringComparison.Ordinal);
        Assert.Contains("var __v2 = @quoteColumns;", generated, StringComparison.Ordinal);
        Assert.Contains("GetOrAdd((__v0, __v1, __v2), static __k =>", generated, StringComparison.Ordinal);
        Assert.Contains("(__k.Item3 ? \"\\\"Id\\\"\" : \"Id\")", generated, StringComparison.Ordinal);
        Assert.Contains("__k.Item1 + \".\" + __k.Item2", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Column_UnknownDialectValue_FallsBackToDoubleQuotes_AndCompiles()
    {
        var source = Usings + """
            public sealed partial class Repo(IAsyncDbConnection connection, bool quoteColumns)
            {
                [Query("SELECT {col:Id} FROM T")]
                public partial Task<int> GetAsync(CancellationToken ct);
            }
            """;
        var repo = Model.PlaceholderResolutionTests.RunRepository(source);

        var generated = ZeroAlloc.ORM.Generator.OrmGenerator.RenderRepository(repo, 99);

        Assert.Contains("(__k ? \"\\\"Id\\\"\" : \"Id\")", generated, StringComparison.Ordinal);
        var compilation = GeneratorHarness.CreateCompilation(new[]
        {
            Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source),
            Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(generated),
        });
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning && string.Equals(d.Location.SourceTree?.ToString(), generated, StringComparison.Ordinal)).Select(d => d.ToString()));
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()));
    }

    [Fact]
    public void Column_NoQuoteColumnsMember_EmitsBare()
    {
        var source = Usings + """
            [Dialect(SqlDialect.SqlServer)]
            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT {col:Id} FROM T")]
                public partial Task<int> GetAsync(CancellationToken ct);
            }
            """;
        AssertCompilesWithoutWarnings(source);
        var generated = GeneratedSource(source);

        Assert.Contains("private static readonly __ZaoPlaceholderSql __sqlCache0_GetAsync = new __ZaoPlaceholderSql(\"SELECT Id FROM T\"", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("[Id]", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void ColumnPlaceholderOnly_UsesOneStaticEntry()
    {
        var source = Usings + """
            [Dialect(SqlDialect.Sqlite)]
            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT COUNT({col:Id}) FROM T")]
                public partial Task<int> CountAsync(CancellationToken ct);
            }
            """;
        AssertCompilesWithoutWarnings(source);
        var generated = GeneratedSource(source);

        Assert.Contains("private static readonly __ZaoPlaceholderSql __sqlCache0_CountAsync = new __ZaoPlaceholderSql(", generated, StringComparison.Ordinal);
        Assert.Contains("\"SELECT COUNT(Id) FROM T\"", generated, StringComparison.Ordinal);
        Assert.Contains("__cmd.CommandText = __Sql0_CountAsync().Full;", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void MultiStatement_PartsHoldEachStatementResolved()
    {
        var source = Usings + """
            public sealed partial class Repo(IAsyncDbConnection connection, string table)
            {
                [Command("DELETE FROM {table} WHERE Id = @id; SELECT changes()", Kind = CommandKind.Scalar)]
                public partial Task<int> DeleteAsync(int id, CancellationToken ct);
            }
            """;
        AssertCompilesWithoutWarnings(source);
        var generated = GeneratedSource(source);

        Assert.Contains("\"DELETE FROM \" + __k + \" WHERE Id = @id; SELECT changes()\"", generated, StringComparison.Ordinal);
        // Each part is trimmed at build time, as the batch emit trims a literal statement.
        Assert.Contains("new string[] { \"DELETE FROM \" + __k + \" WHERE Id = @id\", \"SELECT changes()\" }", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Overloads_GetDistinctCaches()
    {
        var source = Usings + """
            public sealed partial class Repo(IAsyncDbConnection connection, string table)
            {
                [Query("SELECT COUNT(*) FROM {table}")]
                public partial Task<int> CountAsync(CancellationToken ct);

                [Query("SELECT COUNT(*) FROM {table} WHERE Id > @min")]
                public partial Task<int> CountAsync(int min, CancellationToken ct);
            }
            """;
        AssertCompilesWithoutWarnings(source);
        var generated = GeneratedSource(source);

        Assert.Contains("__cmd.CommandText = __Sql0_CountAsync().Full;", generated, StringComparison.Ordinal);
        Assert.Contains("__cmd.CommandText = __Sql1_CountAsync().Full;", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void LiteralPieceWithQuoteBackslashAndNewline_IsEscaped()
    {
        var source = Usings + """
            public sealed partial class Repo(IAsyncDbConnection connection, string table)
            {
                [Query("SELECT COUNT(*) FROM {table}\nWHERE 'say \"hi\" \\ ok' <> ''")]
                public partial Task<int> CountAsync(CancellationToken ct);
            }
            """;
        AssertCompilesWithoutWarnings(source);

        Assert.Contains(
            "\"SELECT COUNT(*) FROM \" + __k + \"\\nWHERE 'say \\\"hi\\\" \\\\ ok' <> ''\"",
            GeneratedSource(source), StringComparison.Ordinal);
    }

    [Fact]
    public void PlaceholdersAtStartAndEnd_HaveNoEmptyLiteral()
    {
        var source = Usings + """
            public sealed partial class Repo(IAsyncDbConnection connection, string verb, string table)
            {
                [Command("{verb} FROM {table}")]
                public partial Task<int> RunAsync(CancellationToken ct);
            }
            """;
        AssertCompilesWithoutWarnings(source);
        var generated = GeneratedSource(source);

        Assert.Contains("new __ZaoPlaceholderSql(__k.Item1 + \" FROM \" + __k.Item2, new string[] { __k.Item1 + \" FROM \" + __k.Item2 })", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("\"\" +", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void SqlThatIsOnlyThePlaceholder_IsTheValue()
    {
        var source = Usings + """
            public sealed partial class Repo(IAsyncDbConnection connection, string table)
            {
                [Command("{table}")]
                public partial Task<int> RunAsync(CancellationToken ct);
            }
            """;
        AssertCompilesWithoutWarnings(source);

        Assert.Contains("new __ZaoPlaceholderSql(__k, new string[] { __k })", GeneratedSource(source), StringComparison.Ordinal);
    }

    private const string TupleRepo = Usings + """
        public sealed record Row(int Id, string Name);

        public sealed partial class Repo(IAsyncDbConnection connection, string table)
        {
            [Query("SELECT Id, Name FROM {table} WHERE Id < @max; SELECT Id, Name FROM {table} WHERE Id >= @max;", Batch = BatchMode.Always)]
            public partial Task<(List<Row> Low, List<Row> High)> SplitAsync(int max, CancellationToken ct);

            [Query("SELECT Id, Name FROM {table} WHERE Id < @max; SELECT Id, Name FROM {table} WHERE Id >= @max;")]
            public partial Task<(List<Row> Low, List<Row> High)> SplitAutoAsync(int max, CancellationToken ct);
        }
        """;

    [Fact]
    public void MultiStatement_UsesPartsPerStatement()
    {
        var source = TupleRepo;
        AssertCompilesWithoutWarnings(source);
        var generated = GeneratedSource(source);

        // Each method reads its accessor once, then every statement takes its own part. The Always
        // method comes first in the source and the Auto method after it, so slicing at the Auto
        // method's accessor read proves each emitted its own assignments.
        var autoStart = generated.IndexOf("var __parts = __Sql1_SplitAutoAsync().Parts;", StringComparison.Ordinal);
        var alwaysStart = generated.IndexOf("var __parts = __Sql0_SplitAsync().Parts;", StringComparison.Ordinal);
        Assert.True(alwaysStart >= 0 && autoStart > alwaysStart);
        var always = generated.Substring(alwaysStart, autoStart - alwaysStart);
        var auto = generated.Substring(autoStart);

        foreach (var section in new[] { always, auto })
        {
            // One local per statement, one part per local, and no other batch command.
            Assert.Equal(1, Count(section, "var __parts = "));
            Assert.Equal(2, Count(section, "= __batch.CreateBatchCommand();"));
            Assert.Equal(2, Count(section, ".CommandText = __parts["));
            Assert.Contains("__cmd0.CommandText = __parts[0];", section, StringComparison.Ordinal);
            Assert.Contains("__cmd1.CommandText = __parts[1];", section, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("{table}", generated, StringComparison.Ordinal);
        Assert.DoesNotContain(".Trim()", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void MultiStatement_JoinedFallback_UsesFull()
    {
        var generated = GeneratedSource(TupleRepo);

        // The fallback branch of BatchMode.Auto runs the joined text.
        Assert.Contains("__cmd.CommandText = __Sql1_SplitAutoAsync().Full;", generated, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SELECT 1 FROM {table}; SELECT 2 FROM {table}")]
    [InlineData("SELECT ';' FROM {table}; SELECT 2 FROM {table};")]
    // Deliberate: the splitter ignores a ';' inside a comment, and this locks that in for both texts.
    [InlineData("SELECT 1 FROM {table} -- a; b\n; SELECT 2")]
    [InlineData("SELECT '{table}' FROM {table}; SELECT /* ; */ 2")]
    public void MultiStatement_MaskedAndTemplateAgreeOnStatementCount(string template)
    {
        var masked = ZeroAlloc.ORM.Generator.SqlPlaceholderScanner.Scan(template).MaskedSql;

        Assert.Equal(
            SqlStatementSplitter.CountStatements(masked),
            SqlStatementSplitter.Split(template).Length);
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private static string GeneratedSource(string source)
    {
        var result = GeneratorHarness.RunGenerator(source);
        return Assert.Single(result.Results[0].GeneratedSources).SourceText.ToString();
    }

    private static GeneratorDriverRunResult AssertCompilesWithoutWarnings(string source)
    {
        var (runResult, compilation) = GeneratorHarness.RunGeneratorAndGetCompilation(source);
        Assert.Empty(runResult.Diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning).Select(d => d.ToString()));
        // Only the generated tree: the harness compilation references no analyzers, so its
        // warnings are the compiler's own, and those in generated code are ours.
        var generatedTree = Assert.Single(runResult.GeneratedTrees);
        Assert.Empty(compilation.GetDiagnostics()
            .Where(d => d.Severity >= DiagnosticSeverity.Warning && d.Location.SourceTree == generatedTree)
            .Select(d => d.ToString()));
        Assert.Empty(compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString()));
        return runResult;
    }

    private static string Normalise(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string ReadSnapshot(string fileName, [CallerFilePath] string testFile = "")
    {
        var path = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(testFile))!, "Snapshots", fileName);
        var text = Normalise(File.ReadAllText(path)).TrimStart('﻿');
        // Drop the //HintName: line the snapshot helper writes above the source.
        return text.Substring(text.IndexOf('\n', StringComparison.Ordinal) + 1);
    }
}
