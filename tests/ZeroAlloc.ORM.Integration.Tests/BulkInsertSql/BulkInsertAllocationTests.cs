using System.Data.Async;
using System.Text;
using AwesomeAssertions;
using Xunit;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Integration.Tests.BulkInsertSql;

// #326 — the emitted BulkInsert chunk loop must add no allocation of its own on
// top of what the provider needs: no SQL rebuilt per full chunk and no
// parameter name concatenated per row. The gate runs it against in-memory
// Sqlite next to a hand-written loop that makes the same provider calls with
// SQL and names built once up front, and fails if the emitted loop allocates
// more. 900 rows are two full chunks of 450, so the SQL a full chunk would
// rebuild and the 1,800 names it would concatenate are both in the measurement.
public class BulkInsertAllocationTests
{
    private const int ChunkSize = 450;
    private const int RowCount = 2 * ChunkSize;
    private const int Iterations = 20;

    // #351 — spelled as the SQL writes them, as the emitted loop binds them on Sqlite.
    private static readonly string[] CodeNames = Names("@Code_");
    private static readonly string[] LabelNames = Names("@Label_");
    private static readonly string FullChunkSql = BuildFullChunkSql();

    [Fact]
    public async Task Full_chunks_allocate_nothing_beyond_the_provider_calls()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync("CREATE TABLE Labels (Code TEXT NOT NULL, Label TEXT NOT NULL);").ConfigureAwait(false);

            var repo = new LabelBulkInsertRepo(fx.Connection);
            var rows = new LabelRow[RowCount];
            for (var i = 0; i < rows.Length; i++)
            {
                var n = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                rows[i] = new LabelRow("C" + n, "L" + n);
            }

            // Both complete synchronously on Sqlite; AllocationGate throws if not.
            var connection = fx.Connection;
            ValueTask<int> Baseline() => new(HandWrittenInsertAsync(connection, rows, CancellationToken.None));
            ValueTask<int> Emitted() => new(repo.InsertAsync(rows, CancellationToken.None));

            var baselineBytes = AllocationGate.MeasureBytesPerCallValueTask(Iterations, Baseline);
            var emittedBytes = AllocationGate.MeasureBytesPerCallValueTask(Iterations, Emitted);
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"{RowCount} rows: hand-written {baselineBytes} B/call, emitted {emittedBytes} B/call");

            AllocationGate.AssertNoMoreThanValueTask(
                Iterations, Baseline, Emitted, "BulkInsert of two full chunks");

            (await Emitted().ConfigureAwait(false)).Should().Be(RowCount);
        }
    }

    // The emitted chunk loop, with the SQL and the parameter names it binds
    // built once. Everything it allocates is the provider's.
    private static async Task<int> HandWrittenInsertAsync(
        IAsyncDbConnection connection, LabelRow[] rows, CancellationToken ct)
    {
        var total = 0;
        var offset = 0;
        var remaining = rows.Length;
        while (remaining > 0)
        {
            var thisChunk = remaining < ChunkSize ? remaining : ChunkSize;
            var cmd = connection.CreateCommand();
            await using (cmd.ConfigureAwait(false))
            {
                cmd.CommandText = FullChunkSql;
                for (var i = 0; i < thisChunk; i++)
                {
                    var row = rows[offset + i];
                    var code = cmd.CreateParameter();
                    code.ParameterName = CodeNames[i];
                    code.Value = row.Code;
                    cmd.Parameters.Add(code);
                    var label = cmd.CreateParameter();
                    label.ParameterName = LabelNames[i];
                    label.Value = row.Label;
                    cmd.Parameters.Add(label);
                }
                total += await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            offset += thisChunk;
            remaining -= thisChunk;
        }
        return total;
    }

    private static string[] Names(string prefix)
    {
        var names = new string[ChunkSize];
        for (var i = 0; i < names.Length; i++)
        {
            names[i] = prefix + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        return names;
    }

    private static string BuildFullChunkSql()
    {
        var sb = new StringBuilder("INSERT INTO Labels (Code, Label) VALUES ");
        for (var i = 0; i < ChunkSize; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append('(').Append(CodeNames[i]).Append(", ").Append(LabelNames[i]).Append(')');
        }
        return sb.ToString();
    }
}
