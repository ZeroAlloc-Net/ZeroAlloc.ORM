using System.Collections.Generic;
using System.Reflection;
using AwesomeAssertions;
using Xunit;
using ZeroAlloc.ORM.Migrations;

namespace ZeroAlloc.ORM.Tests.Migrations;

// #306 — IMigrationSource.Name scopes a source's versions in the history table.
public class MigrationSourceNameTests
{
    private static readonly Assembly TestAssembly = typeof(MigrationSourceNameTests).Assembly;

    [Fact]
    public void Default_name_is_the_implementing_type()
    {
        IMigrationSource source = new PlainSource();

        source.Name.Should().Be("ZeroAlloc.ORM.Tests.Migrations.MigrationSourceNameTests+PlainSource");
    }

    [Fact]
    public void Default_name_of_a_generic_source_carries_no_assembly_version()
    {
        IMigrationSource source = new GenericSource<int>();

        source.Name.Should().Be("ZeroAlloc.ORM.Tests.Migrations.MigrationSourceNameTests+GenericSource`1[System.Int32]");
    }

    [Fact]
    public void A_source_can_return_its_own_name()
    {
        IMigrationSource source = new NamedSource();

        source.Name.Should().Be("MyLibrary");
    }

    [Fact]
    public void Embedded_resource_source_defaults_to_the_prefix_without_its_trailing_dot()
    {
        var source = new EmbeddedResourceMigrationSource(TestAssembly, "ZeroAlloc.ORM.Tests.TestFixtures.Sequential.Migrations.");

        source.Name.Should().Be("ZeroAlloc.ORM.Tests.TestFixtures.Sequential.Migrations");
    }

    [Fact]
    public void Embedded_resource_source_without_a_prefix_defaults_to_the_assembly_name()
    {
        var source = new EmbeddedResourceMigrationSource(TestAssembly);

        source.Name.Should().Be("ZeroAlloc.ORM.Tests");
    }

    [Fact]
    public void Embedded_resource_source_name_can_be_set()
    {
        var source = new EmbeddedResourceMigrationSource(TestAssembly) { Name = "MyApp" };

        ((IMigrationSource)source).Name.Should().Be("MyApp");
    }

    private sealed class PlainSource : IMigrationSource
    {
        public IReadOnlyList<Migration> GetMigrations() => [];
    }

    private sealed class GenericSource<T> : IMigrationSource
    {
        public IReadOnlyList<Migration> GetMigrations() => [];
    }

    private sealed class NamedSource(string name = "MyLibrary") : IMigrationSource
    {
        public string Name => name;

        public IReadOnlyList<Migration> GetMigrations() => [];
    }
}
