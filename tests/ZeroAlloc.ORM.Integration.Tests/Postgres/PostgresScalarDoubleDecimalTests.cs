using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.Postgres;

[Trait("Provider", "Postgres")]
public sealed class PostgresScalarDoubleDecimalTests
{
    private const string Schema =
        "CREATE TABLE t (x INTEGER NOT NULL, d NUMERIC NOT NULL);"
        + " INSERT INTO t (x, d) VALUES (1, 1.25); INSERT INTO t (x, d) VALUES (2, 2.5);";

    [Fact]
    public async Task Average_and_sum_read_back_exactly()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(Schema).ConfigureAwait(false);
        var repo = new ScalarDoubleDecimalRepo(fx.Connection);

        Assert.Equal(1.5d, await repo.AverageAsync(CancellationToken.None).ConfigureAwait(false));
        Assert.Equal(3.75m, await repo.SumAsync(CancellationToken.None).ConfigureAwait(false));
        Assert.Equal(1.5d, await repo.AverageValueAsync(CancellationToken.None).ConfigureAwait(false));
        Assert.Equal(3.75m, await repo.SumValueAsync(CancellationToken.None).ConfigureAwait(false));
    }

    [Fact]
    public async Task No_row_returns_zero()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        var repo = new ScalarDoubleDecimalRepo(fx.Connection);

        Assert.Equal(0d, await repo.NoRowDoubleAsync(CancellationToken.None).ConfigureAwait(false));
        Assert.Equal(0m, await repo.NoRowDecimalAsync(CancellationToken.None).ConfigureAwait(false));
    }
}
