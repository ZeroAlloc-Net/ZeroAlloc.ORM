using FluentAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests;

public class DomainEntityReadTests
{
    // DomainEntity = plain class with a single multi-arg ctor. Each column reads via
    // `__reader.GetOrdinal("ColumnName")` so SELECT column order is irrelevant. Here
    // we deliberately SELECT a column order that differs from the ctor parameter
    // order (Total, CustomerId, Id) to prove the GetOrdinal lookup is doing the work.
    [Fact]
    public async Task Reads_seeded_row_into_class_with_named_columns()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER NOT NULL, Total NUMERIC NOT NULL);
                INSERT INTO Orders (Id, CustomerId, Total) VALUES (1, 42, 99.95);").ConfigureAwait(false);

            var repo = new DomainEntityRepo(fx.Connection);
            var entity = await repo.GetByIdAsync(1, CancellationToken.None).ConfigureAwait(false);

            entity.Should().NotBeNull();
            entity!.Id.Should().Be(1);
            entity.CustomerId.Should().Be(42);
            entity.Total.Should().Be(99.95m);
        }
    }

    [Fact]
    public async Task Empty_result_returns_null()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER NOT NULL, Total NUMERIC NOT NULL);").ConfigureAwait(false);

            var repo = new DomainEntityRepo(fx.Connection);
            var entity = await repo.GetByIdAsync(99, CancellationToken.None).ConfigureAwait(false);

            entity.Should().BeNull();
        }
    }
}
