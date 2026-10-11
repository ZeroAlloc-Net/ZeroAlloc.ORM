using System.Data.Async;
using System.Data.Async.Adapters;
using AwesomeAssertions;
using Xunit;
using ZeroAlloc.ORM.Integration.Tests.TestDoubles;

namespace ZeroAlloc.ORM.Integration.Tests.Ordinals;

// #326 — a DomainEntity read in a loop resolves each column's ordinal once,
// before the first row, not once per row. The Sqlite cells prove the hoisted
// ordinals still read the right column, still read NULL into a nullable
// property, still name a NULL in a non-nullable one, and still handle a result
// with no rows. The counting cells run the same repository over a reader that
// counts GetOrdinal calls.
public class OrdinalHoistTests
{
    private static readonly string[] SelectOrder = ["Nickname", "Name", "Id"];
    private static readonly string[] CtorOrder = ["Id", "Name", "Nickname"];

    // Boxed once, so building a table boxes nothing per row.
    private static readonly object[] Ids = [1, 2, 3, 4, 5];

    private const string SeedFourCustomers = """
        INSERT INTO Customers (Id, Name, Nickname) VALUES
            (1, 'Ada', 'A'),
            (2, 'Brendan', NULL),
            (3, 'Cleo', 'C'),
            (4, 'Dmitri', NULL);
        """;

    [Fact]
    public async Task List_reads_every_row_by_column_name()
    {
        await WithSeededSqliteAsync(async repo =>
        {
            var customers = await repo.ListAsync(CancellationToken.None).ConfigureAwait(false);

            AssertSeeded(customers);
        });
    }

    [Fact]
    public async Task Stream_reads_every_row_by_column_name()
    {
        await WithSeededSqliteAsync(async repo =>
        {
            var customers = new List<NicknamedCustomer>();
            await foreach (var c in repo.StreamAsync(CancellationToken.None).ConfigureAwait(false))
            {
                customers.Add(c);
            }

            AssertSeeded(customers);
        });
    }

    [Fact]
    public async Task Multi_result_lists_with_shared_column_names_read_every_row()
    {
        await WithSeededSqliteAsync(async repo =>
        {
            var split = await repo.SplitAsync(2, CancellationToken.None).ConfigureAwait(false);

            split.Should().NotBeNull();
            AssertSeeded([.. split!.Value.Low, .. split.Value.High]);
            split.Value.Low.Should().HaveCount(2);
        });
    }

    [Fact]
    public async Task Zero_rows_read_as_empty_results()
    {
        await WithSqliteAsync(seedSql: null, async repo =>
        {
            (await repo.ListAsync(CancellationToken.None).ConfigureAwait(false)).Should().BeEmpty();

            var streamed = 0;
            await foreach (var _ in repo.StreamAsync(CancellationToken.None).ConfigureAwait(false))
            {
                streamed++;
            }
            streamed.Should().Be(0);

            var split = await repo.SplitAsync(2, CancellationToken.None).ConfigureAwait(false);
            split.Should().NotBeNull();
            split!.Value.Low.Should().BeEmpty();
            split.Value.High.Should().BeEmpty();
        });
    }

    [Fact]
    public async Task Null_in_a_non_nullable_column_is_still_named()
    {
        await WithSqliteAsync("INSERT INTO Customers (Id, Name, Nickname) VALUES (1, NULL, 'x');", async repo =>
        {
            var ex = await Assert.ThrowsAsync<ZeroAllocOrmMaterializationException>(
                () => repo.ListAsync(CancellationToken.None)).ConfigureAwait(false);

            ex.Message.Should().Contain("Column 'Name' is NULL");
        });
    }

    [Fact]
    public async Task List_resolves_each_ordinal_once_per_result()
    {
        var reader = new TableDataReader(Table(SelectOrder, rows: 4));
        var repo = Repo(reader);

        var customers = await repo.ListAsync(CancellationToken.None).ConfigureAwait(false);

        customers.Should().HaveCount(4);
        reader.GetOrdinalCalls.Should().Be(3);
    }

    [Fact]
    public async Task Stream_resolves_each_ordinal_once_per_result()
    {
        var reader = new TableDataReader(Table(SelectOrder, rows: 4));
        var repo = Repo(reader);

        var count = 0;
        await foreach (var _ in repo.StreamAsync(CancellationToken.None).ConfigureAwait(false))
        {
            count++;
        }

        count.Should().Be(4);
        reader.GetOrdinalCalls.Should().Be(3);
    }

    [Fact]
    public async Task Multi_result_lists_resolve_each_ordinal_once_per_result()
    {
        var reader = new TableDataReader(Table(SelectOrder, rows: 4), Table(CtorOrder, rows: 5));
        var repo = Repo(reader);

        var split = await repo.SplitAsync(4, CancellationToken.None).ConfigureAwait(false);

        split!.Value.Low.Should().HaveCount(4);
        split.Value.High.Should().HaveCount(5);
        reader.GetOrdinalCalls.Should().Be(6);
    }

    private static Task WithSeededSqliteAsync(Func<OrdinalHoistRepo, Task> body) => WithSqliteAsync(SeedFourCustomers, body);

    private static async Task WithSqliteAsync(string? seedSql, Func<OrdinalHoistRepo, Task> body)
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync("CREATE TABLE Customers (Id INTEGER PRIMARY KEY, Name TEXT NULL, Nickname TEXT NULL);").ConfigureAwait(false);
            if (seedSql is not null)
            {
                await fx.ExecuteDdlAsync(seedSql).ConfigureAwait(false);
            }
            await body(new OrdinalHoistRepo(fx.Connection)).ConfigureAwait(false);
        }
    }

    private static void AssertSeeded(IReadOnlyList<NicknamedCustomer> customers)
    {
        customers.Select(c => (c.Id, c.Name, c.Nickname)).Should().Equal(
            (1, "Ada", "A"),
            (2, "Brendan", null),
            (3, "Cleo", "C"),
            (4, "Dmitri", null));
    }

    private static OrdinalHoistRepo Repo(TableDataReader reader)
    {
#pragma warning disable CA2000 // Holds no resources; the test owns nothing to dispose.
        IAsyncDbConnection connection = new RecordingDbConnection(() => reader).AsAsync();
#pragma warning restore CA2000
        return new OrdinalHoistRepo(connection);
    }

    // Rows numbered 1..rows, every other one with a NULL nickname, laid out in
    // the given column order.
    private static TableResult Table(string[] columns, int rows)
    {
        var cells = new List<object[]>(rows);
        for (var i = 1; i <= rows; i++)
        {
            var row = new object[columns.Length];
            for (var c = 0; c < columns.Length; c++)
            {
                row[c] = columns[c] switch
                {
                    "Id" => Ids[i - 1],
                    "Name" => "N" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    _ => i % 2 == 0 ? DBNull.Value : "nick",
                };
            }
            cells.Add(row);
        }
        return new TableResult(columns, cells);
    }
}
