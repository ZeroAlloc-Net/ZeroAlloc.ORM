using System.Globalization;
using FluentAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.Postgres;

// v0.6 Phase A.1 — smoke test asserting the Postgres test fixture boots a
// container, opens the wrapped IAsyncDbConnection, runs a trivial scalar
// query, and disposes cleanly. This is the substrate gate for every other
// Postgres-targeted integration test in this folder.
//
// [Trait("Provider", "Postgres")] tags every Postgres test so CI / dotnet test
// callers can filter ("--filter Provider=Postgres") if Docker is unavailable
// on a given runner. The fixture intentionally does NOT auto-skip on missing
// Docker — see PostgresFixture.cs for the rationale: surfacing a clear
// container-start failure beats a silent skip when the integration suite is
// supposed to run.
[Trait("Provider", "Postgres")]
public sealed class PostgresFixtureSmokeTests
{
    [Fact]
    public async Task Fixture_starts_opens_runs_select_one_closes_cleanly()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);

        var cmd = fx.Connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = "SELECT 1";
            var result = await cmd.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);

            result.Should().NotBeNull();
            Convert.ToInt32(result, CultureInfo.InvariantCulture).Should().Be(1);
        }
    }

    [Fact]
    public async Task Fixture_CanCreateBatch_is_true_on_npgsql()
    {
        // v0.3-CLN3 verification: Npgsql ≥6 implements DbBatch via DbConnection.CreateBatch,
        // and the AdoNet.Async wrapper forwards CanCreateBatch on real-batching providers.
        // This test pins the assumption that the Postgres-backed multi-result-set Auto
        // tests will exercise the IAsyncDbBatch branch (not the ;-joined fallback that
        // Sqlite forces). If this fails, the Auto/Never tests in PostgresMultiResultSetTests
        // collapse to the same fallback — and we need to either (a) chase the AdoNet.Async
        // forwarding, or (b) downgrade the v0.3-CLN3 close-out claim.
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        fx.Connection.CanCreateBatch.Should().BeTrue();
    }
}
