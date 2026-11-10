using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Xunit;
using ZeroAlloc.TestHelpers;

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

        Assert.Contains("private string? __sql0_CountAsync;", source, StringComparison.Ordinal);
        Assert.Contains(
            "private static readonly global::System.Collections.Concurrent.ConcurrentDictionary<string, (string Full, string[] Parts)> __sqlCache0_CountAsync",
            source, StringComparison.Ordinal);
        Assert.Contains("private (string Full, string[] Parts) __Sql0_CountAsync()", source, StringComparison.Ordinal);
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

        Assert.Contains("ConcurrentDictionary<(string, string), (string Full, string[] Parts)> __sqlCache0_DeleteAsync", generated, StringComparison.Ordinal);
        Assert.Contains("var __v0 = @schema;", generated, StringComparison.Ordinal);
        Assert.Contains("var __v1 = @table;", generated, StringComparison.Ordinal);
        Assert.Contains("GetOrAdd((__v0, __v1), static __k => (", generated, StringComparison.Ordinal);
        Assert.Contains("\"DELETE FROM \" + __k.Item1 + \".\" + __k.Item2 + \" WHERE", generated, StringComparison.Ordinal);
        Assert.Contains("__cmd.CommandText = __Sql0_DeleteAsync().Full;", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void ColumnPlaceholder_EmitsBareName()
    {
        var source = Usings + """
            public sealed partial class Repo(IAsyncDbConnection connection, string table)
            {
                [Query("SELECT COUNT({col:Id}) FROM {table}")]
                public partial Task<int> CountAsync(CancellationToken ct);
            }
            """;
        AssertCompilesWithoutWarnings(source);

        Assert.Contains("\"SELECT COUNT(Id) FROM \" + __k", GeneratedSource(source), StringComparison.Ordinal);
    }

    [Fact]
    public void ColumnPlaceholderOnly_UsesOneStaticEntry()
    {
        var source = Usings + """
            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT COUNT({col:Id}) FROM T")]
                public partial Task<int> CountAsync(CancellationToken ct);
            }
            """;
        AssertCompilesWithoutWarnings(source);
        var generated = GeneratedSource(source);

        Assert.Contains("private static readonly (string Full, string[] Parts) __sqlCache0_CountAsync = (", generated, StringComparison.Ordinal);
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
        Assert.Contains("new string[] { \"DELETE FROM \" + __k + \" WHERE Id = @id\", \" SELECT changes()\" }", generated, StringComparison.Ordinal);
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
