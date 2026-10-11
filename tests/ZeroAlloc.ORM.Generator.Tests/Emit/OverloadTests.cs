using System.Linq;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// #328: overloaded [Query] / [Command] methods. Every overload gets its own
// implementation, and the per-method helpers emitted beside them stay distinct.
public class OverloadTests
{
    private const string Usings = """
        using System.Collections.Generic;
        using System.Data.Async;
        using System.Runtime.CompilerServices;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.ORM;

        namespace TestApp;

        """;

    [Fact]
    public void BulkInsert_overloads_each_get_an_implementation()
    {
        var source = Usings + """
            public sealed record RowA(int A);
            public sealed record RowB(int Id);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO T (a) VALUES (@A)", Kind = CommandKind.BulkInsert)]
                public partial Task<int> InsertAsync(IReadOnlyList<RowA> rows, CancellationToken ct);

                [Command("INSERT INTO U (Id) VALUES (@Id)", Kind = CommandKind.BulkInsert)]
                public partial Task<int> InsertAsync(IReadOnlyList<RowB> rows, CancellationToken ct);
            }
            """;

        var runResult = AssertCompilesCleanly(source);
        GeneratorSnapshot.Verify(runResult);
    }

    [Fact]
    public void Query_and_Command_overloads_of_every_shape_compile()
    {
        var source = Usings + """
            public sealed record OrderRow(int Id, decimal Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, Total FROM Orders WHERE Id = @id")]
                public partial Task<OrderRow?> GetAsync(int id, CancellationToken ct);

                [Query("SELECT Id, Total FROM Orders WHERE Code = @code")]
                public partial Task<OrderRow?> GetAsync(string code, CancellationToken ct);

                [Query("SELECT Id, Total FROM Orders WHERE CustomerId = @customerId")]
                public partial Task<IReadOnlyList<OrderRow>> ListAsync(int customerId, CancellationToken ct);

                [Query("SELECT Id, Total FROM Orders")]
                public partial Task<IReadOnlyList<OrderRow>> ListAsync(CancellationToken ct);

                [Query("SELECT Id, Total FROM Orders WHERE CustomerId = @customerId")]
                public partial IAsyncEnumerable<OrderRow> StreamAsync(int customerId, [EnumeratorCancellation] CancellationToken ct);

                [Query("SELECT Id, Total FROM Orders")]
                public partial IAsyncEnumerable<OrderRow> StreamAsync([EnumeratorCancellation] CancellationToken ct);

                [Query("SELECT COUNT(*) FROM Orders WHERE CustomerId = @customerId")]
                public partial Task<int> CountAsync(int customerId, CancellationToken ct);

                [Query("SELECT COUNT(*) FROM Orders")]
                public partial Task<int> CountAsync(CancellationToken ct);

                [Command("DELETE FROM Orders WHERE Id = @id")]
                public partial Task<int> DeleteAsync(int id, CancellationToken ct);

                [Command("DELETE FROM Orders WHERE Code = @code")]
                public partial Task<int> DeleteAsync(string code, CancellationToken ct);

                [Command("DELETE FROM Orders WHERE Id = @id")]
                public partial Task<int> DeleteAsync(int id, IAsyncDbTransaction transaction, CancellationToken ct);
            }
            """;

        AssertCompilesCleanly(source);
    }

    [Fact]
    public void Method_with_two_ORM_attributes_is_still_one_method()
    {
        // The dedupe that used to collapse overloads exists for this case: one
        // method seen by two attribute pipelines is one emit slot, not two.
        var source = Usings + """
            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT 1")]
                [Command("SELECT 1")]
                public partial Task<int> GetAsync(CancellationToken ct);

                [Query("SELECT 1")]
                public partial Task<int> GetAsync(int id, CancellationToken ct);
            }
            """;

        var result = RunTracked(source);
        result.Diagnostics.Select(d => d.Id).Should().Contain("ZAO005");
        result.Results[0].TrackedSteps["AllMethods"]
            .SelectMany(step => step.Outputs)
            .Should().HaveCount(2, "the doubly-attributed method is one slot and its overload another");
    }

    private static GeneratorDriverRunResult RunTracked(string source)
    {
        var compilation = GeneratorHarness.CreateCompilation(new[] { CSharpSyntaxTree.ParseText(source) });
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new ZeroAlloc.ORM.Generator.OrmGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        return driver.RunGenerators(compilation).GetRunResult();
    }

    private static GeneratorDriverRunResult AssertCompilesCleanly(string source)
    {
        var (runResult, compilation) = GeneratorHarness.RunGeneratorAndGetCompilation(source);
        runResult.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .Should().BeEmpty();
        compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .Should().BeEmpty();
        return runResult;
    }
}
