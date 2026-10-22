using System.Data.Async.Adapters;
using AwesomeAssertions;
using Npgsql;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// Writes guarded by a row version against a real PostgreSQL: a second writer changes the row
// between the read and the guarded write, which then affects no rows and throws. Each case
// boots its own container, so the rows a case writes are its own.
[Trait("Provider", "Postgres")]
public sealed class PostgresLinqConcurrencyTests
{
    private static async Task<PostgresFixture> CreateAsync()
    {
        PostgresFixture? fx = null;
        try
        {
            fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(LinqSchema.Ddl).ConfigureAwait(false);
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

    // A second connection to the same database, the other writer.
    private static async Task<NpgsqlConnection> OpenOtherAsync(PostgresFixture fx)
    {
        var raw = new NpgsqlConnection(fx.ConnectionString);
        await raw.OpenAsync().ConfigureAwait(false);
        return raw;
    }

    // The xmin a projection reads is the value the guard compares: once another writer updated
    // the row, the guarded update affects nothing and throws; with the xmin read again it applies.
    [Fact]
    public async Task Stale_xmin_update_throws_and_fresh_xmin_updates()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqConcurrencyRepo(fx.Connection);
        var id = Guid.NewGuid();
        await repo.InsertSessionAsync(id, CancellationToken.None).ConfigureAwait(false);
        var read = (await repo.GetSessionAsync(id, CancellationToken.None).ConfigureAwait(false))!;

        var raw = await OpenOtherAsync(fx).ConfigureAwait(false);
        await using (raw.ConfigureAwait(false))
        {
            var other = new LinqConcurrencyRepo(raw.AsAsync());
            (await other.RecordTurnUnguardedAsync(id, CancellationToken.None).ConfigureAwait(false)).Should().Be(1);
        }

        var stale = () => repo.RecordTurnAsync(id, read.Version, CancellationToken.None);
        await stale.Should().ThrowAsync<ZeroAllocOrmConcurrencyException>()
            .WithMessage("'LinqConcurrencyRepo.RecordTurnAsync' affected no rows*").ConfigureAwait(false);
        (await repo.GetSessionAsync(id, CancellationToken.None).ConfigureAwait(false))!.TurnCount.Should().Be(1);

        var fresh = await repo.GetSessionAsync(id, CancellationToken.None).ConfigureAwait(false);
        fresh!.Version.Should().NotBe(read.Version);
        await repo.RecordTurnAsync(id, fresh.Version, CancellationToken.None).ConfigureAwait(false);

        var after = await repo.GetSessionAsync(id, CancellationToken.None).ConfigureAwait(false);
        after!.TurnCount.Should().Be(2);
        after.Version.Should().NotBe(fresh.Version);
    }

    // Daedalus' Task maps RowVersion to xmin: it reads as the row's xmin and guards a rename as
    // Sql.Xmin does.
    [Fact]
    public async Task Mapped_xmin_property_reads_and_guards()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqConcurrencyRepo(fx.Connection);
        var id = Guid.NewGuid();
        await repo.InsertTaskAsync(id, "Draft", CancellationToken.None).ConfigureAwait(false);
        var read = (await repo.GetTaskAsync(id, CancellationToken.None).ConfigureAwait(false))!;

        var raw = await OpenOtherAsync(fx).ConfigureAwait(false);
        await using (raw.ConfigureAwait(false))
        {
            var cmd = new NpgsqlCommand("""SELECT xmin::text::bigint FROM "Tasks" WHERE "Id" = @id""", raw);
            await using (cmd.ConfigureAwait(false))
            {
                cmd.Parameters.AddWithValue("id", id);
                var xmin = (long)(await cmd.ExecuteScalarAsync().ConfigureAwait(false))!;
                read.RowVersion.Should().Be((uint)xmin);
            }

            var other = new LinqConcurrencyRepo(raw.AsAsync());
            await other.RenameAsync(id, read.RowVersion, "Theirs", CancellationToken.None).ConfigureAwait(false);
        }

        var stale = () => repo.RenameAsync(id, read.RowVersion, "Mine", CancellationToken.None);
        await stale.Should().ThrowAsync<ZeroAllocOrmConcurrencyException>().ConfigureAwait(false);
        (await repo.GetTaskAsync(id, CancellationToken.None).ConfigureAwait(false))!.Title.Should().Be("Theirs");

        var fresh = await repo.GetTaskAsync(id, CancellationToken.None).ConfigureAwait(false);
        await repo.RenameAsync(id, fresh!.RowVersion, "Mine", CancellationToken.None).ConfigureAwait(false);
        (await repo.GetTaskAsync(id, CancellationToken.None).ConfigureAwait(false))!.Title.Should().Be("Mine");
    }

    // A bytea row version compares by value and the guarded update writes the next one: a second
    // writer that saved first leaves the first writer's version stale.
    [Fact]
    public async Task Row_version_guard_detects_concurrent_write()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqConcurrencyRepo(fx.Connection);
        var id = Guid.NewGuid();
        byte[] first = [1, 0, 0, 0];
        byte[] second = [2, 0, 0, 0];
        byte[] third = [3, 0, 0, 0];
        await repo.InsertDocumentAsync(id, "v1", first, CancellationToken.None).ConfigureAwait(false);
        var read = (await repo.GetDocumentAsync(id, CancellationToken.None).ConfigureAwait(false))!;
        read.Version.Should().Equal(first);

        var raw = await OpenOtherAsync(fx).ConfigureAwait(false);
        await using (raw.ConfigureAwait(false))
        {
            var other = new LinqConcurrencyRepo(raw.AsAsync());
            await other.SaveDocumentAsync(id, [1, 0, 0, 0], second, "theirs", CancellationToken.None).ConfigureAwait(false);
        }

        var stale = () => repo.SaveDocumentAsync(id, read.Version, third, "mine", CancellationToken.None);
        await stale.Should().ThrowAsync<ZeroAllocOrmConcurrencyException>().ConfigureAwait(false);

        var current = await repo.GetDocumentAsync(id, CancellationToken.None).ConfigureAwait(false);
        current!.Body.Should().Be("theirs");
        current.Version.Should().Equal(second);

        await repo.SaveDocumentAsync(id, current.Version, third, "mine", CancellationToken.None).ConfigureAwait(false);
        var saved = await repo.GetDocumentAsync(id, CancellationToken.None).ConfigureAwait(false);
        saved!.Body.Should().Be("mine");
        saved.Version.Should().Equal(third);
    }
}
