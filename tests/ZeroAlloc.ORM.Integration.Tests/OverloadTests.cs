using AwesomeAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests;

public class OverloadTests
{
    [Fact]
    public async Task Each_overload_runs_its_own_sql()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync("CREATE TABLE TA (A INTEGER NOT NULL)").ConfigureAwait(false);
            await fx.ExecuteDdlAsync("CREATE TABLE TB (Id INTEGER NOT NULL)").ConfigureAwait(false);
            var repo = new OverloadRepo(fx.Connection);

            var insertedA = await repo.InsertAsync([new OverloadRowA(1), new OverloadRowA(2)], CancellationToken.None).ConfigureAwait(false);
            var insertedB = await repo.InsertAsync([new OverloadRowB(5), new OverloadRowB(6), new OverloadRowB(7)], CancellationToken.None).ConfigureAwait(false);

            insertedA.Should().Be(2);
            insertedB.Should().Be(3);
            (await repo.CountAsync(CancellationToken.None).ConfigureAwait(false)).Should().Be(2);
            (await repo.CountAsync(6, CancellationToken.None).ConfigureAwait(false)).Should().Be(2);
        }
    }
}
