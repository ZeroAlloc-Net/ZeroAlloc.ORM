using System.Data.Async;
using System.Data.Async.Adapters;
using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Xunit;
using ZeroAlloc.ORM.Integration.Tests.TestDoubles;

namespace ZeroAlloc.ORM.Integration.Tests.BulkInsertSql;

// #326 — characterization of the SQL text and parameter names the emitted
// BulkInsert chunk loop hands the provider, for every SqlDialect. The chunk
// SQL is `<head>(@A_0, @B_0), (@A_1, @B_1), ...<tail>`, where head and tail
// are the author's SQL around the VALUES tuple, and each DbParameter is named
// `A_<row>` with no sigil. Caching the full-chunk SQL and the parameter names
// must leave every byte of both unchanged, so each case checks the recorded
// commands three ways: against a literal for a small batch, against a
// reference builder for every chunk boundary, and against a SHA-256 of the
// whole 1000-row run, captured from the emit before the cache existed.
public class BulkInsertSqlCharacterizationTests
{
    // 900 / 2 placeholders.
    private const int ChunkSize = 450;

    public static TheoryData<string> Methods() => new()
    {
        "SqlServer.Insert",
        "SqlServer.InsertReturningIds",
        "PostgreSql.Insert",
        "PostgreSql.InsertReturningIds",
        "Sqlite.Insert",
        "Sqlite.InsertReturningIds",
        "MySql.Insert",
    };

    public static TheoryData<string, int> MethodsByRowCount()
    {
        var data = new TheoryData<string, int>();
        foreach (var row in Methods())
        {
            foreach (var rows in new[] { 1, 2, 449, 450, 451, 900, 1000 })
            {
                data.Add(row.Data, rows);
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(MethodsByRowCount))]
    public async Task Every_chunk_matches_the_reference_SQL_and_parameter_names(string method, int rowCount)
    {
        var commands = await RunAsync(method, rowCount);

        var expectedChunks = new List<int>();
        for (var remaining = rowCount; remaining > 0; remaining -= ChunkSize)
        {
            expectedChunks.Add(Math.Min(remaining, ChunkSize));
        }

        commands.Should().HaveCount(expectedChunks.Count);
        var (head, tail) = SplitTemplate(Template(method));
        for (var c = 0; c < commands.Count; c++)
        {
            commands[c].CommandText.Should().Be(ReferenceSql(head, tail, expectedChunks[c]));
            commands[c].ParameterNames.Should().Equal(ReferenceNames(expectedChunks[c]));
        }
    }

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task A_two_row_batch_emits_the_literal_SQL(string method)
    {
        var commands = await RunAsync(method, 2);

        commands.Should().ContainSingle();
        commands[0].CommandText.Should().Be(TwoRowLiteral(method));
        commands[0].ParameterNames.Should().Equal("CustomerId_0", "Total_0", "CustomerId_1", "Total_1");
    }

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task A_1000_row_run_is_byte_identical_to_the_pre_cache_emit(string method)
    {
        var commands = await RunAsync(method, 1000);

        Fingerprint(commands).Should().Be(PreCacheFingerprint(method));
    }

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task Full_chunks_reuse_one_SQL_string(string method)
    {
        // Two calls of 900 rows: four full chunks. Every one must hand the
        // provider the same string instance, not a rebuilt copy.
        var commands = await RunAsync(method, 900);
        commands.AddRange(await RunAsync(method, 900));

        commands.Should().HaveCount(4);
        foreach (var command in commands)
        {
            ReferenceEquals(command.CommandText, commands[0].CommandText).Should().BeTrue();
        }
    }

    private static async Task<List<RecordedCommand>> RunAsync(string method, int rowCount)
    {
#pragma warning disable CA2000 // Owns nothing; the async wrapper is disposed below.
        var raw = new RecordingDbConnection(placeholdersPerRow: 2);
#pragma warning restore CA2000
        var connection = raw.AsAsync();
        await using (connection.ConfigureAwait(false))
        {
            var rows = new BulkOrderRow[rowCount];
            for (var i = 0; i < rows.Length; i++)
            {
                rows[i] = new BulkOrderRow(i + 1, (i + 1) * 0.01m);
            }

            var result = await InvokeAsync(method, connection, rows).ConfigureAwait(false);
            result.Should().Be(rowCount);
            return raw.Commands;
        }
    }

    private static async Task<int> InvokeAsync(string method, IAsyncDbConnection connection, BulkOrderRow[] rows)
    {
        var ct = CancellationToken.None;
        return method switch
        {
            "SqlServer.Insert" => await new SqlServerDialectBulkInsertRepo(connection).InsertAsync(rows, ct).ConfigureAwait(false),
            "SqlServer.InsertReturningIds" => (await new SqlServerDialectBulkInsertRepo(connection).InsertReturningIdsAsync(rows, ct).ConfigureAwait(false)).Count,
            "PostgreSql.Insert" => await new PostgresDialectBulkInsertRepo(connection).InsertAsync(rows, ct).ConfigureAwait(false),
            "PostgreSql.InsertReturningIds" => (await new PostgresDialectBulkInsertRepo(connection).InsertReturningIdsAsync(rows, ct).ConfigureAwait(false)).Count,
            "Sqlite.Insert" => await new SqliteDialectBulkInsertRepo(connection).InsertAsync(rows, ct).ConfigureAwait(false),
            "Sqlite.InsertReturningIds" => (await new SqliteDialectBulkInsertRepo(connection).InsertReturningIdsAsync(rows, ct).ConfigureAwait(false)).Count,
            "MySql.Insert" => await new MySqlDialectBulkInsertRepo(connection).InsertAsync(rows, ct).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, null),
        };
    }

    // The author's SQL, as written on each repository method.
    private static string Template(string method) => method switch
    {
        "SqlServer.Insert" => "INSERT INTO Orders (CustomerId, Total) VALUES (@CustomerId, @Total)",
        "SqlServer.InsertReturningIds" => "INSERT INTO Orders (CustomerId, Total) OUTPUT INSERTED.Id VALUES (@CustomerId, @Total)",
        "PostgreSql.Insert" => "INSERT INTO orders (customer_id, total) VALUES (@CustomerId, @Total)",
        "PostgreSql.InsertReturningIds" => "INSERT INTO orders (customer_id, total) VALUES (@CustomerId, @Total) RETURNING id",
        "Sqlite.Insert" => "INSERT INTO Orders (CustomerId, Total) VALUES (@CustomerId, @Total)",
        "Sqlite.InsertReturningIds" => "INSERT INTO Orders (CustomerId, Total) VALUES (@CustomerId, @Total) RETURNING Id",
        "MySql.Insert" => "INSERT INTO `Orders` (`CustomerId`, `Total`) values (@CustomerId, @Total) ON DUPLICATE KEY UPDATE `Total` = `Total` + 1",
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, null),
    };

    private static string TwoRowLiteral(string method) => method switch
    {
        "SqlServer.Insert" => "INSERT INTO Orders (CustomerId, Total) VALUES (@CustomerId_0, @Total_0), (@CustomerId_1, @Total_1)",
        "SqlServer.InsertReturningIds" => "INSERT INTO Orders (CustomerId, Total) OUTPUT INSERTED.Id VALUES (@CustomerId_0, @Total_0), (@CustomerId_1, @Total_1)",
        "PostgreSql.Insert" => "INSERT INTO orders (customer_id, total) VALUES (@CustomerId_0, @Total_0), (@CustomerId_1, @Total_1)",
        "PostgreSql.InsertReturningIds" => "INSERT INTO orders (customer_id, total) VALUES (@CustomerId_0, @Total_0), (@CustomerId_1, @Total_1) RETURNING id",
        "Sqlite.Insert" => "INSERT INTO Orders (CustomerId, Total) VALUES (@CustomerId_0, @Total_0), (@CustomerId_1, @Total_1)",
        "Sqlite.InsertReturningIds" => "INSERT INTO Orders (CustomerId, Total) VALUES (@CustomerId_0, @Total_0), (@CustomerId_1, @Total_1) RETURNING Id",
        "MySql.Insert" => "INSERT INTO `Orders` (`CustomerId`, `Total`) values (@CustomerId_0, @Total_0), (@CustomerId_1, @Total_1) ON DUPLICATE KEY UPDATE `Total` = `Total` + 1",
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, null),
    };

    // SHA-256 over every recorded command of a 1000-row call, captured at
    // 332d606, before the full-chunk SQL and parameter names were cached.
    // SqlServer.Insert and Sqlite.Insert share their SQL, so they share a value.
    private static string PreCacheFingerprint(string method) => method switch
    {
        "SqlServer.Insert" => "A676A1FC628F333F54BC852E2694A1F224BBEA78ED99BD8859581A66A3AC0CF8",
        "SqlServer.InsertReturningIds" => "C53662CB4223404C621CA3E78C5E4EEF6300A9DB11827E15BE8770F06D20722D",
        "PostgreSql.Insert" => "416E9EC009820AB21008B90C595ABD05454311D2B5C69B5E0F13BBA907DB63D1",
        "PostgreSql.InsertReturningIds" => "A02AC6300D5E2BD35603ED65E49197531B8359D668F5AC12CCCC701E4396C9BB",
        "Sqlite.Insert" => "A676A1FC628F333F54BC852E2694A1F224BBEA78ED99BD8859581A66A3AC0CF8",
        "Sqlite.InsertReturningIds" => "7EF66E9AF6E7FE30D82E21BD2DCB0A84104DF9C96AFD0589A193F0D3CE49DA09",
        "MySql.Insert" => "15CF3F049B6AA159A7991D797BA5E51D55ADBA830E03C30A40D27BDE8ADA175F",
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, null),
    };

    private static (string Head, string Tail) SplitTemplate(string template)
    {
        const string tuple = "(@CustomerId, @Total)";
        var at = template.IndexOf(tuple, StringComparison.Ordinal);
        return (template[..at], template[(at + tuple.Length)..]);
    }

    private static string ReferenceSql(string head, string tail, int rows)
    {
        var sb = new StringBuilder(head);
        for (var i = 0; i < rows; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append("(@CustomerId_").Append(i.ToString(System.Globalization.CultureInfo.InvariantCulture))
              .Append(", @Total_").Append(i.ToString(System.Globalization.CultureInfo.InvariantCulture))
              .Append(')');
        }
        return sb.Append(tail).ToString();
    }

    private static List<string> ReferenceNames(int rows)
    {
        var names = new List<string>(rows * 2);
        for (var i = 0; i < rows; i++)
        {
            names.Add("CustomerId_" + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            names.Add("Total_" + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        return names;
    }

    private static string Fingerprint(List<RecordedCommand> commands)
    {
        var sb = new StringBuilder();
        foreach (var command in commands)
        {
            sb.Append(command.CommandText).Append('\n');
            sb.AppendJoin('\u001f', command.ParameterNames).Append('\n');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }
}
