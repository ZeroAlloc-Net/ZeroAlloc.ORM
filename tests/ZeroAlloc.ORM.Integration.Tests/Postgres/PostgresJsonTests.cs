using AwesomeAssertions;
using Npgsql;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.Postgres;

// [Json] string parameters against a real server, on a full NpgsqlConnection and on a slim
// data source with EnableArrays. Each case boots its own container, so related checks share one.
[Trait("Provider", "Postgres")]
public sealed class PostgresJsonTests
{
    private const string Ddl = "CREATE TABLE documents (id uuid PRIMARY KEY, body jsonb NOT NULL, note jsonb NULL);";

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
    public async Task Json_round_trips_without_cast(string kind)
    {
        await using var fx = await CreateAsync(kind).ConfigureAwait(false);
        var repo = new JsonRepo(fx.Connection);
        var id = Guid.NewGuid();

        // The SQL in JsonRepo has no ::jsonb cast. Without the Jsonb parameter type Npgsql sends text
        // and Postgres rejects it for a jsonb column, so a successful insert proves the type is set.
        (await repo.InsertAsync(id, "{\"b\":[1,2],\"a\":\"x\"}", "[ true ]", CancellationToken.None).ConfigureAwait(false))
            .Should().Be(1);

        (await repo.GetFieldAAsync(id, CancellationToken.None).ConfigureAwait(false)).Should().Be("x");
        var row = await repo.GetAsync(id, CancellationToken.None).ConfigureAwait(false);
        row.Should().NotBeNull();
        row!.Id.Should().Be(id);
        // The input is not canonical. jsonb reorders keys and normalises whitespace, plain text would not.
        row.Body.Should().Be("{\"a\": \"x\", \"b\": [1, 2]}");
        row.Note.Should().Be("[true]");
    }

    [Fact]
    public void Sql_carries_no_jsonb_cast()
    {
        string[] statements =
        [
            JsonRepo.InsertSql, JsonRepo.GetSql, JsonRepo.FieldASql,
            JsonRepo.NoteIsNullSql, JsonRepo.InsertPayloadSql, JsonRepo.InsertManySql,
        ];

        foreach (var sql in statements)
        {
            sql.Should().NotContain("::jsonb").And.NotContain("CAST(");
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task Null_json_stores_sql_null(string kind)
    {
        await using var fx = await CreateAsync(kind).ConfigureAwait(false);
        var repo = new JsonRepo(fx.Connection);
        var withNull = Guid.NewGuid();
        var withValue = Guid.NewGuid();

        await repo.InsertAsync(withNull, "{}", null, CancellationToken.None).ConfigureAwait(false);
        await repo.InsertAsync(withValue, "{}", "null", CancellationToken.None).ConfigureAwait(false);

        (await repo.NoteIsNullAsync(withNull, CancellationToken.None).ConfigureAwait(false)).Should().BeTrue();
        // The JSON literal null is a value, not SQL NULL.
        (await repo.NoteIsNullAsync(withValue, CancellationToken.None).ConfigureAwait(false)).Should().BeFalse();
        var row = await repo.GetAsync(withNull, CancellationToken.None).ConfigureAwait(false);
        row!.Note.Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task Invalid_json_surfaces_postgres_error(string kind)
    {
        await using var fx = await CreateAsync(kind).ConfigureAwait(false);
        var repo = new JsonRepo(fx.Connection);

        var act = async () => await repo.InsertAsync(Guid.NewGuid(), "{not json", null, CancellationToken.None)
            .ConfigureAwait(false);

        var ex = (await act.Should().ThrowAsync<PostgresException>().ConfigureAwait(false)).Which;
        ex.SqlState.Should().Be("22P02");
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task Composite_member_and_bulk_rows_bind_as_jsonb(string kind)
    {
        await using var fx = await CreateAsync(kind).ConfigureAwait(false);
        var repo = new JsonRepo(fx.Connection);
        var composite = Guid.NewGuid();
        var bulkA = Guid.NewGuid();
        var bulkB = Guid.NewGuid();

        (await repo.InsertPayloadAsync(composite, new JsonPayload("n", "{\"a\": \"c\"}"), CancellationToken.None)
            .ConfigureAwait(false)).Should().Be(1);
        (await repo.InsertManyAsync(
            [new JsonBulkRow(bulkA, "{\"a\": \"p\"}", "[1]"), new JsonBulkRow(bulkB, "{\"a\": \"q\"}", null)],
            CancellationToken.None).ConfigureAwait(false)).Should().Be(2);

        (await repo.GetFieldAAsync(composite, CancellationToken.None).ConfigureAwait(false)).Should().Be("c");
        (await repo.GetFieldAAsync(bulkA, CancellationToken.None).ConfigureAwait(false)).Should().Be("p");
        (await repo.GetFieldAAsync(bulkB, CancellationToken.None).ConfigureAwait(false)).Should().Be("q");
        (await repo.NoteIsNullAsync(bulkA, CancellationToken.None).ConfigureAwait(false)).Should().BeFalse();
        (await repo.NoteIsNullAsync(bulkB, CancellationToken.None).ConfigureAwait(false)).Should().BeTrue();
    }
}
