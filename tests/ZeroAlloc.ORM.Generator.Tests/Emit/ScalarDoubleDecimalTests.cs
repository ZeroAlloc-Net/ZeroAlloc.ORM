using Xunit;

using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

public class ScalarDoubleDecimalTests
{
    private const string Header = """
        using System.Data.Async;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.ORM;

        namespace TestApp;

        public sealed partial class Repo(IAsyncDbConnection connection)
        {
        """;

    private static string Repo(string members) => Header + "\n" + members + "\n}\n";

    [Fact]
    public void Query_returning_double_reads_scalar()
    {
        var source = Repo("""
                [Query("SELECT avg(x) FROM t")]
                public partial Task<double> AverageAsync(CancellationToken ct);
            """);
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public void Query_returning_decimal_reads_scalar()
    {
        var source = Repo("""
                [Query("SELECT sum(d) FROM t")]
                public partial Task<decimal> SumAsync(CancellationToken ct);
            """);
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public void Query_returning_valuetask_double_reads_scalar()
    {
        var source = Repo("""
                [Query("SELECT avg(x) FROM t")]
                public partial ValueTask<double> AverageAsync(CancellationToken ct);
            """);
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public void Query_returning_valuetask_decimal_reads_scalar()
    {
        var source = Repo("""
                [Query("SELECT sum(d) FROM t")]
                public partial ValueTask<decimal> SumAsync(CancellationToken ct);
            """);
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public void Nullable_double_decimal_int_long_scalars()
    {
        var source = Repo("""
                [Query("SELECT avg(x) FROM t")]
                public partial Task<double?> AverageAsync(CancellationToken ct);

                [Query("SELECT sum(d) FROM t")]
                public partial Task<decimal?> SumDecimalAsync(CancellationToken ct);

                [Query("SELECT max(x) FROM t")]
                public partial Task<int?> MaxIntAsync(CancellationToken ct);

                [Query("SELECT max(x) FROM t")]
                public partial Task<long?> MaxLongAsync(CancellationToken ct);
            """);
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public void Factory_annotated_double_and_decimal_still_resolve_factory_not_scalar_shape()
    {
        var source = Repo("""
                [return: Materialize(Factory = "FromStorage")]
                [Query("SELECT avg(x) FROM t")]
                public partial Task<double> AverageAsync(CancellationToken ct);

                [return: Materialize(Factory = "FromStorage")]
                [Query("SELECT sum(d) FROM t")]
                public partial Task<decimal> SumAsync(CancellationToken ct);
            """);
        var result = GeneratorHarness.RunGenerator(source);
        var ids = string.Join(",", System.Linq.Enumerable.Select(result.Results[0].Diagnostics, d => d.Id));
        Assert.Equal("ZAO043,ZAO043", ids);
    }
}
