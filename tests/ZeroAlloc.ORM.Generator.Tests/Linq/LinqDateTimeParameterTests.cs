using System;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Xunit;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Generator.Tests.Linq;

// A DateTime parameter compared with, or set into, a column whose map declares its storage binds
// with that type explicitly: NpgsqlDbType.Timestamp for a timestamp column, TimestampTz for a
// timestamptz one, as EF Core's Npgsql provider binds it. Npgsql then rejects a value whose Kind
// does not fit, rather than PostgreSQL shifting it through the session's TimeZone. A column whose
// storage is Default keeps the type Npgsql infers from the Kind. A parameter that meets a
// timestamp column and a timestamptz one cannot bind both ways and is ZAO102.
public class LinqDateTimeParameterTests
{
    private const string Usings = """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using System.Data.Async;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.ORM;
        using ZeroAlloc.ORM.Linq;

        """;

    private const string Entities = """
        namespace TestApp;

        public sealed class Reading
        {
            public Guid Id { get; private set; }
            public Guid SensorId { get; private set; }
            public DateTime TakenAt { get; private set; }
            public DateTime RecordedAt { get; private set; }
            public DateTime SeenAt { get; private set; }
            public DateTime? ClosedAt { get; private set; }
        }

        public sealed class Sensor
        {
            public Guid Id { get; private set; }
            public DateTime InstalledAt { get; private set; }
        }

        public sealed record ReadingLine(Guid Id, DateTime? At);

        [TableMap(typeof(Sensor), "Sensors")]
        [ColumnMap(nameof(Sensor.InstalledAt), DateTimeStorage = DateTimeStorage.TimestampTz)]
        internal static class SensorMap;

        """;

    // TakenAt is a timestamp, RecordedAt and ClosedAt are timestamptz, SeenAt declares nothing.
    private const string ReadingMap = """
        [TableMap(typeof(Reading), "Readings")]
        [ColumnMap(nameof(Reading.TakenAt), DateTimeStorage = DateTimeStorage.Timestamp)]
        [ColumnMap(nameof(Reading.RecordedAt), DateTimeStorage = DateTimeStorage.TimestampTz)]
        [ColumnMap(nameof(Reading.ClosedAt), DateTimeStorage = DateTimeStorage.TimestampTz)]
        internal static class ReadingMap;

        [Dialect(SqlDialect.PostgreSql)]
        public sealed partial class ReadingStore(IAsyncDbConnection connection)
        {

        """;

    private const string Prelude = Usings + Entities + ReadingMap;

    private static string Binding(string parameter, string npgsqlDbType)
        => "__np_" + parameter + ".NpgsqlDbType = global::NpgsqlTypes.NpgsqlDbType." + npgsqlDbType + ";";

    [Fact]
    public void Query_parameter_compared_with_a_timestamp_column_binds_timestamp()
    {
        VerifyCompiled(Prelude + """
            [LinqQuery(nameof(SinceQuery))]
            public partial Task<List<ReadingLine>> Since(DateTime since, CancellationToken ct);

            private static IQuery<ReadingLine> SinceQuery(DateTime since) =>
                Sql.Table<Reading>().Where(r => r.TakenAt >= since).Select(r => new ReadingLine(r.Id, r.TakenAt));
            }
            """,
            "WHERE \"TakenAt\" >= @since",
            Binding("since", "Timestamp"));
    }

    [Fact]
    public void Query_parameter_compared_with_a_timestamptz_column_binds_timestamptz()
    {
        VerifyCompiled(Prelude + """
            [LinqQuery(nameof(BeforeQuery))]
            public partial Task<List<ReadingLine>> Before(DateTime until, CancellationToken ct);

            private static IQuery<ReadingLine> BeforeQuery(DateTime until) =>
                Sql.Table<Reading>().Where(r => until > r.RecordedAt).Select(r => new ReadingLine(r.Id, r.RecordedAt));
            }
            """,
            "WHERE @until > \"RecordedAt\"",
            Binding("until", "TimestampTz"));
    }

    // The assembly default reaches every DateTime column that declares nothing of its own.
    [Fact]
    public void Assembly_default_timestamptz_binds_timestamptz()
    {
        VerifyCompiled(Usings + """
            [assembly: EntityMapDefaults(DateTimeStorage = DateTimeStorage.TimestampTz)]

            """ + Entities + ReadingMap + """
            [LinqQuery(nameof(SeenQuery))]
            public partial Task<List<ReadingLine>> Seen(DateTime since, CancellationToken ct);

            private static IQuery<ReadingLine> SeenQuery(DateTime since) =>
                Sql.Table<Reading>().Where(r => r.SeenAt >= since).Select(r => new ReadingLine(r.Id, r.SeenAt));
            }
            """,
            "WHERE \"SeenAt\" >= @since",
            Binding("since", "TimestampTz"));
    }

    // Default storage: Npgsql keeps picking timestamp or timestamptz from the value's Kind.
    [Fact]
    public void Default_storage_column_keeps_the_inferred_type()
    {
        var (result, _) = GeneratorHarness.RunGeneratorAndGetCompilation(Prelude + """
            [LinqQuery(nameof(SeenQuery))]
            public partial Task<List<ReadingLine>> Seen(DateTime since, CancellationToken ct);

            private static IQuery<ReadingLine> SeenQuery(DateTime since) =>
                Sql.Table<Reading>().Where(r => r.SeenAt >= since).Select(r => new ReadingLine(r.Id, r.SeenAt));
            }
            """, NpgsqlStub.Source);

        Assert.Empty(result.Results[0].Diagnostics);
        Assert.DoesNotContain("NpgsqlDbType", Generated(result), StringComparison.Ordinal);
    }

    // A nullable parameter's null test casts it to the type it binds with, here a timestamp, not
    // the timestamptz an untyped DateTime gets.
    [Fact]
    public void Nullable_parameter_null_test_casts_to_the_column_storage()
    {
        VerifyCompiled(Prelude + """
            [LinqQuery(nameof(UpToQuery))]
            public partial Task<List<ReadingLine>> UpTo(DateTime? until, CancellationToken ct);

            private static IQuery<ReadingLine> UpToQuery(DateTime? until) =>
                Sql.Table<Reading>().Where(r => until == null || r.TakenAt < until).Select(r => new ReadingLine(r.Id, r.TakenAt));
            }
            """,
            "@until::timestamp IS NULL",
            Binding("until", "Timestamp"));
    }

    // Projected, the parameter is cast to the same type, so it reads back as it was sent.
    [Fact]
    public void Projected_parameter_casts_to_the_column_storage()
    {
        VerifyCompiled(Prelude + """
            [LinqQuery(nameof(StampQuery))]
            public partial Task<List<ReadingLine>> Stamp(DateTime? at, CancellationToken ct);

            private static IQuery<ReadingLine> StampQuery(DateTime? at) =>
                Sql.Table<Reading>().Where(r => r.TakenAt < at).Select(r => new ReadingLine(r.Id, at));
            }
            """,
            "SELECT \"Id\", @at::timestamp FROM",
            Binding("at", "Timestamp"));
    }

    // Set into ClosedAt and compared with RecordedAt, both timestamptz: one type for both.
    [Fact]
    public void Update_parameter_set_into_and_compared_with_timestamptz_columns_binds_timestamptz()
    {
        VerifyCompiled(Prelude + """
            [LinqCommand(nameof(CloseQuery))]
            public partial Task<int> Close(DateTime at, CancellationToken ct);

            private static int CloseQuery(DateTime at) =>
                Sql.Table<Reading>().Where(r => r.RecordedAt < at).ExecuteUpdate(set => set.SetProperty(r => r.ClosedAt, at));
            }
            """,
            "UPDATE \"Readings\" SET \"ClosedAt\" = @at WHERE \"RecordedAt\" < @at",
            Binding("at", "TimestampTz"));
    }

    [Fact]
    public void Delete_condition_on_a_timestamp_column_binds_timestamp()
    {
        VerifyCompiled(Prelude + """
            [LinqCommand(nameof(PurgeQuery))]
            public partial Task<int> Purge(DateTime before, CancellationToken ct);

            private static int PurgeQuery(DateTime before) =>
                Sql.Table<Reading>().Where(r => r.TakenAt < before).ExecuteDelete();
            }
            """,
            "DELETE FROM \"Readings\" WHERE \"TakenAt\" < @before",
            Binding("before", "Timestamp"));
    }

    // Under a join the column's storage comes from its own source's map.
    [Fact]
    public void Join_condition_reads_the_joined_source_storage()
    {
        VerifyCompiled(Prelude + """
            [LinqQuery(nameof(InstalledQuery))]
            public partial Task<List<ReadingLine>> Installed(DateTime since, CancellationToken ct);

            private static IQuery<ReadingLine> InstalledQuery(DateTime since) =>
                Sql.Table<Reading>()
                    .Join(Sql.Table<Sensor>(), r => r.SensorId, s => s.Id, (r, s) => new { Reading = r, Sensor = s })
                    .Where(x => x.Sensor.InstalledAt > since)
                    .Select(x => new ReadingLine(x.Reading.Id, x.Reading.TakenAt));
            }
            """,
            "WHERE t1.\"InstalledAt\" > @since",
            Binding("since", "TimestampTz"));
    }

    // An IN subquery's condition resolves through the subquery's own source.
    [Fact]
    public void Subquery_condition_reads_the_subquery_source_storage()
    {
        VerifyCompiled(Prelude + """
            [LinqQuery(nameof(FromNewSensorsQuery))]
            public partial Task<List<ReadingLine>> FromNewSensors(DateTime since, CancellationToken ct);

            private static IQuery<ReadingLine> FromNewSensorsQuery(DateTime since) =>
                Sql.Table<Reading>()
                    .Where(r => Sql.Table<Sensor>().Where(s => s.InstalledAt > since).Select(s => s.Id).Contains(r.SensorId))
                    .Select(r => new ReadingLine(r.Id, r.TakenAt));
            }
            """,
            "\"InstalledAt\" > @since",
            Binding("since", "TimestampTz"));
    }

    // A keyset row pairs each column with its parameter.
    [Fact]
    public void Keyset_row_binds_each_parameter_with_its_column_storage()
    {
        VerifyCompiled(Prelude + """
            [LinqQuery(nameof(AfterQuery))]
            public partial Task<List<ReadingLine>> After(DateTime afterAt, Guid afterId, CancellationToken ct);

            private static IQuery<ReadingLine> AfterQuery(DateTime afterAt, Guid afterId) =>
                Sql.Table<Reading>()
                    .Where(r => Sql.Row(r.TakenAt, r.Id) > Sql.Row(afterAt, afterId))
                    .OrderBy(r => r.TakenAt)
                    .ThenBy(r => r.Id)
                    .Select(r => new ReadingLine(r.Id, r.TakenAt));
            }
            """,
            "(\"TakenAt\", \"Id\") > (@afterAt, @afterId)",
            Binding("afterAt", "Timestamp"));
    }

    // A column whose storage is Default takes the declared type as well as the inferred one.
    [Fact]
    public void Parameter_compared_with_a_timestamp_and_a_default_column_binds_timestamp()
    {
        VerifyCompiled(Prelude + """
            [LinqQuery(nameof(BothQuery))]
            public partial Task<List<ReadingLine>> Both(DateTime since, CancellationToken ct);

            private static IQuery<ReadingLine> BothQuery(DateTime since) =>
                Sql.Table<Reading>().Where(r => r.SeenAt >= since && r.TakenAt >= since).Select(r => new ReadingLine(r.Id, r.TakenAt));
            }
            """,
            "WHERE \"SeenAt\" >= @since AND \"TakenAt\" >= @since",
            Binding("since", "Timestamp"));
    }

    // A [Param] that writes a DbType keeps it.
    [Fact]
    public void Written_dbtype_wins_over_the_column_storage()
    {
        var (result, _) = GeneratorHarness.RunGeneratorAndGetCompilation(Prelude + """
            [LinqQuery(nameof(SinceQuery))]
            public partial Task<List<ReadingLine>> Since([Param(DbType = System.Data.DbType.DateTime2)] DateTime since, CancellationToken ct);

            private static IQuery<ReadingLine> SinceQuery(DateTime since) =>
                Sql.Table<Reading>().Where(r => r.RecordedAt >= since).Select(r => new ReadingLine(r.Id, r.TakenAt));
            }
            """, NpgsqlStub.Source);

        Assert.Empty(result.Results[0].Diagnostics);
        var generated = Generated(result);
        Assert.Contains("global::System.Data.DbType.DateTime2", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("NpgsqlDbType", generated, StringComparison.Ordinal);
    }

    // Reported at the first use of the second type, naming the type of the first.
    [Theory]
    [InlineData("Sql.Table<Reading>().Where(r => r.TakenAt >= at && r.RecordedAt >= at).Select(r => new ReadingLine(r.Id, r.TakenAt))", "timestamp", "timestamptz")]
    [InlineData("Sql.Table<Reading>().Where(r => at < r.RecordedAt || r.TakenAt == at).Select(r => new ReadingLine(r.Id, r.TakenAt))", "timestamptz", "timestamp")]
    [InlineData("Sql.Table<Reading>().Where(r => Sql.Table<Sensor>().Where(s => s.InstalledAt > at).Select(s => s.Id).Contains(r.SensorId) && r.TakenAt < at).Select(r => new ReadingLine(r.Id, r.TakenAt))", "timestamptz", "timestamp")]
    [InlineData("Sql.Table<Reading>().Join(Sql.Table<Sensor>(), r => r.SensorId, s => s.Id, (r, s) => new { Reading = r, Sensor = s }).Where(x => x.Reading.TakenAt > at && x.Sensor.InstalledAt > at).Select(x => new ReadingLine(x.Reading.Id, x.Reading.TakenAt))", "timestamp", "timestamptz")]
    public void Query_parameter_compared_with_timestamp_and_timestamptz_columns_reports_ZAO102(string query, string first, string second)
    {
        var (result, _) = GeneratorHarness.RunGeneratorAndGetCompilation(Prelude + """
            [LinqQuery(nameof(EitherQuery))]
            public partial Task<List<ReadingLine>> Either(DateTime at, CancellationToken ct);

            private static IQuery<ReadingLine> EitherQuery(DateTime at) =>
            """ + query + """
            ;
            }
            """, NpgsqlStub.Source);

        AssertConflict(result, "at", first, second);
    }

    // Set into a timestamptz column but compared with a timestamp one.
    [Fact]
    public void Update_parameter_set_into_timestamptz_and_compared_with_timestamp_reports_ZAO102()
    {
        var (result, _) = GeneratorHarness.RunGeneratorAndGetCompilation(Prelude + """
            [LinqCommand(nameof(CloseQuery))]
            public partial Task<int> Close(DateTime at, CancellationToken ct);

            private static int CloseQuery(DateTime at) =>
                Sql.Table<Reading>().Where(r => r.TakenAt < at).ExecuteUpdate(set => set.SetProperty(r => r.ClosedAt, at));
            }
            """, NpgsqlStub.Source);

        AssertConflict(result, "at", "timestamptz", "timestamp");
    }

    // The statements of a tuple companion bind one parameter alike, so they must agree too.
    [Fact]
    public void Tuple_statements_comparing_one_parameter_with_both_storages_report_ZAO102()
    {
        var (result, _) = GeneratorHarness.RunGeneratorAndGetCompilation(Prelude + """
            [LinqQuery(nameof(BothQuery))]
            public partial Task<(List<ReadingLine> Taken, List<ReadingLine> Recorded)> Both(DateTime at, CancellationToken ct);

            private static (IQuery<ReadingLine>, IQuery<ReadingLine>) BothQuery(DateTime at) =>
                (Sql.Table<Reading>().Where(r => r.TakenAt >= at).Select(r => new ReadingLine(r.Id, r.TakenAt)),
                 Sql.Table<Reading>().Where(r => r.RecordedAt >= at).Select(r => new ReadingLine(r.Id, r.RecordedAt)));
            }
            """, NpgsqlStub.Source);

        AssertConflict(result, "at", "timestamp", "timestamptz");
    }

    // Without Npgsql the explicit type has nothing to name it by: ZAO082, as for a jsonb one.
    [Fact]
    public void Timestamp_comparison_without_npgsql_reports_ZAO082()
    {
        var (result, _) = GeneratorHarness.RunGeneratorAndGetCompilation(Prelude + """
            [LinqQuery(nameof(SinceQuery))]
            public partial Task<List<ReadingLine>> Since(DateTime since, CancellationToken ct);

            private static IQuery<ReadingLine> SinceQuery(DateTime since) =>
                Sql.Table<Reading>().Where(r => r.TakenAt >= since).Select(r => new ReadingLine(r.Id, r.TakenAt));
            }
            """);

        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Equal("ZAO082", diagnostic.Id);
        Assert.Contains("'since'", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Contains("timestamp", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Empty(result.GeneratedTrees);
    }

    private static void AssertConflict(GeneratorDriverRunResult result, string parameter, string first, string second)
    {
        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Equal("ZAO102", diagnostic.Id);
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("Parameter '" + parameter + "', which binds as " + first + " for a " + first + " column", message, StringComparison.Ordinal);
        Assert.Contains("used against a " + second + " column as well", message, StringComparison.Ordinal);
        Assert.Empty(result.GeneratedTrees);
    }

    private static string Generated(GeneratorDriverRunResult result)
        => string.Concat(result.GeneratedTrees.Select(t => t.ToString())).Replace("\\\"", "\"", StringComparison.Ordinal);

    private static void VerifyCompiled(
        string source,
        string expectedSql,
        string expectedBinding,
        [CallerFilePath] string testFilePath = "",
        [CallerMemberName] string testMethod = "")
    {
        var (result, compilation) = GeneratorHarness.RunGeneratorAndGetCompilation(source, NpgsqlStub.Source);

        Assert.Empty(result.Results[0].Diagnostics);
        var generatedPaths = result.GeneratedTrees.Select(t => t.FilePath).ToHashSet(StringComparer.Ordinal);
        var problems = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error
                || (d.Severity == DiagnosticSeverity.Warning
                    && d.Location.SourceTree is { } tree
                    && generatedPaths.Contains(tree.FilePath)))
            .Select(d => d.Id + ": " + d.GetMessage(CultureInfo.InvariantCulture))
            .ToList();
        Assert.Empty(problems);

        var generated = Generated(result);
        Assert.Contains(expectedSql, generated, StringComparison.Ordinal);
        Assert.Contains(expectedBinding, generated, StringComparison.Ordinal);

        GeneratorSnapshot.Verify(result, testFilePath, testMethod);
    }
}
