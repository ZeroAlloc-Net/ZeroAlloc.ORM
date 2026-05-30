using FluentAssertions;
using Xunit;

namespace ZeroAlloc.TypeConversions.Tests;

public class EnumDiscoveryTests
{
    [Fact]
    public void Plain_enum_is_Enum_kind()
    {
        var compilation = TypeFixture.CreateCompilation("public enum OrderStatus { Pending, Cancelled }");
        var type = compilation.GetTypeByMetadataName("OrderStatus")!;

        var result = ConventionDiscovery.Resolve(type, new ConventionContext(compilation));

        result.Kind.Should().Be(ConventionKind.Enum);
    }

    [Fact]
    public void Enum_with_StoreAsString_is_EnumAsString_kind()
    {
        var compilation = TypeFixture.CreateCompilation("""
            using ZeroAlloc.ORM;

            [StoreAsString]
            public enum OrderStatus { Pending, Cancelled }
            """);
        var type = compilation.GetTypeByMetadataName("OrderStatus")!;

        var result = ConventionDiscovery.Resolve(type, new ConventionContext(compilation));

        result.Kind.Should().Be(ConventionKind.EnumAsString);
    }
}
