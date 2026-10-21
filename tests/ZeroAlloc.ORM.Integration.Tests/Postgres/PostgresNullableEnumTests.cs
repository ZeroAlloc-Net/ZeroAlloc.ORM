using AwesomeAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.Postgres;

// A nullable enum parameter binds DBNull when null. The emit used to cast `(int)@status`, which
// throws on null, and call `@shape.ToString()`, which sends "" instead of NULL.
[Trait("Provider", "Postgres")]
public sealed class PostgresNullableEnumTests
{
    [Fact]
    public async Task Null_enums_round_trip_through_command_and_query()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync("CREATE TABLE nullable_enums (id int PRIMARY KEY, status int NULL, shape text NULL);").ConfigureAwait(false);
        var repo = new PostgresNullableEnumRepo(fx.Connection);

        await repo.InsertAsync(1, null, null, CancellationToken.None).ConfigureAwait(false);
        await repo.InsertAsync(2, Status.Cancelled, StringStatus.Cancelled, CancellationToken.None).ConfigureAwait(false);

        var all = await repo.FindAsync(null, null, CancellationToken.None).ConfigureAwait(false);
        all.Should().Equal(
            new PostgresNullableEnumRow(1, null, null),
            new PostgresNullableEnumRow(2, Status.Cancelled, StringStatus.Cancelled));
        (await repo.FindAsync(Status.Cancelled, null, CancellationToken.None).ConfigureAwait(false)).Select(r => r.Id).Should().Equal(2);
        (await repo.FindAsync(null, StringStatus.Cancelled, CancellationToken.None).ConfigureAwait(false)).Select(r => r.Id).Should().Equal(2);
        (await repo.FindAsync(Status.Pending, null, CancellationToken.None).ConfigureAwait(false)).Should().BeEmpty();
    }
}
