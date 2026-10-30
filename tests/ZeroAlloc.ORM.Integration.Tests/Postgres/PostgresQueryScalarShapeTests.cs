using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.Postgres;

// #400 — non-nullable scalars and lists of a scalar from [Query], and #410 enums and
// tuple scalar lists, on PostgreSQL.
[Trait("Provider", "Postgres")]
public sealed class PostgresQueryScalarShapeTests
{
    [Fact]
    public async Task Scalars_read_the_first_column()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(QueryScalarShapeAssertions.PostgresSchema).ConfigureAwait(false);
        await QueryScalarShapeAssertions.ScalarsReadTheFirstColumnAsync(new QueryScalarShapeRepo(fx.Connection)).ConfigureAwait(false);
    }

    [Fact]
    public async Task No_row_into_a_non_nullable_scalar_throws()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(QueryScalarShapeAssertions.PostgresSchema).ConfigureAwait(false);
        await QueryScalarShapeAssertions.NoRowThrowsAsync(new QueryScalarShapeRepo(fx.Connection)).ConfigureAwait(false);
    }

    [Fact]
    public async Task Null_into_a_non_nullable_scalar_throws()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(QueryScalarShapeAssertions.PostgresSchema).ConfigureAwait(false);
        await QueryScalarShapeAssertions.NullThrowsAsync(new QueryScalarShapeRepo(fx.Connection)).ConfigureAwait(false);
    }

    [Fact]
    public async Task Lists_read_every_row_in_order()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(QueryScalarShapeAssertions.PostgresSchema).ConfigureAwait(false);
        await QueryScalarShapeAssertions.ListsReadEveryRowInOrderAsync(new QueryScalarShapeRepo(fx.Connection)).ConfigureAwait(false);
    }

    [Fact]
    public async Task Empty_lists_come_back_empty()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(QueryScalarShapeAssertions.PostgresSchema).ConfigureAwait(false);
        await QueryScalarShapeAssertions.EmptyListsComeBackEmptyAsync(new QueryScalarShapeRepo(fx.Connection)).ConfigureAwait(false);
    }

    [Fact]
    public async Task Null_into_a_non_nullable_list_element_throws()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(QueryScalarShapeAssertions.PostgresSchema).ConfigureAwait(false);
        await QueryScalarShapeAssertions.NullIntoANonNullableElementThrowsAsync(new QueryScalarShapeRepo(fx.Connection)).ConfigureAwait(false);
    }

    [Fact]
    public async Task Nullable_list_elements_keep_nulls()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(QueryScalarShapeAssertions.PostgresSchema).ConfigureAwait(false);
        await QueryScalarShapeAssertions.NullableElementsKeepNullsAsync(new QueryScalarShapeRepo(fx.Connection)).ConfigureAwait(false);
    }

    [Fact]
    public async Task Enum_scalars_read_the_first_column()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(QueryScalarShapeAssertions.PostgresSchema).ConfigureAwait(false);
        await QueryScalarShapeAssertions.EnumScalarsReadTheFirstColumnAsync(new QueryScalarShapeRepo(fx.Connection)).ConfigureAwait(false);
    }

    [Fact]
    public async Task Non_nullable_enum_throws_on_no_row_and_NULL()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(QueryScalarShapeAssertions.PostgresSchema).ConfigureAwait(false);
        await QueryScalarShapeAssertions.NonNullableEnumThrowsOnNoRowAndNullAsync(new QueryScalarShapeRepo(fx.Connection)).ConfigureAwait(false);
    }

    [Fact]
    public async Task Enum_lists_read_every_row()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(QueryScalarShapeAssertions.PostgresSchema).ConfigureAwait(false);
        await QueryScalarShapeAssertions.EnumListsReadEveryRowAsync(new QueryScalarShapeRepo(fx.Connection)).ConfigureAwait(false);
    }

    [Fact]
    public async Task Tuple_scalar_lists_read_every_row()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(QueryScalarShapeAssertions.PostgresSchema).ConfigureAwait(false);
        await QueryScalarShapeAssertions.TupleScalarListsReadEveryRowAsync(new QueryScalarShapeRepo(fx.Connection)).ConfigureAwait(false);
    }

    [Fact]
    public async Task Null_into_a_non_nullable_tuple_list_element_throws()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(QueryScalarShapeAssertions.PostgresSchema).ConfigureAwait(false);
        await QueryScalarShapeAssertions.NullIntoANonNullableTupleListElementThrowsAsync(new QueryScalarShapeRepo(fx.Connection)).ConfigureAwait(false);
    }
}
