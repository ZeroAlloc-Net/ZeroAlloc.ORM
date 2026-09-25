using FluentAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests;

// v2.0, #219 — Microsoft.Data.Sqlite binds the generator's unprefixed
// ParameterName to an `@name`, `:name` or `$name` placeholder: it looks the
// name up as written, then retries with each of those sigils prepended.
// SQLite has no stored procedures, so the sproc half of this coverage lives
// in PostgresParameterBindingTests and SqlServerParameterBindingTests.
public sealed class ParameterBindingTests
{
    [Fact]
    public async Task Scalar_composite_and_override_parameters_bind_to_at_placeholders()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(ParameterBindingRepo.OrdersDdl).ConfigureAwait(false);

            var repo = new ParameterBindingRepo(fx.Connection);
            var id = await repo.FirstMatchAsync(7, new Money(50m, "EUR"), excludedId: 1, CancellationToken.None)
                .ConfigureAwait(false);

            id.Should().Be(ParameterBindingRepo.ExpectedMatch);
        }
    }

    [Fact]
    public async Task Unprefixed_parameters_bind_to_colon_placeholders()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(ParameterBindingRepo.OrdersDdl).ConfigureAwait(false);

            var repo = new ParameterBindingRepo(fx.Connection);
            var id = await repo.FirstForCustomerColonAsync(7, excludedId: 1, CancellationToken.None)
                .ConfigureAwait(false);

            id.Should().Be(2);
        }
    }
}
