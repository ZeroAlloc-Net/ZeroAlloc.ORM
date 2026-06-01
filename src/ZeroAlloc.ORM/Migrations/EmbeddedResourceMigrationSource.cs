using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace ZeroAlloc.ORM.Migrations;

/// <summary>
/// v1.1 — <see cref="IMigrationSource"/> backed by embedded <c>.sql</c> resources
/// in a caller-supplied assembly. AOT-clean: uses
/// <see cref="Assembly.GetManifestResourceNames"/> +
/// <see cref="Assembly.GetManifestResourceStream(string)"/>, both of which are
/// safe under NativeAOT once the host assembly is loaded.
///
/// <para>
/// Resources are discovered by file-naming convention rather than attribute, so
/// adding a migration is a single-file change with no code edits. The expected
/// name shape (relative to the project's RootNamespace) is:
/// </para>
/// <code>
///   &lt;RootNamespace&gt;.Migrations.NNN_&lt;name&gt;.sql
/// </code>
/// <para>
/// where <c>NNN</c> is 3+ digits and <c>&lt;name&gt;</c> is a
/// <c>[A-Za-z0-9_]+</c> identifier. The leading number parses into
/// <see cref="Migration.Version"/>; <c>&lt;name&gt;</c> becomes
/// <see cref="Migration.Name"/>; the file body becomes <see cref="Migration.Sql"/>.
/// </para>
///
/// <para>
/// When <c>resourceNamespacePrefix</c> is non-null, only resources whose name
/// starts with that literal prefix are considered (the typical production
/// use-case: scope to your project's own <c>Migrations</c> folder so unrelated
/// embedded SQL elsewhere in the assembly is ignored). When the prefix is
/// <c>null</c>, the matcher accepts any resource whose name contains
/// <c>.Migrations.NNN_&lt;name&gt;.sql</c> — useful for monorepo / multi-project
/// scenarios where migrations live under several namespaces.
/// </para>
/// </summary>
public sealed class EmbeddedResourceMigrationSource : IMigrationSource
{
    // Pattern targets the tail of the resource name. The "relaxed" form (prefix
    // null) requires the literal `.Migrations.` segment to appear so unrelated
    // embedded SQL doesn't get misclassified as a migration. Multi-line is OFF;
    // the resource name is always single-line.
    //
    // 3+ digits per the spec ("3+ digits"); upper bound is unbounded so callers
    // can adopt large version numbers (timestamps, etc.) without restructuring.
    //
    // Both patterns are anchored, use only finite-bounded character classes, and
    // are matched against bounded resource-name strings — they don't admit the
    // exponential-backtracking shapes Meziantou's MA0009 warns about. The 1-second
    // timeout is a defense-in-depth ceiling for any future pattern edit that
    // accidentally introduces a catastrophic-backtracking branch.
    private static readonly Regex RelaxedNamespacePattern =
        new(
            @"\.Migrations\.(?<version>\d{3,})_(?<name>[A-Za-z0-9_]+)\.sql$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

    // Prefix-scoped form: matches `<prefix>NNN_<name>.sql` where the prefix is
    // expected to already include the trailing `.Migrations.` segment (so the
    // pattern itself stays terse).
    private static readonly Regex PrefixScopedTailPattern =
        new(
            @"^(?<version>\d{3,})_(?<name>[A-Za-z0-9_]+)\.sql$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

    private readonly Assembly _assembly;
    private readonly string? _resourceNamespacePrefix;

    /// <summary>
    /// Creates a new source that scans the given assembly for embedded
    /// migration SQL resources.
    /// </summary>
    /// <param name="assembly">Assembly to scan. Use <c>typeof(Anchor).Assembly</c> from the project that owns the migrations.</param>
    /// <param name="resourceNamespacePrefix">
    /// Optional literal prefix that the resource name must start with — typically
    /// <c>&quot;MyApp.Migrations.&quot;</c>. When <c>null</c>, any resource whose
    /// name contains <c>.Migrations.NNN_&lt;name&gt;.sql</c> is picked up.
    /// </param>
    public EmbeddedResourceMigrationSource(Assembly assembly, string? resourceNamespacePrefix = null)
    {
        _assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
        _resourceNamespacePrefix = resourceNamespacePrefix;
    }

    /// <inheritdoc />
    public IReadOnlyList<Migration> GetMigrations()
    {
        var names = _assembly.GetManifestResourceNames();
        var list = new List<Migration>(capacity: names.Length);

        foreach (var name in names)
        {
            if (!TryMatch(name, out var version, out var migrationName))
            {
                continue;
            }

            var sql = LoadResourceText(name);
            list.Add(new Migration(version, migrationName, sql));
        }

        // Stable order by ascending version. Equal versions retain insertion
        // order from GetManifestResourceNames (which is itself deterministic
        // for a given build).
        list.Sort(static (a, b) => a.Version.CompareTo(b.Version));
        return list;
    }

    private bool TryMatch(string resourceName, out int version, out string migrationName)
    {
        version = 0;
        migrationName = string.Empty;

        Match match;
        if (_resourceNamespacePrefix is not null)
        {
            if (!resourceName.StartsWith(_resourceNamespacePrefix, StringComparison.Ordinal))
            {
                return false;
            }

            var tail = resourceName.Substring(_resourceNamespacePrefix.Length);
            match = PrefixScopedTailPattern.Match(tail);
        }
        else
        {
            match = RelaxedNamespacePattern.Match(resourceName);
        }

        if (!match.Success)
        {
            return false;
        }

        if (!int.TryParse(match.Groups["version"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out version))
        {
            return false;
        }

        migrationName = match.Groups["name"].Value;
        return true;
    }

    private string LoadResourceText(string resourceName)
    {
        using var stream = _assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Manifest resource '{resourceName}' returned a null stream from assembly '{_assembly.FullName}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
