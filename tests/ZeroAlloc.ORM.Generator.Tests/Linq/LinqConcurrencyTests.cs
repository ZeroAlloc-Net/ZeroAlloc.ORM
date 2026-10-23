using System;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Xunit;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Generator.Tests.Linq;

// Optimistic concurrency in a [LinqCommand]: a write guarded by the row's version, so it affects
// no rows once another writer changed the row, and ThrowOnNoRows turns that into a
// ZeroAllocOrmConcurrencyException. The version is PostgreSQL's xmin system column, named by
// Sql.Xmin(row) or by a uint property mapped to xmin, or a byte[] row-version column the
// application writes itself. xmin is read as a uint and compared, by == or != only, with a uint
// parameter, which binds as NpgsqlDbType.Xid.
public class LinqConcurrencyTests
{
    private const string Prelude = """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using System.Data.Async;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.ORM;
        using ZeroAlloc.ORM.Linq;

        namespace TestApp;

        public sealed class AgentSessionEntity
        {
            public Guid Id { get; private set; }
            public int TurnCount { get; private set; }
            public long TotalInputTokens { get; private set; }
            public DateTimeOffset LastActivityAt { get; private set; }
        }

        public sealed class TaskEntity
        {
            public Guid Id { get; private set; }
            public string Title { get; private set; } = "";
            public int Status { get; private set; }
            public uint RowVersion { get; private set; }
            public long Effort { get; private set; }
        }

        public sealed class DocumentEntity
        {
            public Guid Id { get; private set; }
            public string Body { get; private set; } = "";
            public byte[] Version { get; private set; } = [];
            public uint Generation { get; private set; }
        }

        public sealed record SessionVersion(Guid Id, int TurnCount, uint Version);

        public sealed record TaskVersion(Guid Id, uint RowVersion);

        [TableMap(typeof(AgentSessionEntity), "AgentSessions")]
        internal static class AgentSessionMap;

        [TableMap(typeof(TaskEntity), "Tasks")]
        [ColumnMap(nameof(TaskEntity.RowVersion), Column = "xmin")]
        internal static class TaskMap;

        [TableMap(typeof(DocumentEntity), "Documents")]
        internal static class DocumentMap;

        """;

    private const string Repo = """
        [Dialect(SqlDialect.PostgreSql)]
        public sealed partial class SessionStore(IAsyncDbConnection connection)
        {

        """;

    // PostgresAgentSessionStore: a turn applies only to the session as it was read. The xmin
    // compared is a parameter bound as xid, so the SQL needs no cast, and the method throws when
    // another writer changed the row in between.
    [Fact]
    public void Xmin_guarded_update()
    {
        VerifyCompiled(
            Repo + """
                [LinqCommand(nameof(RecordTurnQuery), ThrowOnNoRows = true)]
                public partial Task RecordTurn(Guid id, uint expected, DateTimeOffset now, CancellationToken ct);

                private static int RecordTurnQuery(Guid id, uint expected, DateTimeOffset now) =>
                    Sql.Table<AgentSessionEntity>()
                        .Where(s => s.Id == id && Sql.Xmin(s) == expected)
                        .ExecuteUpdate(set => set
                            .SetProperty(s => s.TurnCount, s => s.TurnCount + 1)
                            .SetProperty(s => s.LastActivityAt, now));
            }
            """,
            "UPDATE \"AgentSessions\" SET \"TurnCount\" = (\"TurnCount\" + 1), \"LastActivityAt\" = @now WHERE \"Id\" = @id AND \"xmin\" = @expected",
            "__np_expected.NpgsqlDbType = global::NpgsqlTypes.NpgsqlDbType.Xid;");
    }

    // The xmin a guarded write compares is read with the row, as a uint; a join qualifies it with
    // its source's alias.
    [Fact]
    public void Xmin_read_in_projection()
    {
        VerifyCompiled(
            Repo + """
                [LinqQuery(nameof(VersionQuery))]
                public partial Task<SessionVersion?> GetVersion(Guid id, CancellationToken ct);

                [LinqQuery(nameof(JoinedQuery))]
                public partial Task<List<TaskVersion>> GetJoined(CancellationToken ct);

                private static IQuery<SessionVersion> VersionQuery(Guid id) =>
                    Sql.Table<AgentSessionEntity>()
                        .Where(s => s.Id == id)
                        .Select(s => new SessionVersion(s.Id, s.TurnCount, Sql.Xmin(s)));

                private static IQuery<TaskVersion> JoinedQuery() =>
                    Sql.Table<TaskEntity>()
                        .Join(Sql.Table<AgentSessionEntity>(), t => t.Id, s => s.Id, (t, s) => new { Task = t, Session = s })
                        .Select(x => new TaskVersion(x.Task.Id, Sql.Xmin(x.Session)));
            }
            """,
            "SELECT \"Id\", \"TurnCount\", \"xmin\" FROM \"AgentSessions\" WHERE \"Id\" = @id LIMIT 1",
            "SELECT t0.\"Id\", t1.\"xmin\" FROM \"Tasks\" AS t0 INNER JOIN \"AgentSessions\" AS t1 ON t0.\"Id\" = t1.\"Id\"",
            "GetFieldValue<uint>(2)");
    }

    // Daedalus' Task maps RowVersion to xmin, the way EF Core's Npgsql provider does. The property
    // is xmin exactly as Sql.Xmin(t) is: read as a uint, compared with a parameter bound as xid.
    [Fact]
    public void Mapped_xmin_property_compares_as_xid()
    {
        VerifyCompiled(
            Repo + """
                [LinqCommand(nameof(RenameQuery), ThrowOnNoRows = true)]
                public partial Task Rename(Guid id, uint rowVersion, string title, CancellationToken ct);

                [LinqQuery(nameof(TaskVersionQuery))]
                public partial Task<TaskVersion?> GetVersion(Guid id, CancellationToken ct);

                private static int RenameQuery(Guid id, uint rowVersion, string title) =>
                    Sql.Table<TaskEntity>()
                        .Where(t => t.Id == id && t.RowVersion == rowVersion)
                        .ExecuteUpdate(set => set.SetProperty(t => t.Title, title));

                private static IQuery<TaskVersion> TaskVersionQuery(Guid id) =>
                    Sql.Table<TaskEntity>()
                        .Where(t => t.Id == id)
                        .Select(t => new TaskVersion(t.Id, t.RowVersion));
            }
            """,
            "UPDATE \"Tasks\" SET \"Title\" = @title WHERE \"Id\" = @id AND \"xmin\" = @rowVersion",
            "SELECT \"Id\", \"xmin\" FROM \"Tasks\" WHERE \"Id\" = @id LIMIT 1",
            "__np_rowVersion.NpgsqlDbType = global::NpgsqlTypes.NpgsqlDbType.Xid;");
    }

    // Daedalus loads a task with its executions in one tuple, then renames the task guarded by
    // the RowVersion it read: each tuple element reads xmin as a uint, mapped or through Sql.Xmin.
    [Fact]
    public void Xmin_read_in_tuple_companion()
    {
        VerifyCompiled(
            Repo + """
                [LinqQuery(nameof(WithSessionsQuery))]
                public partial Task<(TaskVersion? Task, List<SessionVersion> Sessions)> WithSessions(Guid id, CancellationToken ct);

                private static (IQuery<TaskVersion>, IQuery<SessionVersion>) WithSessionsQuery(Guid id) =>
                    (Sql.Table<TaskEntity>().Where(t => t.Id == id).Select(t => new TaskVersion(t.Id, t.RowVersion)),
                     Sql.Table<AgentSessionEntity>().Where(s => s.Id == id).Select(s => new SessionVersion(s.Id, s.TurnCount, Sql.Xmin(s))));
            }
            """,
            "SELECT \"Id\", \"xmin\" FROM \"Tasks\" WHERE \"Id\" = @id LIMIT 1",
            "SELECT \"Id\", \"TurnCount\", \"xmin\" FROM \"AgentSessions\" WHERE \"Id\" = @id",
            "GetFieldValue<uint>(2)");
    }

    // xmin compares with an xid parameter in every condition: a WhereIf, the Where of a Contains
    // subquery, through Sql.Xmin or a mapped property, a guarded delete, and != for a row that
    // changed since it was read.
    [Fact]
    public void Xmin_guards_in_conditions_and_deletes()
    {
        VerifyCompiled(
            Repo + """
                [LinqCommand(nameof(DeleteIfQuery))]
                public partial Task<int> DeleteIf(bool check, uint expected, CancellationToken ct);

                [LinqCommand(nameof(DeleteTasksOfQuery))]
                public partial Task<int> DeleteTasksOf(uint expected, CancellationToken ct);

                [LinqCommand(nameof(DeleteSessionsOfQuery))]
                public partial Task<int> DeleteSessionsOf(uint rowVersion, CancellationToken ct);

                [LinqCommand(nameof(DeleteTaskQuery), ThrowOnNoRows = true)]
                public partial Task DeleteTask(Guid id, uint rowVersion, CancellationToken ct);

                [LinqQuery(nameof(ChangedQuery))]
                public partial Task<bool> Changed(Guid id, uint seen, CancellationToken ct);

                private static int DeleteIfQuery(bool check, uint expected) =>
                    Sql.Table<AgentSessionEntity>().WhereIf(check, s => Sql.Xmin(s) == expected).ExecuteDelete();

                private static int DeleteTasksOfQuery(uint expected) =>
                    Sql.Table<TaskEntity>()
                        .Where(t => Sql.Table<AgentSessionEntity>().Where(s => Sql.Xmin(s) == expected).Select(s => s.Id).Contains(t.Id))
                        .ExecuteDelete();

                private static int DeleteSessionsOfQuery(uint rowVersion) =>
                    Sql.Table<AgentSessionEntity>()
                        .Where(s => Sql.Table<TaskEntity>().Where(t => t.RowVersion == rowVersion).Select(t => t.Id).Contains(s.Id))
                        .ExecuteDelete();

                private static int DeleteTaskQuery(Guid id, uint rowVersion) =>
                    Sql.Table<TaskEntity>().Where(t => t.Id == id && t.RowVersion == rowVersion).ExecuteDelete();

                private static IQuery<bool> ChangedQuery(Guid id, uint seen) =>
                    Sql.Table<AgentSessionEntity>().Where(s => s.Id == id && Sql.Xmin(s) != seen).Any();
            }
            """,
            "DELETE FROM \"AgentSessions\" WHERE ((@check) IS NOT TRUE OR (\"xmin\" = @expected))",
            "DELETE FROM \"Tasks\" WHERE \"Id\" IN (SELECT \"Id\" FROM \"AgentSessions\" WHERE \"xmin\" = @expected)",
            "DELETE FROM \"AgentSessions\" WHERE \"Id\" IN (SELECT \"Id\" FROM \"Tasks\" WHERE \"xmin\" = @rowVersion)",
            "DELETE FROM \"Tasks\" WHERE \"Id\" = @id AND \"xmin\" = @rowVersion",
            "SELECT EXISTS (SELECT 1 FROM \"AgentSessions\" WHERE \"Id\" = @id AND \"xmin\" <> @seen)");
    }

    // A byte[] row version PostgreSQL does not generate: the guard compares it as bytea, and the
    // same update writes the next version the application chose.
    [Fact]
    public void Row_version_guarded_update_sets_next_version()
    {
        VerifyCompiled(
            Repo + """
                [LinqCommand(nameof(SaveQuery), ThrowOnNoRows = true)]
                public partial Task Save(Guid id, byte[] expected, byte[] next, string body, CancellationToken ct);

                private static int SaveQuery(Guid id, byte[] expected, byte[] next, string body) =>
                    Sql.Table<DocumentEntity>()
                        .Where(d => d.Id == id && d.Version == expected)
                        .ExecuteUpdate(set => set.SetProperty(d => d.Body, body).SetProperty(d => d.Version, next));
            }
            """,
            "UPDATE \"Documents\" SET \"Body\" = @body, \"Version\" = @next WHERE \"Id\" = @id AND \"Version\" = @expected",
            "throw new global::ZeroAlloc.ORM.ZeroAllocOrmConcurrencyException(\"'SessionStore.Save' affected no rows; the row was changed or removed since it was read\");");
    }

    // What xmin cannot be, through Sql.Xmin or a property mapped to it, each reported at the
    // construct: xmin is a transaction id, so it is compared for equality only, and PostgreSQL
    // sets it itself.
    [Theory]
    [InlineData(
        false,
        "Sql.Table<AgentSessionEntity>().OrderBy(s => Sql.Xmin(s)).Select(s => new SessionVersion(s.Id, s.TurnCount, Sql.Xmin(s)))",
        "Sql.Xmin(s)",
        "'Sql.Xmin(s)' in an ordering, since xmin is a transaction id, compared only for equality, is not supported in a [LinqQuery] chain")]
    [InlineData(
        true,
        "Sql.Table<AgentSessionEntity>().Where(s => Sql.Xmin(s) > expected).ExecuteDelete()",
        "Sql.Xmin(s) > expected",
        "'Sql.Xmin(s) > expected', since xmin is a transaction id, compared only for equality, is not supported in a [LinqCommand] chain")]
    [InlineData(
        true,
        "Sql.Table<AgentSessionEntity>().ExecuteUpdate(set => set.SetProperty(s => Sql.Xmin(s), expected))",
        "Sql.Xmin(s)",
        "'Sql.Xmin(s)' as a setter target, since xmin is a system column PostgreSQL sets itself, is not supported in a [LinqCommand] chain")]
    [InlineData(
        true,
        "Sql.Table<AgentSessionEntity>().ExecuteUpdate(set => set.SetProperty(s => s.TotalInputTokens, s => Sql.Xmin(s) + 1))",
        "Sql.Xmin(s) + 1",
        "'Sql.Xmin(s) + 1', since xmin is a transaction id, compared only for equality, is not supported in a [LinqCommand] chain")]
    [InlineData(
        true,
        "Sql.Table<AgentSessionEntity>().Where(s => Sql.Xmin(s) + 1 == expected).ExecuteDelete()",
        "Sql.Xmin(s) + 1",
        "'Sql.Xmin(s) + 1', since xmin is a transaction id, compared only for equality, is not supported in a [LinqCommand] chain")]
    [InlineData(
        true,
        "Sql.Table<AgentSessionEntity>().Where(s => Sql.Xmin(s) == 5u).ExecuteDelete()",
        "Sql.Xmin(s) == 5u",
        "'Sql.Xmin(s) == 5u', comparing xmin with something other than a uint parameter, is not supported in a [LinqCommand] chain")]
    [InlineData(
        true,
        "Sql.Table<AgentSessionEntity>().Where(s => Sql.Xmin(s) == expected).ExecuteUpdate(set => set.SetProperty(s => s.TotalInputTokens, expected))",
        "expected",
        "Parameter 'expected', a uint that binds as an xmin transaction id, used other than in a comparison with xmin, is not supported in a [LinqCommand] chain")]
    [InlineData(
        false,
        "Sql.Table<TaskEntity>().OrderBy(t => t.RowVersion).Select(t => new SessionVersion(t.Id, t.Status, t.RowVersion))",
        "t.RowVersion",
        "'RowVersion', whose column is xmin, outside a projection or an == or != test with a uint parameter, since xmin is a transaction id, compared only for equality, is not supported in a [LinqQuery] chain")]
    [InlineData(
        true,
        "Sql.Table<TaskEntity>().Where(t => t.RowVersion < expected).ExecuteDelete()",
        "t.RowVersion",
        "'RowVersion', whose column is xmin, outside a projection or an == or != test with a uint parameter, since xmin is a transaction id, compared only for equality, is not supported in a [LinqCommand] chain")]
    [InlineData(
        true,
        "Sql.Table<DocumentEntity>().Where(d => d.Generation == expected).ExecuteDelete()",
        "d.Generation",
        "'expected' is a uint parameter; uint binds only as xmin's transaction id, and 'Generation' is not xmin, so comparing them is not supported in a [LinqCommand] chain")]
    [InlineData(
        true,
        "Sql.Table<TaskEntity>().ExecuteUpdate(set => set.SetProperty(t => t.Effort, t => t.RowVersion + 1))",
        "t.RowVersion + 1",
        "'t.RowVersion + 1', since xmin is a transaction id, compared only for equality, is not supported in a [LinqCommand] chain")]
    [InlineData(
        true,
        "Sql.Table<TaskEntity>().Where(t => t.RowVersion + 1 == expected).ExecuteDelete()",
        "t.RowVersion + 1",
        "'t.RowVersion + 1', since xmin is a transaction id, compared only for equality, is not supported in a [LinqCommand] chain")]
    public void Xmin_reports_ZAO102(bool command, string chain, string located, string message)
    {
        var method = command
            ? """
                [LinqCommand(nameof(RunQuery))]
                public partial Task<int> Run(Guid id, uint expected, CancellationToken ct);

                private static int RunQuery(Guid id, uint expected) =>
            """
            : """
                [LinqQuery(nameof(RunQuery))]
                public partial Task<List<SessionVersion>> Run(Guid id, uint expected, CancellationToken ct);

                private static IQuery<SessionVersion> RunQuery(Guid id, uint expected) =>
            """;
        var (d, at) = Single(Repo + method + chain + """
            ;
            }
            """);

        Assert.Equal("ZAO102", d.Id);
        Assert.Equal(located, at);
        Assert.Equal(message, d.GetMessage(CultureInfo.InvariantCulture));
    }

    // xmin reads as a uint only: a projection into a wider parameter would read it through the
    // wrong getter.
    [Fact]
    public void Xmin_projected_into_another_type_reports_ZAO107()
    {
        var (d, at) = Single(Repo + """
                [LinqQuery(nameof(WideQuery))]
                public partial Task<List<WideVersion>> Run(CancellationToken ct);

                private static IQuery<WideVersion> WideQuery() =>
                    Sql.Table<AgentSessionEntity>().Select(s => new WideVersion(s.Id, Sql.Xmin(s)));
            }

            public sealed record WideVersion(Guid Id, long Version);
            """);

        Assert.Equal("ZAO107", d.Id);
        Assert.Equal("Sql.Xmin(s)", at);
        Assert.Equal(
            "Unsupported [LinqQuery] projection: 'Sql.Xmin(s)' reads xmin, a uint; declare the parameter 'uint'",
            d.GetMessage(CultureInfo.InvariantCulture));
    }

    // Only xmin reads as a uint: a uint property the map leaves on a column of its own is no row
    // version the projection can read, alone or as a tuple element, though a tuple companion
    // reads every element with the method-wide xmin reader.
    [Theory]
    [InlineData(
        "Task<List<TaskVersion>>",
        "IQuery<TaskVersion>",
        "Sql.Table<DocumentEntity>().Select(d => new TaskVersion(d.Id, d.Generation))")]
    [InlineData(
        "Task<(TaskVersion? Task, List<TaskVersion> Documents)>",
        "(IQuery<TaskVersion>, IQuery<TaskVersion>)",
        "(Sql.Table<TaskEntity>().Select(t => new TaskVersion(t.Id, t.RowVersion)), Sql.Table<DocumentEntity>().Select(d => new TaskVersion(d.Id, d.Generation)))")]
    public void Uint_column_other_than_xmin_projected_reports_ZAO107(string returns, string companion, string chain)
    {
        var (d, at) = Single(Repo + "    [LinqQuery(nameof(GenerationQuery))]\n    public partial " + returns
            + " Run(CancellationToken ct);\n\n    private static " + companion + " GenerationQuery() =>\n        " + chain + ";\n}\n");

        Assert.Equal("ZAO107", d.Id);
        Assert.Equal("d.Generation", at);
        Assert.Equal(
            "Unsupported [LinqQuery] projection: 'Generation' reads a uint, which only xmin reads as, but its column is Generation",
            d.GetMessage(CultureInfo.InvariantCulture));
    }

    // The xid binding names NpgsqlDbType, so a project without Npgsql cannot compile it.
    [Fact]
    public void Xid_parameter_without_npgsql_reports_ZAO082()
    {
        var (d, at) = Single(Repo + """
                [LinqCommand(nameof(RunQuery))]
                public partial Task<int> Run(Guid id, uint expected, CancellationToken ct);

                private static int RunQuery(Guid id, uint expected) =>
                    Sql.Table<AgentSessionEntity>().Where(s => s.Id == id && Sql.Xmin(s) == expected).ExecuteDelete();
            }
            """);

        Assert.Equal("ZAO082", d.Id);
        Assert.Equal("expected", at);
        Assert.Equal(
            "Parameter or column 'expected' uses an xid, which needs Npgsql. Reference the Npgsql package, or bind it another way.",
            d.GetMessage(CultureInfo.InvariantCulture));
    }

    // uint is no general-purpose parameter type: Npgsql would send it as an oid. Compared with an
    // int column it is rejected as before, by the [Query] path's binding check.
    [Fact]
    public void Uint_parameter_outside_xmin_stays_rejected()
    {
        var (result, _) = GeneratorHarness.RunGeneratorOnFile(Prelude + Repo + """
                [LinqCommand(nameof(RunQuery))]
                public partial Task<int> Run(uint count, CancellationToken ct);

                private static int RunQuery(uint count) =>
                    Sql.Table<AgentSessionEntity>().Where(s => s.TurnCount == count).ExecuteDelete();
            }
            """);

        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Equal("ZAO041", diagnostic.Id);
    }

    // A property mapped to xmin is the transaction id PostgreSQL keeps, a uint; EF Core's old
    // byte[] row version mapped there would read the wrong type.
    [Fact]
    public void Xmin_column_of_another_type_reports_ZAO106()
    {
        var (result, tree) = GeneratorHarness.RunGeneratorOnFile(Prelude + """
            public sealed class LegacyEntity
            {
                public Guid Id { get; private set; }
                public byte[] RowVersion { get; private set; } = [];
            }

            [TableMap(typeof(LegacyEntity), "Legacy")]
            [ColumnMap(nameof(LegacyEntity.RowVersion), Column = "xmin")]
            internal static class LegacyMap;
            """);

        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Equal("ZAO106", diagnostic.Id);
        var span = diagnostic.Location.SourceSpan;
        Assert.Equal("ColumnMap(nameof(LegacyEntity.RowVersion), Column = \"xmin\")", tree.ToString().Substring(span.Start, span.Length));
        Assert.Equal(
            "Invalid entity map: property 'RowVersion' of TestApp.LegacyEntity maps to xmin, PostgreSQL's transaction id, "
                + "so it must be a uint, not 'byte[]'",
            diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    // Runs the generator and returns its single diagnostic plus the source text it points at.
    private static (Diagnostic Diagnostic, string Located) Single(string body)
    {
        var (result, tree) = GeneratorHarness.RunGeneratorOnFile(Prelude + body);
        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Same(tree, diagnostic.Location.SourceTree);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        var span = diagnostic.Location.SourceSpan;
        return (diagnostic, tree.ToString().Substring(span.Start, span.Length));
    }

    // Each of `expected` is text the generated files must contain, checked before the snapshot.
    // Npgsql is present, so an xid parameter's binding compiles.
    private static void VerifyCompiled(
        string body,
        string expected1,
        string expected2,
        string? expected3 = null,
        string? expected4 = null,
        string? expected5 = null,
        [CallerFilePath] string testFilePath = "",
        [CallerMemberName] string testMethod = "")
    {
        var (result, compilation) = GeneratorHarness.RunGeneratorAndGetCompilation(Prelude + body, NpgsqlStub.Source);

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
        Assert.Contains(expected1, generated, StringComparison.Ordinal);
        Assert.Contains(expected2, generated, StringComparison.Ordinal);
        foreach (var expected in new[] { expected3, expected4, expected5 })
        {
            if (expected is not null) Assert.Contains(expected, generated, StringComparison.Ordinal);
        }

        GeneratorSnapshot.Verify(result, testFilePath, testMethod);
    }
}
