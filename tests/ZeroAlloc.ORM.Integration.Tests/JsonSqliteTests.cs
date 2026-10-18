using AwesomeAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests;

public class JsonSqliteTests
{
    [Fact]
    public async Task Json_on_sqlite_binds_as_text()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync("CREATE TABLE documents (id INTEGER PRIMARY KEY, body TEXT NOT NULL, note TEXT NULL);")
                .ConfigureAwait(false);

            var repo = new JsonSqliteRepo(fx.Connection);
            (await repo.InsertAsync(1, "{\"a\": 1}", null, CancellationToken.None).ConfigureAwait(false)).Should().Be(1);
            (await repo.InsertAsync(2, "{\"a\": 2}", "[1, 2]", CancellationToken.None).ConfigureAwait(false)).Should().Be(1);

            var first = await repo.GetAsync(1, CancellationToken.None).ConfigureAwait(false);
            var second = await repo.GetAsync(2, CancellationToken.None).ConfigureAwait(false);

            first.Should().Be(new SqliteDocumentRow(1, "{\"a\": 1}", null));
            second.Should().Be(new SqliteDocumentRow(2, "{\"a\": 2}", "[1, 2]"));
        }
    }
}
