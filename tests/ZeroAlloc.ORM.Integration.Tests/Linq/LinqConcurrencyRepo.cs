using System.Data.Async;
using ZeroAlloc.ORM.Linq;

namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// [LinqCommand] writes guarded by the row's version, the way Daedalus' stores use EF Core's
// concurrency tokens: PostgreSQL's xmin, through Sql.Xmin or a property mapped to it, and a
// bytea row version the application writes itself. ThrowOnNoRows turns a stale version into a
// ZeroAllocOrmConcurrencyException. Plus [Command] inserts to seed rows and [LinqQuery] reads.
[Dialect(SqlDialect.PostgreSql)]
internal sealed partial class LinqConcurrencyRepo(IAsyncDbConnection connection)
{
    [Command("""
        INSERT INTO "AgentSessions" ("Id", "State", "LastActivityAt", "TurnCount", "TotalInputTokens")
        VALUES (@id, 'Idle', now(), 0, 0)
        """)]
    public partial Task<int> InsertSessionAsync(Guid id, CancellationToken ct);

    [Command("""
        INSERT INTO "Tasks" ("Id", "ProjectId", "Title", "Status", "Kind", "CreatedAt", "Tags", "Order")
        VALUES (@id, @id, @title, 0, 'Bug', now(), '{}', 0)
        """)]
    public partial Task<int> InsertTaskAsync(Guid id, string title, CancellationToken ct);

    [Command("""
        INSERT INTO "VersionedDocuments" ("Id", "Body", "Version") VALUES (@id, @body, @version)
        """)]
    public partial Task<int> InsertDocumentAsync(Guid id, string body, byte[] version, CancellationToken ct);

    [LinqQuery(nameof(SessionVersionQuery))]
    public partial Task<SessionVersion?> GetSessionAsync(Guid id, CancellationToken ct);

    [LinqCommand(nameof(GuardedTurnQuery), ThrowOnNoRows = true)]
    public partial Task RecordTurnAsync(Guid id, uint expected, CancellationToken ct);

    [LinqCommand(nameof(TurnQuery))]
    public partial Task<int> RecordTurnUnguardedAsync(Guid id, CancellationToken ct);

    [LinqQuery(nameof(TaskVersionQuery))]
    public partial Task<TaskVersion?> GetTaskAsync(Guid id, CancellationToken ct);

    [LinqCommand(nameof(RenameQuery), ThrowOnNoRows = true)]
    public partial Task RenameAsync(Guid id, uint rowVersion, string title, CancellationToken ct);

    [LinqQuery(nameof(DocumentQuery))]
    public partial Task<DocumentVersion?> GetDocumentAsync(Guid id, CancellationToken ct);

    [LinqCommand(nameof(SaveDocumentQuery), ThrowOnNoRows = true)]
    public partial Task SaveDocumentAsync(Guid id, byte[] expected, byte[] next, string body, CancellationToken ct);

    private static IQuery<SessionVersion> SessionVersionQuery(Guid id) =>
        Sql.Table<AgentSessionEntity>()
            .Where(s => s.Id == id)
            .Select(s => new SessionVersion(s.Id, s.TurnCount, Sql.Xmin(s)));

    private static int GuardedTurnQuery(Guid id, uint expected) =>
        Sql.Table<AgentSessionEntity>()
            .Where(s => s.Id == id && Sql.Xmin(s) == expected)
            .ExecuteUpdate(set => set.SetProperty(s => s.TurnCount, s => s.TurnCount + 1));

    private static int TurnQuery(Guid id) =>
        Sql.Table<AgentSessionEntity>()
            .Where(s => s.Id == id)
            .ExecuteUpdate(set => set.SetProperty(s => s.TurnCount, s => s.TurnCount + 1));

    private static IQuery<TaskVersion> TaskVersionQuery(Guid id) =>
        Sql.Table<TaskEntity>()
            .Where(t => t.Id == id)
            .Select(t => new TaskVersion(t.Id, t.Title, t.RowVersion));

    private static int RenameQuery(Guid id, uint rowVersion, string title) =>
        Sql.Table<TaskEntity>()
            .Where(t => t.Id == id && t.RowVersion == rowVersion)
            .ExecuteUpdate(set => set.SetProperty(t => t.Title, title));

    private static IQuery<DocumentVersion> DocumentQuery(Guid id) =>
        Sql.Table<VersionedDocumentEntity>()
            .Where(d => d.Id == id)
            .Select(d => new DocumentVersion(d.Id, d.Body, d.Version));

    private static int SaveDocumentQuery(Guid id, byte[] expected, byte[] next, string body) =>
        Sql.Table<VersionedDocumentEntity>()
            .Where(d => d.Id == id && d.Version == expected)
            .ExecuteUpdate(set => set.SetProperty(d => d.Body, body).SetProperty(d => d.Version, next));
}
