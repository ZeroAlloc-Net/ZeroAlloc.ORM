using System.Linq;
using System.Reflection;
using FluentAssertions;
using Xunit;
using ZeroAlloc.ORM.Migrations;

namespace ZeroAlloc.ORM.Tests.Migrations;

/// <summary>
/// v1.1 Phase A.1 — coverage for the embedded-resource discovery contract.
///
/// Resource names are formed from RootNamespace ("ZeroAlloc.ORM.Tests") + folder
/// path, so the embedded SQL fixtures under tests/.../TestFixtures/&lt;Set&gt;/Migrations/
/// surface as resources named:
///   ZeroAlloc.ORM.Tests.TestFixtures.&lt;Set&gt;.Migrations.NNN_&lt;name&gt;.sql
///
/// All tests scope by an explicit prefix to keep the assertions deterministic
/// (the relaxed-namespace, null-prefix path is covered by a dedicated test that
/// asserts on the union shape rather than per-set ordering).
/// </summary>
public class EmbeddedResourceMigrationSourceTests
{
    private static readonly Assembly TestAssembly = typeof(EmbeddedResourceMigrationSourceTests).Assembly;

    [Fact]
    public void GetMigrations_returns_migrations_in_version_order()
    {
        var source = new EmbeddedResourceMigrationSource(
            TestAssembly,
            resourceNamespacePrefix: "ZeroAlloc.ORM.Tests.TestFixtures.Sequential.Migrations.");

        var migrations = source.GetMigrations();

        migrations.Should().HaveCount(2);
        migrations[0].Version.Should().Be(1);
        migrations[0].Name.Should().Be("create_users");
        migrations[0].Sql.Should().Contain("CREATE TABLE users");
        migrations[1].Version.Should().Be(2);
        migrations[1].Name.Should().Be("add_email");
        migrations[1].Sql.Should().Contain("ADD COLUMN email");
    }

    [Fact]
    public void GetMigrations_with_gap_in_versions_returns_them_in_version_order()
    {
        var source = new EmbeddedResourceMigrationSource(
            TestAssembly,
            resourceNamespacePrefix: "ZeroAlloc.ORM.Tests.TestFixtures.Gap.Migrations.");

        var migrations = source.GetMigrations();

        migrations.Should().HaveCount(2);
        migrations.Select(m => m.Version).Should().Equal(1, 3);
    }

    [Fact]
    public void GetMigrations_handles_4_digit_versions()
    {
        var source = new EmbeddedResourceMigrationSource(
            TestAssembly,
            resourceNamespacePrefix: "ZeroAlloc.ORM.Tests.TestFixtures.Large.Migrations.");

        var migrations = source.GetMigrations();

        migrations.Should().HaveCount(1);
        migrations[0].Version.Should().Be(1234);
        migrations[0].Name.Should().Be("far_future");
    }

    [Fact]
    public void GetMigrations_with_namespace_prefix_filters_to_that_prefix()
    {
        var altOnly = new EmbeddedResourceMigrationSource(
            TestAssembly,
            resourceNamespacePrefix: "ZeroAlloc.ORM.Tests.TestFixtures.Alt.Migrations.");

        var migrations = altOnly.GetMigrations();

        migrations.Should().HaveCount(1);
        migrations[0].Version.Should().Be(1);
        migrations[0].Name.Should().Be("alt_only");

        // Sanity: the Sequential prefix returns its own 2 migrations and none from Alt.
        var sequential = new EmbeddedResourceMigrationSource(
            TestAssembly,
            resourceNamespacePrefix: "ZeroAlloc.ORM.Tests.TestFixtures.Sequential.Migrations.");
        sequential.GetMigrations().Select(m => m.Name)
            .Should().NotContain("alt_only");
    }

    [Fact]
    public void GetMigrations_with_null_prefix_picks_up_all_Migrations_folders_in_assembly()
    {
        // Relaxed-namespace matcher: any resource whose name contains
        // ".Migrations.NNN_<name>.sql" — regardless of parent namespace —
        // is picked up. With four fixture sets (Sequential 1,2 + Gap 1,3 +
        // Large 1234 + Alt 1) the union is 6 migrations.
        var source = new EmbeddedResourceMigrationSource(TestAssembly);

        var migrations = source.GetMigrations();

        migrations.Should().HaveCount(6);
        // Sorted by version ascending; ties at version 1 are present multiple
        // times because each fixture set is independent. The ordering invariant
        // is "non-decreasing by version"; equal-version names need not be
        // unique across sets.
        migrations.Select(m => m.Version)
            .Should().BeInAscendingOrder();
    }
}
