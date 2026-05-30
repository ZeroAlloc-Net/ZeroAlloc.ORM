using FluentAssertions;
using Xunit;

namespace ZeroAlloc.TypeConversions.Tests;

public class PrimitiveDiscoveryTests
{
    [Theory]
    [InlineData("System.Int32")]
    [InlineData("System.Int64")]
    [InlineData("System.Int16")]
    [InlineData("System.Byte")]
    [InlineData("System.Boolean")]
    [InlineData("System.Decimal")]
    [InlineData("System.Double")]
    [InlineData("System.Single")]
    [InlineData("System.String")]
    [InlineData("System.DateTime")]
    [InlineData("System.DateTimeOffset")]
    [InlineData("System.TimeSpan")]
    [InlineData("System.Guid")]
    public void Primitive_for_supported_scalar_types(string metadataName)
    {
        var compilation = TypeFixture.CreateCompilation("public class Anchor {}");
        var type = compilation.GetTypeByMetadataName(metadataName)!;

        var result = ConventionDiscovery.Resolve(type, new ConventionContext(compilation));

        result.Kind.Should().Be(ConventionKind.Primitive);
    }

    [Fact]
    public void Primitive_for_byte_array()
    {
        var compilation = TypeFixture.CreateCompilation("public class Anchor { public byte[] Blob => null!; }");
        var anchor = compilation.GetTypeByMetadataName("Anchor")!;
        var blobProp = (Microsoft.CodeAnalysis.IPropertySymbol)anchor.GetMembers("Blob")[0];

        var result = ConventionDiscovery.Resolve(blobProp.Type, new ConventionContext(compilation));

        result.Kind.Should().Be(ConventionKind.Primitive);
    }
}
