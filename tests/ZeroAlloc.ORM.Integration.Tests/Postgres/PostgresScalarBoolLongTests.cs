using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.Postgres;

[Trait("Provider", "Postgres")]
public sealed class PostgresScalarBoolLongTests
{
    private const string Schema = "CREATE TABLE t (id INTEGER PRIMARY KEY); INSERT INTO t (id) VALUES (1); INSERT INTO t (id) VALUES (2);";

    [Fact]
    public async Task Exists_returns_true_and_false()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(Schema).ConfigureAwait(false);
        var repo = new ScalarBoolLongRepo(fx.Connection);

        Assert.True(await repo.ExistsAsync(1, CancellationToken.None).ConfigureAwait(false));
        Assert.False(await repo.ExistsAsync(99, CancellationToken.None).ConfigureAwait(false));
    }

    [Fact]
    public async Task Count_returns_long()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(Schema).ConfigureAwait(false);
        var repo = new ScalarBoolLongRepo(fx.Connection);

        Assert.Equal(2L, await repo.CountAsync(CancellationToken.None).ConfigureAwait(false));
    }
}
