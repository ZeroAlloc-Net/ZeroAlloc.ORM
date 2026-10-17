using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ZeroAlloc.TypeConversions.Tests;

// PostgreSQL arrays: `T[]`, `List<T>` and `IReadOnlyList<T>` of string, int, long or Guid
// classify as PrimitiveArray and carry the element type and the matched collection shape.
// `byte[]` stays a Primitive, a blob, and other element types stay Unknown.
public class PrimitiveArrayDiscoveryTests
{
    [Theory]
    [InlineData("string[]", "string", PrimitiveArrayShape.Array)]
    [InlineData("int[]", "int", PrimitiveArrayShape.Array)]
    [InlineData("long[]", "long", PrimitiveArrayShape.Array)]
    [InlineData("System.Guid[]", "System.Guid", PrimitiveArrayShape.Array)]
    [InlineData("System.Collections.Generic.List<string>", "string", PrimitiveArrayShape.List)]
    [InlineData("System.Collections.Generic.List<int>", "int", PrimitiveArrayShape.List)]
    [InlineData("System.Collections.Generic.List<long>", "long", PrimitiveArrayShape.List)]
    [InlineData("System.Collections.Generic.List<System.Guid>", "System.Guid", PrimitiveArrayShape.List)]
    [InlineData("System.Collections.Generic.IReadOnlyList<string>", "string", PrimitiveArrayShape.ReadOnlyList)]
    [InlineData("System.Collections.Generic.IReadOnlyList<int>", "int", PrimitiveArrayShape.ReadOnlyList)]
    [InlineData("System.Collections.Generic.IReadOnlyList<long>", "long", PrimitiveArrayShape.ReadOnlyList)]
    [InlineData("System.Collections.Generic.IReadOnlyList<System.Guid>", "System.Guid", PrimitiveArrayShape.ReadOnlyList)]
    public void Supported_shape_and_element_is_PrimitiveArray(
        string typeSyntax, string elementDisplay, PrimitiveArrayShape shape)
    {
        var (compilation, type) = TypeOf(typeSyntax);

        var result = ConventionDiscovery.Resolve(type, new ConventionContext(compilation));

        result.Kind.Should().Be(ConventionKind.PrimitiveArray);
        result.ArrayShape.Should().Be(shape);
        result.ElementType.Should().NotBeNull();
        result.ElementType!.ToDisplayString().Should().Be(elementDisplay);
    }

    [Fact]
    public void Byte_array_stays_Primitive()
    {
        var (compilation, type) = TypeOf("byte[]");

        var result = ConventionDiscovery.Resolve(type, new ConventionContext(compilation));

        result.Kind.Should().Be(ConventionKind.Primitive);
        result.ElementType.Should().BeNull();
        result.ArrayShape.Should().Be(PrimitiveArrayShape.None);
    }

    [Theory]
    [InlineData("decimal[]")]
    [InlineData("System.Collections.Generic.List<CustomerRow>")]
    [InlineData("System.Collections.Generic.IList<string>")]
    [InlineData("string[,]")]
    [InlineData("int?[]")]
    public void Unsupported_shape_or_element_stays_Unknown(string typeSyntax)
    {
        var (compilation, type) = TypeOf(typeSyntax);

        var result = ConventionDiscovery.Resolve(type, new ConventionContext(compilation));

        result.Kind.Should().Be(ConventionKind.Unknown);
    }

    private static (Compilation Compilation, ITypeSymbol Type) TypeOf(string typeSyntax)
    {
        var compilation = TypeFixture.CreateCompilation($$"""
            public sealed record CustomerRow(int Id);
            public class Anchor { public {{typeSyntax}} Value => null!; }
            """);
        var anchor = compilation.GetTypeByMetadataName("Anchor")!;
        var property = (IPropertySymbol)anchor.GetMembers("Value")[0];
        return (compilation, property.Type);
    }
}
