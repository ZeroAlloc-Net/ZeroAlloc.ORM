using System;

namespace ZeroAlloc.ORM.Migrations;

/// <summary>
/// Adopts a database whose schema was created outside <see cref="MigrationRunner"/>, for
/// example by another tool. When the migration source has no recorded history, the
/// <see cref="Check"/> decides whether migrations 1 through <see cref="ThroughVersion"/> are
/// recorded as applied without being run.
/// <para>
/// <see cref="ThroughVersion"/> must be one of the source's migration versions. The runner
/// throws <see cref="ZeroAllocOrmMigrationConflictException"/> on every run when it is not,
/// even when the source already has history, so a stale baseline fails loudly.
/// </para>
/// </summary>
public sealed class MigrationBaseline
{
    /// <summary>Creates a baseline covering versions 1 through <paramref name="throughVersion"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="throughVersion"/> is below 1.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="check"/> is null.</exception>
    public MigrationBaseline(int throughVersion, IMigrationBaselineCheck check)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(throughVersion, 1);
        ThroughVersion = throughVersion;
        Check = check ?? throw new ArgumentNullException(nameof(check));
    }

    /// <summary>The highest version recorded as applied when the check returns <see cref="BaselineDecision.MarkApplied"/>.</summary>
    public int ThroughVersion { get; }

    /// <summary>The check that decides whether to record the baseline or run every migration.</summary>
    public IMigrationBaselineCheck Check { get; }
}
