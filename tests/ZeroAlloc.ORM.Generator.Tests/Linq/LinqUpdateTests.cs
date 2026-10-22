using System;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Xunit;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Generator.Tests.Linq;

// [LinqCommand] with ExecuteUpdate: the companion's chain is Sql.Table<T>(), Where and WhereIf
// calls, then ExecuteUpdate(set => set.SetProperty(...)...), one UPDATE whose SET list follows
// the setters in written order. A setter's value is a companion parameter, a constant, a column
// of the row, or +, - and * over those on numbers. One snapshot per Daedalus store shape; every
// snapshot is also compiled, with no error anywhere and no warning in the emitted code.
public class LinqUpdateTests
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

        public enum SessionState { Idle, Running, Failed }

        public sealed class AgentSessionEntity
        {
            public Guid Id { get; private set; }
            public SessionState State { get; private set; }
            public SessionState PreviousState { get; private set; }
            public DateTimeOffset LastActivityAt { get; private set; }
            public DateTimeOffset? EndedAt { get; private set; }
            public int TurnCount { get; private set; }
            public long TotalInputTokens { get; private set; }
            public long TotalOutputTokens { get; private set; }
            public decimal Cost { get; private set; }
            public double Score { get; private set; }
            public string? Note { get; private set; }
            public string? FailureReason { get; private set; }
            public uint Version { get; private set; }
            public string Scratch { get; private set; } = "";
            public short Level { get; private set; }
            [Json]
            public string? Payload { get; private set; }
            [Json]
            public string? Snapshot { get; private set; }
        }

        public sealed class SkillEntity
        {
            public string Id { get; private set; } = "";
            public bool IsActive { get; private set; }
            public DateTimeOffset UpdatedAt { get; private set; }
        }

        public enum RepositoryPlatform { GitHub, GitLab }

        public sealed class RepositoryReference
        {
            public string Url { get; private set; } = "";
            public RepositoryPlatform Platform { get; private set; }
        }

        public sealed class AnalysisOutcome
        {
            public string? PullRequestUrl { get; private set; }
            [Json]
            public string? ValidationResult { get; private set; }
        }

        public sealed class CodeAnalysisRequest
        {
            public Guid Id { get; private set; }
            public RepositoryReference Repository { get; private set; } = null!;
            public AnalysisOutcome? Outcome { get; private set; }
        }

        [TableMap(typeof(AgentSessionEntity), "AgentSessions")]
        [ColumnMap(nameof(AgentSessionEntity.State), EnumAsString = true)]
        [ColumnMap(nameof(AgentSessionEntity.Version), Column = "xmin")]
        [ColumnMap(nameof(AgentSessionEntity.Scratch), Ignore = true)]
        internal static class AgentSessionMap;

        [TableMap(typeof(SkillEntity), "Skills", Schema = "agents")]
        internal static class SkillMap;

        [TableMap(typeof(CodeAnalysisRequest), "AnalysisRequests")]
        [OwnedMap(nameof(CodeAnalysisRequest.Repository))]
        [OwnedMap(nameof(CodeAnalysisRequest.Outcome))]
        [ColumnMap("Repository.Url", Column = "RepositoryUrl")]
        [ColumnMap("Repository.Platform", EnumAsString = true)]
        internal static class CodeAnalysisRequestMap;

        """;

    private const string Repo = """
        [Dialect(SqlDialect.PostgreSql)]
        public sealed partial class SessionStore(IAsyncDbConnection connection)
        {

        """;

    // AgentSessionCrashRecovery: every running session is marked failed at start-up. An enum
    // constant set into a column stored by name renders as the name, as in a comparison.
    [Fact]
    public void Update_state_and_timestamp()
    {
        VerifyCompiled(
            Repo + """
                [LinqCommand(nameof(FailRunningQuery))]
                public partial Task<int> FailRunning(DateTimeOffset now, CancellationToken ct);

                private static int FailRunningQuery(DateTimeOffset now) =>
                    Sql.Table<AgentSessionEntity>()
                        .Where(s => s.State == SessionState.Running)
                        .ExecuteUpdate(set => set
                            .SetProperty(s => s.State, SessionState.Failed)
                            .SetProperty(s => s.LastActivityAt, now));
            }
            """,
            "UPDATE \"AgentSessions\" SET \"State\" = 'Failed', \"LastActivityAt\" = @now WHERE \"State\" = 'Running'",
            "// LINQ: SessionStore.FailRunningQuery");
    }

    // PostgresSkillStore: skills no longer on disk are deactivated. The array parameter renders as
    // = ANY, and the update runs inside the caller's transaction.
    [Fact]
    public void Update_with_array_not_contains()
    {
        VerifyCompiled(
            Repo + """
                [LinqCommand(nameof(DeactivateMissingQuery))]
                public partial Task DeactivateMissing(string[] names, DateTimeOffset now, IAsyncDbTransaction tx, CancellationToken ct);

                private static int DeactivateMissingQuery(string[] names, DateTimeOffset now) =>
                    Sql.Table<SkillEntity>()
                        .Where(s => s.IsActive && !names.Contains(s.Id))
                        .ExecuteUpdate(set => set.SetProperty(s => s.IsActive, false).SetProperty(s => s.UpdatedAt, now));
            }
            """,
            "UPDATE \"agents\".\"Skills\" SET \"IsActive\" = FALSE, \"UpdatedAt\" = @now WHERE \"IsActive\" AND NOT (\"Id\" = ANY(@names))",
            "__cmd.Transaction = @tx;",
            NpgsqlStub.Source);
    }

    // PostgresAgentSessionStore: a turn adds its usage to the session's counters in one statement,
    // so concurrent turns lose no increment. An int parameter added to a long column is widened by
    // C#; the SQL adds it as it is, as PostgreSQL promotes it the same way.
    [Fact]
    public void Update_counters_with_arithmetic()
    {
        VerifyCompiled(
            Repo + """
                [LinqCommand(nameof(RecordTurnQuery))]
                public partial ValueTask<int> RecordTurn(
                    Guid id, int inputTokens, long outputTokens, decimal cost, decimal rate, double decay, DateTimeOffset now, CancellationToken ct);

                private static int RecordTurnQuery(
                    Guid id, int inputTokens, long outputTokens, decimal cost, decimal rate, double decay, DateTimeOffset now) =>
                    Sql.Table<AgentSessionEntity>()
                        .Where(s => s.Id == id)
                        .ExecuteUpdate(set => set
                            .SetProperty(s => s.TurnCount, s => s.TurnCount + 1)
                            .SetProperty(s => s.TotalInputTokens, s => s.TotalInputTokens + inputTokens)
                            .SetProperty(s => s.TotalOutputTokens, s => s.TotalOutputTokens + outputTokens - s.TurnCount)
                            .SetProperty(s => s.Cost, s => s.Cost + cost * rate)
                            .SetProperty(s => s.Score, s => s.Score * decay)
                            .SetProperty(s => s.LastActivityAt, now));
            }
            """,
            "UPDATE \"AgentSessions\" SET \"TurnCount\" = (\"TurnCount\" + 1), \"TotalInputTokens\" = (\"TotalInputTokens\" + @inputTokens), "
                + "\"TotalOutputTokens\" = ((\"TotalOutputTokens\" + @outputTokens) - \"TurnCount\"), \"Cost\" = (\"Cost\" + (@cost * @rate)), "
                + "\"Score\" = (\"Score\" * @decay), \"LastActivityAt\" = @now WHERE \"Id\" = @id",
            "public partial async global::System.Threading.Tasks.ValueTask<int> RecordTurn(");
    }

    // PostgresAgentSessionStore: a state change applies only while the session is still in the
    // state it was read in, and the caller reads the rows affected. Both enum parameters meet the
    // name-stored State column, so both bind by name.
    [Fact]
    public void Compare_and_swap_update()
    {
        VerifyCompiled(
            Repo + """
                [LinqCommand(nameof(TransitionQuery))]
                public partial Task<int> Transition(Guid id, SessionState from, SessionState to, DateTimeOffset now, CancellationToken ct);

                private static int TransitionQuery(Guid id, SessionState from, SessionState to, DateTimeOffset now) =>
                    Sql.Table<AgentSessionEntity>()
                        .Where(s => s.Id == id && s.State == from)
                        .ExecuteUpdate(set => set.SetProperty(s => s.State, to).SetProperty(s => s.LastActivityAt, now));
            }
            """,
            "UPDATE \"AgentSessions\" SET \"State\" = @to, \"LastActivityAt\" = @now WHERE \"Id\" = @id AND \"State\" = @from",
            "__p_to.Value = @to.ToString();");
    }

    // An owned member is set through the map, as plan 3 reads it: its renamed or default column.
    // The EnumAsString owned member binds its parameter by name, and the [Json] one as jsonb.
    [Fact]
    public void Update_owned_member()
    {
        VerifyCompiled(
            Repo + """
                [LinqCommand(nameof(RecordOutcomeQuery))]
                public partial Task<int> RecordOutcome(
                    Guid id, string url, string? pullRequestUrl, RepositoryPlatform platform, string? validation, CancellationToken ct);

                private static int RecordOutcomeQuery(
                    Guid id, string url, string? pullRequestUrl, RepositoryPlatform platform, string? validation) =>
                    Sql.Table<CodeAnalysisRequest>()
                        .Where(r => r.Id == id)
                        .ExecuteUpdate(set => set
                            .SetProperty(r => r.Repository.Url, url)
                            .SetProperty(r => r.Outcome!.PullRequestUrl, pullRequestUrl)
                            .SetProperty(r => r.Repository.Platform, platform)
                            .SetProperty(r => r.Outcome!.ValidationResult, validation));
            }
            """,
            "UPDATE \"AnalysisRequests\" SET \"RepositoryUrl\" = @url, \"Outcome_PullRequestUrl\" = @pullRequestUrl, "
                + "\"Repository_Platform\" = @platform, \"Outcome_ValidationResult\" = @validation WHERE \"Id\" = @id",
            "NpgsqlDbType = global::NpgsqlTypes.NpgsqlDbType.Jsonb;",
            NpgsqlStub.Source);
    }

    // A null constant renders NULL; a nullable parameter binds its null, typed by its facet.
    [Fact]
    public void Update_nullable_column_to_null()
    {
        VerifyCompiled(
            Repo + """
                [LinqCommand(nameof(ReopenQuery))]
                public partial Task<int> Reopen(Guid id, string? reason, CancellationToken ct);

                private static int ReopenQuery(Guid id, string? reason) =>
                    Sql.Table<AgentSessionEntity>()
                        .Where(s => s.Id == id)
                        .ExecuteUpdate(set => set
                            .SetProperty(s => s.EndedAt, (DateTimeOffset?)null)
                            .SetProperty(s => s.Note, (string?)null)
                            .SetProperty(s => s.FailureReason, reason));
            }
            """,
            "UPDATE \"AgentSessions\" SET \"EndedAt\" = NULL, \"Note\" = NULL, \"FailureReason\" = @reason WHERE \"Id\" = @id",
            "// LINQ: SessionStore.ReopenQuery");
    }

    // One enum parameter set into State and compared with State binds by name in both: the setter's
    // target column is a vote, as a comparison's column is.
    [Fact]
    public void Enum_parameter_in_setter_and_predicate_binds_by_name()
    {
        VerifyCompiled(
            Repo + """
                [LinqCommand(nameof(MoveQuery), ThrowOnNoRows = true)]
                public partial Task Move(Guid id, SessionState state, CancellationToken ct);

                private static int MoveQuery(Guid id, SessionState state) =>
                    Sql.Table<AgentSessionEntity>()
                        .Where(s => s.Id == id && s.State != state)
                        .ExecuteUpdate(set => set.SetProperty(s => s.State, state).SetProperty(s => s.PreviousState, SessionState.Idle));
            }
            """,
            "UPDATE \"AgentSessions\" SET \"State\" = @state, \"PreviousState\" = 0 WHERE \"Id\" = @id AND \"State\" <> @state",
            "__p_state.Value = @state.ToString();");
    }

    // A [Json] column takes another [Json] column, a constant or null as it is: PostgreSQL types
    // each from the target, and no parameter binds as jsonb.
    [Fact]
    public void Json_column_takes_a_json_column_or_a_constant()
    {
        var (result, _) = GeneratorHarness.RunGeneratorAndGetCompilation(Prelude + Repo + """
                [LinqCommand(nameof(CopyQuery))]
                public partial Task<int> Copy(CancellationToken ct);

                private static int CopyQuery() =>
                    Sql.Table<AgentSessionEntity>()
                        .ExecuteUpdate(set => set.SetProperty(s => s.Payload, s => s.Snapshot).SetProperty(s => s.Snapshot, "{}"));
            }
            """);

        Assert.Empty(result.Results[0].Diagnostics);
        var generated = string.Concat(result.GeneratedTrees.Select(t => t.ToString())).Replace("\\\"", "\"", StringComparison.Ordinal);
        Assert.Contains("UPDATE \"AgentSessions\" SET \"Payload\" = \"Snapshot\", \"Snapshot\" = '{}'", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("Jsonb", generated, StringComparison.Ordinal);
    }

    // One parameter set into two [Json] columns binds as jsonb for both: nothing is reported.
    [Fact]
    public void Json_parameter_in_two_json_setters_is_accepted()
    {
        var (result, _) = GeneratorHarness.RunGeneratorAndGetCompilation(Prelude + Repo + """
                [LinqCommand(nameof(StoreQuery))]
                public partial Task<int> Store(string? doc, CancellationToken ct);

                private static int StoreQuery(string? doc) =>
                    Sql.Table<AgentSessionEntity>()
                        .ExecuteUpdate(set => set.SetProperty(s => s.Payload, doc).SetProperty(s => s.Snapshot, doc));
            }
            """, NpgsqlStub.Source);

        Assert.Empty(result.Results[0].Diagnostics);
        var generated = string.Concat(result.GeneratedTrees.Select(t => t.ToString())).Replace("\\\"", "\"", StringComparison.Ordinal);
        Assert.Contains("UPDATE \"AgentSessions\" SET \"Payload\" = @doc, \"Snapshot\" = @doc", generated, StringComparison.Ordinal);
        Assert.Contains("NpgsqlDbType.Jsonb", generated, StringComparison.Ordinal);
    }

    // What a setter cannot be, each reported at the construct with a message that names it.
    [Theory]
    [InlineData(
        "Sql.Table<CodeAnalysisRequest>().ExecuteUpdate(set => set.SetProperty(r => r.Outcome, (AnalysisOutcome?)null))",
        "ZAO102",
        "r.Outcome",
        "Setting 'Outcome', a whole owned object rather than one of its columns, is not supported in a [LinqCommand] chain")]
    [InlineData(
        "Sql.Table<AgentSessionEntity>().ExecuteUpdate(set => set.SetProperty(s => s.Version, 1u))",
        "ZAO102",
        "s.Version",
        "Setting 'Version', whose column xmin is a system column PostgreSQL sets itself, is not supported in a [LinqCommand] chain")]
    [InlineData(
        "Sql.Table<AgentSessionEntity>().ExecuteUpdate(set => set.SetProperty(s => s.TurnCount, 1).SetProperty(s => s.Cost, 2m).SetProperty(s => s.TurnCount, 3))",
        "ZAO102",
        "s.TurnCount",
        "Setting 'TurnCount', whose column TurnCount an earlier SetProperty sets already, is not supported in a [LinqCommand] chain")]
    [InlineData(
        "Sql.Table<AgentSessionEntity>().ExecuteUpdate(set => set.SetProperty(s => s.Note, s => s.Note + \"!\"))",
        "ZAO102",
        "s.Note + \"!\"",
        "'s.Note + \"!\"' in a setter value, where +, - and * apply to int, long, decimal, double and float only, is not supported in a [LinqCommand] chain")]
    [InlineData(
        "Sql.Table<AgentSessionEntity>().ExecuteUpdate(set => set.SetProperty(s => s.TurnCount, s => s.TurnCount / 2))",
        "ZAO102",
        "s.TurnCount / 2",
        "'s.TurnCount / 2' in a setter value, where +, - and * apply to int, long, decimal, double and float only, is not supported in a [LinqCommand] chain")]
    [InlineData(
        "Sql.Table<AgentSessionEntity>().ExecuteUpdate(set => set.SetProperty(s => s.Note, s => s.FailureReason!.Trim()))",
        "ZAO102",
        "s.FailureReason!.Trim()",
        "'s.FailureReason!.Trim()' as a setter value, rather than a companion parameter, a constant, a column of the row, or +, - and * over those, is not supported in a [LinqCommand] chain")]
    [InlineData(
        "Sql.Table<AgentSessionEntity>().Where(s => s.TurnCount + 1 > 5).ExecuteUpdate(set => set.SetProperty(s => s.TurnCount, 0))",
        "ZAO102",
        "s.TurnCount + 1",
        "'s.TurnCount + 1' is not supported in a [LinqCommand] chain")]
    [InlineData(
        "Sql.Table<AgentSessionEntity>().ExecuteUpdate(set => set)",
        "ZAO102",
        "ExecuteUpdate",
        "'ExecuteUpdate' without a SetProperty is not supported in a [LinqCommand] chain")]
    [InlineData(
        "Sql.Table<AgentSessionEntity>().ExecuteUpdate(set => set.SetProperty(s => s.Scratch, \"x\"))",
        "ZAO105",
        "s.Scratch",
        "'Scratch' is not a mapped column of 'TestApp.AgentSessionEntity'")]
    [InlineData(
        "Sql.Table<AgentSessionEntity>().ExecuteUpdate(set => set.SetProperty(s => s.State, s => s.PreviousState))",
        "ZAO102",
        "s.PreviousState",
        "Setting 'State' from 'PreviousState', whose columns store the enum differently, one by name and one as an integer, is not supported in a [LinqCommand] chain")]
    [InlineData(
        "Sql.Table<AgentSessionEntity>().OrderBy(s => s.TurnCount).ExecuteUpdate(set => set.SetProperty(s => s.TurnCount, 0))",
        "ZAO102",
        "OrderBy",
        "'OrderBy' before 'ExecuteUpdate' is not supported in a [LinqCommand] chain")]
    [InlineData(
        "Sql.Table<AgentSessionEntity>().Where(s => s.PreviousState == state).ExecuteUpdate(set => set.SetProperty(s => s.State, state))",
        "ZAO111",
        "state",
        "Parameter 'state' is compared with or set into columns that store its enum differently")]
    [InlineData(
        "Sql.Table<AgentSessionEntity>().ExecuteUpdate(set => set.SetProperty(s => s.TurnCount, (int?)null))",
        "ZAO102",
        "s.TurnCount",
        "'s.TurnCount' as a setter target of type 'int?', a conversion of its column's 'int', is not supported in a [LinqCommand] chain")]
    [InlineData(
        "Sql.Table<AgentSessionEntity>().ExecuteUpdate(set => set.SetProperty(s => s.TurnCount, maybe))",
        "ZAO102",
        "s.TurnCount",
        "'s.TurnCount' as a setter target of type 'int?', a conversion of its column's 'int', is not supported in a [LinqCommand] chain")]
    [InlineData(
        "Sql.Table<AgentSessionEntity>().ExecuteUpdate(set => set.SetProperty(s => s.State, (SessionState?)null))",
        "ZAO102",
        "s.State",
        "'s.State' as a setter target of type 'TestApp.SessionState?', a conversion of its column's 'TestApp.SessionState', is not supported in a [LinqCommand] chain")]
    [InlineData(
        "Sql.Table<AgentSessionEntity>().ExecuteUpdate(set => set.SetProperty(s => s.TurnCount, wide))",
        "ZAO102",
        "s.TurnCount",
        "'s.TurnCount' as a setter target of type 'long', a conversion of its column's 'int', is not supported in a [LinqCommand] chain")]
    [InlineData(
        "Sql.Table<AgentSessionEntity>().ExecuteUpdate(set => set.SetProperty(s => s.TurnCount, s => (int)(s.TotalInputTokens + 1)))",
        "ZAO102",
        "(int)(s.TotalInputTokens + 1)",
        "'(int)(s.TotalInputTokens + 1)' as a setter value, rather than a companion parameter, a constant, a column of the row, or +, - and * over those, is not supported in a [LinqCommand] chain")]
    [InlineData(
        "Sql.Table<AgentSessionEntity>().ExecuteUpdate(set => set.SetProperty(s => s.TurnCount, s => s.TurnCount + 'a'))",
        "ZAO102",
        "s.TurnCount + 'a'",
        "'s.TurnCount + 'a'' in a setter value, where +, - and * apply to int, long, decimal, double and float only, is not supported in a [LinqCommand] chain")]
    [InlineData(
        "Sql.Table<AgentSessionEntity>().ExecuteUpdate(set => set.SetProperty(s => s.TotalInputTokens, s => 1u + s.TurnCount))",
        "ZAO102",
        "1u + s.TurnCount",
        "'1u + s.TurnCount' in a setter value, where +, - and * apply to int, long, decimal, double and float only, is not supported in a [LinqCommand] chain")]
    [InlineData(
        "Sql.Table<AgentSessionEntity>().ExecuteUpdate(set => set.SetProperty(s => s.TurnCount, s => s.TurnCount + s.Level))",
        "ZAO102",
        "s.TurnCount + s.Level",
        "'s.TurnCount + s.Level' in a setter value, where +, - and * apply to int, long, decimal, double and float only, is not supported in a [LinqCommand] chain")]
    [InlineData(
        "Sql.Table<AgentSessionEntity>().Where(s => s.Note == doc).ExecuteUpdate(set => set.SetProperty(s => s.Payload, doc))",
        "ZAO102",
        "doc",
        "Parameter 'doc', which binds as jsonb for the [Json] 'Payload', used in a condition or another setter as well, is not supported in a [LinqCommand] chain")]
    [InlineData(
        "Sql.Table<AgentSessionEntity>().ExecuteUpdate(set => set.SetProperty(s => s.Payload, doc).SetProperty(s => s.Note, doc))",
        "ZAO102",
        "doc",
        "Parameter 'doc', which binds as jsonb for the [Json] 'Payload', used in a condition or another setter as well, is not supported in a [LinqCommand] chain")]
    [InlineData(
        "Sql.Table<AgentSessionEntity>().ExecuteUpdate(set => set.SetProperty(s => s.Payload, s => s.Note))",
        "ZAO102",
        "s.Note",
        "'s.Note' set into the [Json] 'Payload', rather than a parameter, a constant, null or another [Json] column, is not supported in a [LinqCommand] chain")]
    [InlineData(
        "Sql.Table<AgentSessionEntity>().Where(s => Sql.Table<SkillEntity>().Where(k => k.Id == doc).Select(k => k.Id).Contains(s.Note!)).ExecuteUpdate(set => set.SetProperty(s => s.Payload, doc))",
        "ZAO102",
        "doc",
        "Parameter 'doc', which binds as jsonb for the [Json] 'Payload', used in a condition or another setter as well, is not supported in a [LinqCommand] chain")]
    [InlineData(
        "Sql.Table<AgentSessionEntity>().WhereIf(doc != null, s => s.Note == doc).ExecuteUpdate(set => set.SetProperty(s => s.Payload, doc))",
        "ZAO102",
        "doc",
        "Parameter 'doc', which binds as jsonb for the [Json] 'Payload', used in a condition or another setter as well, is not supported in a [LinqCommand] chain")]
    [InlineData(
        "Sql.Table<AgentSessionEntity>().Where(s => s.Snapshot == doc).ExecuteUpdate(set => set.SetProperty(s => s.Payload, doc))",
        "ZAO102",
        "doc",
        "Parameter 'doc', which binds as jsonb for the [Json] 'Payload', used in a condition or another setter as well, is not supported in a [LinqCommand] chain")]
    public void Setter_reports(string chain, string id, string located, string message)
    {
        var (d, at) = Single(Repo + """
                [LinqCommand(nameof(UpdateQuery))]
                public partial Task<int> Update(SessionState state, int? maybe, long wide, string? doc, CancellationToken ct);

                private static int UpdateQuery(SessionState state, int? maybe, long wide, string? doc) =>
            """ + chain + """
            ;
            }
            """);

        Assert.Equal(id, d.Id);
        Assert.Equal(located, at);
        Assert.Equal(message, d.GetMessage(CultureInfo.InvariantCulture));
    }

    // Runs the generator and returns its single diagnostic plus the source text it points at.
    private static (Diagnostic Diagnostic, string Located) Single(string body)
    {
        var (result, tree) = GeneratorHarness.RunGeneratorOnFile(Prelude + body);
        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Same(tree, diagnostic.Location.SourceTree);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Empty(result.Results[0].GeneratedSources);
        var span = diagnostic.Location.SourceSpan;
        return (diagnostic, tree.ToString().Substring(span.Start, span.Length));
    }

    // Each of `expected` is text the generated files must contain, checked before the snapshot.
    private static void VerifyCompiled(
        string body,
        string expected1,
        string expected2,
        string? additionalSource = null,
        [CallerFilePath] string testFilePath = "",
        [CallerMemberName] string testMethod = "")
    {
        var (result, compilation) = additionalSource is null
            ? GeneratorHarness.RunGeneratorAndGetCompilation(Prelude + body)
            : GeneratorHarness.RunGeneratorAndGetCompilation(Prelude + body, additionalSource);

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

        GeneratorSnapshot.Verify(result, testFilePath, testMethod);
    }
}
