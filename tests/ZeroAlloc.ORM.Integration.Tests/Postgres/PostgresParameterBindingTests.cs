using FluentAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.Postgres;

// v2.0, #219 — Npgsql stores a parameter's name with a leading `@` or `:`
// trimmed and matches placeholders against that trimmed name, so the
// generator's unprefixed ParameterName binds `@name` and `:name` alike.
[Trait("Provider", "Postgres")]
public sealed class PostgresParameterBindingTests
{
    [Fact]
    public async Task Scalar_composite_and_override_parameters_bind_to_at_placeholders()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(ParameterBindingRepo.OrdersDdl).ConfigureAwait(false);

        var repo = new ParameterBindingRepo(fx.Connection);
        var id = await repo.FirstMatchAsync(7, new Money(50m, "EUR"), excludedId: 1, CancellationToken.None)
            .ConfigureAwait(false);

        id.Should().Be(ParameterBindingRepo.ExpectedMatch);
    }

    [Fact]
    public async Task Unprefixed_parameters_bind_to_colon_placeholders()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(ParameterBindingRepo.OrdersDdl).ConfigureAwait(false);

        var repo = new ParameterBindingRepo(fx.Connection);
        var id = await repo.FirstForCustomerColonAsync(7, excludedId: 1, CancellationToken.None)
            .ConfigureAwait(false);

        id.Should().Be(2);
    }

    [Fact]
    public async Task Stored_procedure_binds_input_and_output_parameters_by_unprefixed_name()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(@"
            CREATE PROCEDURE scale_proc(IN amount integer, OUT doubled integer, OUT tripled integer)
                LANGUAGE plpgsql
            AS $$
            BEGIN
                doubled := amount * 2;
                tripled := amount * 3;
            END;
            $$;").ConfigureAwait(false);

        var repo = new PostgresParameterBindingProcedureRepo(fx.Connection);
        var (doubled, tripled) = await repo.ScaleAsync(21, 0, 0, CancellationToken.None).ConfigureAwait(false);

        doubled.Should().Be(42, "the input reached the procedure and the first OUT slot came back");
        tripled.Should().Be(63, "the second OUT slot is matched by name, not taken from the first");
    }
}
