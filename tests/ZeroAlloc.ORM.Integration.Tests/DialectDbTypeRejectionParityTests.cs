using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Npgsql;
using Xunit;
using ZeroAlloc.ORM.Generator;
using ZeroAlloc.ORM.Integration.Tests.SqlServer;

namespace ZeroAlloc.ORM.Integration.Tests;

// #248 — ZAO015 reports a [Param(DbType)] override from a table of the pairs each dialect's
// provider rejects, DialectDbTypeRejections in the generator. The table was taken from a probe
// against live servers (#243). This test re-runs that probe against the providers this suite
// pins, so a provider upgrade that changes what it accepts fails here instead of turning ZAO015
// into a false positive or a silent miss.
//
// The probe binds each sample value of a type with each DbType member and the undefined value
// 999, and runs SELECT @p. A pair is rejected only when every sample throws; a value the
// provider converts or reinterprets counts as accepted.
public sealed class DialectDbTypeRejectionParityTests
{
    // The sample values per bound type, keyed like the table: by the reader method
    // PrimitiveCatalog maps the type to. They are the probe's values.
    private static readonly (string Reader, object[] Values)[] Samples =
    [
        ("GetInt32", [1, 0]),
        ("GetInt64", [1L, 0L]),
        ("GetInt16", [(short)1, (short)0]),
        ("GetByte", [(byte)1, (byte)0]),
        ("GetBoolean", [true, false]),
        ("GetDecimal", [1m, 1.5m]),
        ("GetDouble", [1d, 1.5d]),
        ("GetFloat", [1f, 1.5f]),
        ("GetString",
        [
            "abc", "1", "1.5", "true", "2024-01-02", "2024-01-02 03:04:05",
            "2024-01-02 03:04:05+00:00", "03:04:05", "11111111-2222-3333-4444-555555555555", "<a/>",
        ]),
        ("GetDateTime",
        [
            new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Unspecified),
            new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Local),
        ]),
        ("GetFieldValue<global::System.DateTimeOffset>",
        [
            new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)),
        ]),
        ("GetFieldValue<global::System.TimeSpan>", [new TimeSpan(3, 4, 5), TimeSpan.Zero]),
        ("GetGuid", [Guid.Parse("11111111-2222-3333-4444-555555555555")]),
        ("GetFieldValue<byte[]>", ["abc"u8.ToArray(), "0123456789abcdef"u8.ToArray()]),
    ];

    private static readonly int[] DbTypes = Enum.GetValues<DbType>()
        .Where(d => d != DbType.Object)
        .Select(d => (int)d)
        .Append(999)
        .ToArray();

    [Fact]
    public void Samples_cover_every_bound_type_in_the_table()
    {
        Assert.Equal(
            DialectDbTypeRejections.ProbedReaderMethods.OrderBy(r => r, StringComparer.Ordinal),
            Samples.Select(s => s.Reader).OrderBy(r => r, StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task SqlServer_rejects_exactly_the_table()
    {
        var fx = new SqlServerFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            var connectionString = fx.ConnectionString;
            await AssertParityAsync(SqlDialectModel.SqlServer, () => new SqlConnection(connectionString)).ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task PostgreSql_rejects_exactly_the_table()
    {
        var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await using (fx.ConfigureAwait(false))
        {
            var connectionString = fx.ConnectionString;
            await AssertParityAsync(SqlDialectModel.PostgreSql, () => new NpgsqlConnection(connectionString)).ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task Sqlite_rejects_nothing()
        => await AssertParityAsync(SqlDialectModel.Sqlite, () => new SqliteConnection("Data Source=:memory:")).ConfigureAwait(false);

    private static async Task AssertParityAsync(SqlDialectModel dialect, Func<DbConnection> connect)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            var mismatches = new List<string>();
            var connection = connect();
            try
            {
                await connection.OpenAsync().ConfigureAwait(false);
                foreach (var (reader, values) in Samples)
                {
                    foreach (var dbType in DbTypes)
                    {
                        var (rejected, evidence, reopened) = await ProbeAsync(connection, connect, values, dbType).ConfigureAwait(false);
                        connection = reopened;
                        var expected = DialectDbTypeRejections.Rejects(dialect, reader, dbType);
                        if (rejected != expected)
                        {
                            mismatches.Add(string.Create(
                                CultureInfo.InvariantCulture,
                                $"{reader} as {Name(dbType)}: table says {(expected ? "reject" : "accept")}, provider {evidence}"));
                        }
                    }
                }
            }
            finally
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }

            Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    // Binds each value in turn until one runs. A failed attempt can leave the connection
    // unusable, so it is replaced then.
    private static async Task<(bool Rejected, string Evidence, DbConnection Connection)> ProbeAsync(
        DbConnection connection, Func<DbConnection> connect, object[] values, int dbType)
    {
        string? firstError = null;
        foreach (var value in values)
        {
            try
            {
                var command = connection.CreateCommand();
                await using (command.ConfigureAwait(false))
                {
                    command.CommandText = "SELECT @p";
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = "@p";
                    parameter.DbType = (DbType)dbType;
                    parameter.Value = value;
                    if ((DbType)dbType is DbType.String or DbType.AnsiString or DbType.StringFixedLength
                        or DbType.AnsiStringFixedLength or DbType.Binary)
                    {
                        parameter.Size = 50;
                    }
                    command.Parameters.Add(parameter);
                    var result = await command.ExecuteScalarAsync().ConfigureAwait(false);
                    return (false, "accepted " + Show(value) + " -> " + Show(result), connection);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                firstError ??= "rejected " + Show(value) + ": " + ex.GetType().Name + ": " + ex.Message.Split('\n')[0].Trim();
                if (connection.State != ConnectionState.Open)
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                    connection = connect();
                    await connection.OpenAsync().ConfigureAwait(false);
                }
            }
        }
        return (true, firstError!, connection);
    }

    private static string Name(int dbType)
        => Enum.IsDefined((DbType)dbType) ? ((DbType)dbType).ToString() : dbType.ToString(CultureInfo.InvariantCulture);

    private static string Show(object? value)
        => value is null ? "null"
            : value is byte[] bytes ? "bytes[" + Convert.ToHexString(bytes) + "]"
            : value.GetType().Name + ":" + Convert.ToString(value, CultureInfo.InvariantCulture);
}
