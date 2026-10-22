using System;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Xunit;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Generator.Tests.Linq;

// How a map declares a DateTime column's store type, and what `.Date` renders for it. A
// timestamptz column is cut at the UTC day, `date_trunc('day', col, 'UTC')`, whatever the
// session's TimeZone; a timestamp column, or one that declares nothing, keeps
// `date_trunc('day', col)`. A column's own ColumnMap setting wins over the assembly default.
public class LinqDateTimeStorageTests
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

        public sealed class Execution
        {
            public Guid Id { get; private set; }
            public Guid RunId { get; private set; }
            public DateTime ExecutedAt { get; private set; }
            public DateTime StartedAt { get; private set; }
        }

        public sealed class Run
        {
            public Guid Id { get; private set; }
            public DateTime StartedAt { get; private set; }
        }

        public sealed record DayCount(DateTime Day, int Count);

        public sealed record TwoDays(DateTime ExecutedOn, DateTime RunOn, int Count);

        [Dialect(SqlDialect.PostgreSql)]
        public sealed partial class ExecutionStore(IAsyncDbConnection connection)
        {
            [LinqQuery(nameof(ByExecutedDayQuery))]
            public partial Task<List<DayCount>> ByExecutedDay(CancellationToken ct);

            [LinqQuery(nameof(ByStartedDayQuery))]
            public partial Task<List<DayCount>> ByStartedDay(CancellationToken ct);

            private static IQuery<DayCount> ByExecutedDayQuery() =>
                Sql.Table<Execution>()
                    .GroupBy(e => e.ExecutedAt.Date)
                    .Select(g => new DayCount(g.Key, g.Count()));

            private static IQuery<DayCount> ByStartedDayQuery() =>
                Sql.Table<Execution>()
                    .GroupBy(e => e.StartedAt.Date)
                    .OrderBy(g => g.Key)
                    .Select(g => new DayCount(g.Key, g.Count()));
        }

        """;

    // The assembly default makes every DateTime column a timestamptz; StartedAt says it is a
    // timestamp, which wins.
    [Fact]
    public void Assembly_default_timestamptz_cuts_at_the_UTC_day_and_a_column_override_wins()
    {
        VerifyCompiled(Usings + """
            [assembly: EntityMapDefaults(DateTimeStorage = DateTimeStorage.TimestampTz)]

            """ + Entities + """
            [TableMap(typeof(Execution), "Executions")]
            [ColumnMap(nameof(Execution.StartedAt), DateTimeStorage = DateTimeStorage.Timestamp)]
            internal static class ExecutionMap;

            [TableMap(typeof(Run), "Runs")]
            internal static class RunMap;
            """,
            "date_trunc('day', \"ExecutedAt\", 'UTC')",
            "date_trunc('day', \"StartedAt\")");
    }

    // Without an assembly default, one column declares timestamptz; the other stays as it was.
    [Fact]
    public void Column_timestamptz_without_assembly_default_cuts_at_the_UTC_day()
    {
        VerifyCompiled(Usings + Entities + """
            [TableMap(typeof(Execution), "Executions")]
            [ColumnMap(nameof(Execution.ExecutedAt), DateTimeStorage = DateTimeStorage.TimestampTz)]
            internal static class ExecutionMap;

            [TableMap(typeof(Run), "Runs")]
            internal static class RunMap;
            """,
            "date_trunc('day', \"ExecutedAt\", 'UTC')",
            "date_trunc('day', \"StartedAt\")");
    }

    // Under a join each key reads its storage from its own source's map: Execution.StartedAt is
    // a timestamptz, Run.StartedAt of the same name is not.
    [Fact]
    public void Join_reads_each_key_storage_from_its_own_map()
    {
        VerifyCompiled(Usings + Entities + """
            [TableMap(typeof(Execution), "Executions")]
            [ColumnMap(nameof(Execution.StartedAt), DateTimeStorage = DateTimeStorage.TimestampTz)]
            internal static class ExecutionMap;

            [TableMap(typeof(Run), "Runs")]
            internal static class RunMap;

            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class RunStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(ByDaysQuery))]
                public partial Task<List<TwoDays>> ByDays(CancellationToken ct);

                private static IQuery<TwoDays> ByDaysQuery() =>
                    Sql.Table<Execution>()
                        .Join(Sql.Table<Run>(), e => e.RunId, r => r.Id, (e, r) => new { Execution = e, Run = r })
                        .GroupBy(x => new { Executed = x.Execution.StartedAt.Date, Ran = x.Run.StartedAt.Date })
                        .Select(g => new TwoDays(g.Key.Executed, g.Key.Ran, g.Count()));
            }
            """,
            "date_trunc('day', t0.\"StartedAt\", 'UTC')",
            "date_trunc('day', t1.\"StartedAt\")");
    }

    // `expected` and `alsoExpected` are SQL the generated files must contain, checked before the snapshot.
    private static void VerifyCompiled(
        string source,
        string expected,
        string alsoExpected,
        [CallerFilePath] string testFilePath = "",
        [CallerMemberName] string testMethod = "")
    {
        var (result, compilation) = GeneratorHarness.RunGeneratorAndGetCompilation(source);

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

        // The SQL sits in C# string literals, which escape its quoted identifiers.
        var generated = string.Concat(result.GeneratedTrees.Select(t => t.ToString()))
            .Replace("\\\"", "\"", StringComparison.Ordinal);
        Assert.Contains(expected, generated, StringComparison.Ordinal);
        Assert.Contains(alsoExpected, generated, StringComparison.Ordinal);

        GeneratorSnapshot.Verify(result, testFilePath, testMethod);
    }
}
