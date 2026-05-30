using FluentAssertions;
using Xunit;

namespace ZeroAlloc.TypeConversions.Tests;

public class ConventionDiscoveryTests
{
    [Fact]
    public void Unknown_for_arbitrary_type()
    {
        var compilation = TypeFixture.CreateCompilation("public class Foo {}");
        var type = compilation.GetTypeByMetadataName("Foo")!;

        var result = ConventionDiscovery.Resolve(type, new ConventionContext(compilation));

        result.Kind.Should().Be(ConventionKind.Unknown);
        result.Factory.Should().BeNull();
        result.Value.Should().BeNull();
        result.ExpandedColumns.Should().BeEmpty();
    }
}
