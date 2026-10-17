using System.Collections.ObjectModel;
using AwesomeAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.Postgres;

// PostgreSQL arrays against a real server, on a full NpgsqlConnection and on a slim data
// source with EnableArrays. Each case boots its own container, so keep the case count small.
[Trait("Provider", "Postgres")]
public sealed class PostgresArrayTests
{
    private const string Ddl = "CREATE TABLE tagged (id uuid PRIMARY KEY, tags text[] NULL);";

    public static TheoryData<string> Fixtures => new() { "full", "slim" };

    private static async Task<PostgresFixture> CreateAsync(string kind)
    {
        PostgresFixture? fx = null;
        try
        {
            fx = string.Equals(kind, "slim", StringComparison.Ordinal)
                ? await PostgresFixture.CreateSlimAndInitializeAsync().ConfigureAwait(false)
                : await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(Ddl).ConfigureAwait(false);
            return fx;
        }
        catch
        {
            if (fx is not null)
            {
                await fx.DisposeAsync().ConfigureAwait(false);
            }
            throw;
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task String_array_round_trips(string kind)
    {
        await using var fx = await CreateAsync(kind).ConfigureAwait(false);
        var repo = new ArrayRepo(fx.Connection);
        var id = Guid.NewGuid();

        (await repo.InsertAsync(id, ["red", "green", "blue"], CancellationToken.None).ConfigureAwait(false)).Should().Be(1);
        var row = await repo.GetAsync(id, CancellationToken.None).ConfigureAwait(false);

        row.Should().NotBeNull();
        row!.Id.Should().Be(id);
        row.Tags.Should().Equal("red", "green", "blue");
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task Any_with_guid_list_filters_rows(string kind)
    {
        await using var fx = await CreateAsync(kind).ConfigureAwait(false);
        var repo = new ArrayRepo(fx.Connection);
        // Ascending ids: Postgres orders uuid bytewise, so ORDER BY id matches the list order.
        var ids = Enumerable.Range(1, 4).Select(i => new Guid(i, 0, 0, new byte[8])).ToList();
        for (var i = 0; i < ids.Count; i++)
        {
            await repo.InsertAsync(ids[i], [$"t{i}"], CancellationToken.None).ConfigureAwait(false);
        }

        var rows = await repo.GetByIdsAsync([ids[1], ids[3]], CancellationToken.None).ConfigureAwait(false);

        rows.Select(r => r.Id).Should().Equal(ids[1], ids[3]);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task List_column_materialises(string kind)
    {
        await using var fx = await CreateAsync(kind).ConfigureAwait(false);
        var repo = new ArrayRepo(fx.Connection);
        var id = Guid.NewGuid();
        await repo.InsertAsync(id, ["x", "y"], CancellationToken.None).ConfigureAwait(false);

        var row = await repo.GetListAsync(id, CancellationToken.None).ConfigureAwait(false);

        row.Should().NotBeNull();
        row!.Tags.Should().BeOfType<List<string>>().Which.Should().Equal("x", "y");
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task Null_and_empty_arrays_round_trip(string kind)
    {
        await using var fx = await CreateAsync(kind).ConfigureAwait(false);
        var repo = new ArrayRepo(fx.Connection);
        var nullId = Guid.NewGuid();
        var emptyId = Guid.NewGuid();

        await repo.InsertAsync(nullId, null, CancellationToken.None).ConfigureAwait(false);
        await repo.InsertAsync(emptyId, [], CancellationToken.None).ConfigureAwait(false);

        var nullRow = await repo.GetNullableAsync(nullId, CancellationToken.None).ConfigureAwait(false);
        nullRow.Should().NotBeNull();
        nullRow!.Tags.Should().BeNull();

        var emptyRow = await repo.GetNullableAsync(emptyId, CancellationToken.None).ConfigureAwait(false);
        emptyRow.Should().NotBeNull();
        emptyRow!.Tags.Should().NotBeNull().And.BeEmpty();

        var stored = await ScalarAsync(fx, "SELECT tags::text FROM tagged WHERE tags IS NOT NULL").ConfigureAwait(false);
        stored.Should().Be("{}");
        var nulls = await ScalarAsync(fx, "SELECT count(*)::text FROM tagged WHERE tags IS NULL").ConfigureAwait(false);
        nulls.Should().Be("1");

        var none = await repo.GetByIdsAsync([], CancellationToken.None).ConfigureAwait(false);
        none.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task ReadOnlyCollection_parameter_binds_as_array(string kind)
    {
        await using var fx = await CreateAsync(kind).ConfigureAwait(false);
        var repo = new ArrayRepo(fx.Connection);
        var hit = Guid.NewGuid();
        var miss = Guid.NewGuid();
        await repo.InsertAsync(hit, ["a", "z"], CancellationToken.None).ConfigureAwait(false);
        await repo.InsertAsync(miss, ["q"], CancellationToken.None).ConfigureAwait(false);

        var rows = await repo.GetOverlappingAsync(new ReadOnlyCollection<string>(["a", "b"]), CancellationToken.None)
            .ConfigureAwait(false);

        rows.Should().ContainSingle().Which.Id.Should().Be(hit);
    }

    private static async Task<string?> ScalarAsync(PostgresFixture fx, string sql)
    {
        var cmd = fx.Connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = sql;
            return (await cmd.ExecuteScalarAsync().ConfigureAwait(false))?.ToString();
        }
    }
}
