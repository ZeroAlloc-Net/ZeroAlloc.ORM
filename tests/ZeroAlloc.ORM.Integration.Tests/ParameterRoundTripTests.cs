using System.Globalization;
using FluentAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests;

public class ParameterRoundTripTests
{
    [Fact]
    public async Task Int_parameter_roundtrips()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Things (Id INTEGER PRIMARY KEY, Value INTEGER NOT NULL);
                INSERT INTO Things (Id, Value) VALUES (1, 42), (2, 84);").ConfigureAwait(false);

            var repo = new IntParamRepo(fx.Connection);
            var result = await repo.GetByIdAsync(1, CancellationToken.None).ConfigureAwait(false);
            result.Should().Be(42);
        }
    }

    [Fact]
    public async Task String_parameter_roundtrips()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Things (Name TEXT PRIMARY KEY, Value INTEGER NOT NULL);
                INSERT INTO Things (Name, Value) VALUES ('alpha', 7), ('beta', 13);").ConfigureAwait(false);

            var repo = new StringParamRepo(fx.Connection);
            var result = await repo.GetByNameAsync("beta", CancellationToken.None).ConfigureAwait(false);
            result.Should().Be(13);
        }
    }

    [Fact]
    public async Task Decimal_parameter_roundtrips()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Things (Price NUMERIC PRIMARY KEY, Value INTEGER NOT NULL);
                INSERT INTO Things (Price, Value) VALUES (9.99, 100), (19.95, 200);").ConfigureAwait(false);

            var repo = new DecimalParamRepo(fx.Connection);
            var result = await repo.GetByPriceAsync(19.95m, CancellationToken.None).ConfigureAwait(false);
            result.Should().Be(200);
        }
    }

    [Fact]
    public async Task Guid_parameter_roundtrips()
    {
        var guid = new Guid("11111111-2222-3333-4444-555555555555");
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            // Microsoft.Data.Sqlite stores Guid as TEXT in the canonical upper-case
            // 8-4-4-4-12 format. Seed with that literal so the bound parameter matches.
            await fx.ExecuteDdlAsync(string.Format(
                CultureInfo.InvariantCulture,
                @"
                    CREATE TABLE Things (Guid TEXT PRIMARY KEY, Value INTEGER NOT NULL);
                    INSERT INTO Things (Guid, Value) VALUES ('{0}', 555);",
                guid.ToString().ToUpperInvariant())).ConfigureAwait(false);

            var repo = new GuidParamRepo(fx.Connection);
            var result = await repo.GetByGuidAsync(guid, CancellationToken.None).ConfigureAwait(false);
            result.Should().Be(555);
        }
    }

    [Fact]
    public async Task DateTime_parameter_roundtrips()
    {
        var created = new DateTime(2026, 5, 30, 12, 34, 56, DateTimeKind.Utc);
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            // Microsoft.Data.Sqlite stores DateTime as TEXT using the ISO-8601 round-trip
            // pattern "yyyy-MM-dd HH:mm:ss" (space separator, no timezone suffix). Seed
            // exactly that literal so the bound parameter equality holds.
            await fx.ExecuteDdlAsync(string.Format(
                CultureInfo.InvariantCulture,
                @"
                    CREATE TABLE Things (Created TEXT PRIMARY KEY, Value INTEGER NOT NULL);
                    INSERT INTO Things (Created, Value) VALUES ('{0}', 909);",
                created.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))).ConfigureAwait(false);

            var repo = new DateTimeParamRepo(fx.Connection);
            var result = await repo.GetByCreatedAsync(created, CancellationToken.None).ConfigureAwait(false);
            result.Should().Be(909);
        }
    }

    [Fact]
    public async Task Nullable_int_parameter_null_value_roundtrips()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Things (Id INTEGER PRIMARY KEY, Value INTEGER NOT NULL);
                INSERT INTO Things (Id, Value) VALUES (1, 77);").ConfigureAwait(false);

            var repo = new NullableIntParamRepo(fx.Connection);
            var result = await repo.GetWhenNullAsync(null, CancellationToken.None).ConfigureAwait(false);
            result.Should().Be(77);
        }
    }
}
