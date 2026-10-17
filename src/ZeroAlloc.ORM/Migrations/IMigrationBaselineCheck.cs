using System.Data.Async;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroAlloc.ORM.Migrations;

/// <summary>
/// Decides, for a database on which a migration source has no recorded history, whether the
/// baseline migrations are already reflected in its schema. Used through
/// <see cref="MigrationBaseline"/>.
/// </summary>
public interface IMigrationBaselineCheck
{
    /// <summary>
    /// Inspects the database behind <paramref name="connection"/> and returns what the runner
    /// should do. Called at most once per run, and only when the source has no history.
    /// </summary>
    /// <exception cref="ZeroAllocOrmMigrationConflictException">
    /// Thrown when the database must not be touched, for example because its schema matches
    /// neither an empty database nor the baseline. The runner records no history rows and runs no migrations, and rethrows. Any exception from the check propagates the same way.
    /// </exception>
    Task<BaselineDecision> DecideAsync(IAsyncDbConnection connection, CancellationToken ct);
}
