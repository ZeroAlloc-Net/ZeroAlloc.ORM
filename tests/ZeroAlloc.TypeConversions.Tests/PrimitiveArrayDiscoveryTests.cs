using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ZeroAlloc.TypeConversions.Tests;

// PostgreSQL arrays: `T[]`, `List<T>` and `IReadOnlyList<T>` of string, int, long, float, double or Guid
// classify as PrimitiveArray and carry the element type and the matched collection shape.
// `byte[]` stays a Primitive, a blob, and other element types stay Unknown.
public class PrimitiveArrayDiscoveryTests
{
    [Theory]
    [InlineData("string[]", "string", PrimitiveArrayShape.Array)]
    [InlineData("int[]", "int", PrimitiveArrayShape.Array)]
    [InlineData("long[]", "long", PrimitiveArrayShape.Array)]
    [InlineData("float[]", "float", PrimitiveArrayShape.Array)]
    [InlineData("double[]", "double", PrimitiveArrayShape.Array)]
    [InlineData("System.Guid[]", "System.Guid", PrimitiveArrayShape.Array)]
    [InlineData("System.Collections.Generic.List<string>", "string", PrimitiveArrayShape.List)]
    [InlineData("System.Collections.Generic.List<int>", "int", PrimitiveArrayShape.List)]
    [InlineData("System.Collections.Generic.List<long>", "long", PrimitiveArrayShape.List)]
    [InlineData("System.Collections.Generic.List<float>", "float", PrimitiveArrayShape.List)]
    [InlineData("System.Collections.Generic.List<double>", "double", PrimitiveArrayShape.List)]
    [InlineData("System.Collections.Generic.List<System.Guid>", "System.Guid", PrimitiveArrayShape.List)]
    [InlineData("System.Collections.Generic.IReadOnlyList<string>", "string", PrimitiveArrayShape.ReadOnlyList)]
    [InlineData("System.Collections.Generic.IReadOnlyList<int>", "int", PrimitiveArrayShape.ReadOnlyList)]
    [InlineData("System.Collections.Generic.IReadOnlyList<long>", "long", PrimitiveArrayShape.ReadOnlyList)]
    [InlineData("System.Collections.Generic.IReadOnlyList<float>", "float", PrimitiveArrayShape.ReadOnlyList)]
    [InlineData("System.Collections.Generic.IReadOnlyList<double>", "double", PrimitiveArrayShape.ReadOnlyList)]
    [InlineData("System.Collections.Generic.IReadOnlyList<System.Guid>", "System.Guid", PrimitiveArrayShape.ReadOnlyList)]
    // Reference nullability does not change the shape. ElementType keeps the annotation as written.
    [InlineData("string[]?", "string", PrimitiveArrayShape.Array)]
    [InlineData("string?[]", "string?", PrimitiveArrayShape.Array)]
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
    [InlineData("System.Collections.Generic.List<int?>")]
    [InlineData("float?[]")]
    [InlineData("System.Collections.Generic.List<double?>")]
    [InlineData("System.Collections.Immutable.ImmutableArray<string>")]
    public void Unsupported_shape_or_element_stays_Unknown(string typeSyntax)
    {
        var (compilation, type) = TypeOf(typeSyntax);

        var result = ConventionDiscovery.Resolve(type, new ConventionContext(compilation));

        result.Kind.Should().Be(ConventionKind.Unknown);
    }

    // An array is one column, so a multi-column record holding one is a composite.
    [Fact]
    public void Record_with_array_member_is_MultiArgCtor()
    {
        var (compilation, type) = TypeOf("Labels");

        var result = ConventionDiscovery.Resolve(type, new ConventionContext(compilation));

        result.Kind.Should().Be(ConventionKind.MultiArgCtor);
        result.ExpandedColumns.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("string", "GetFieldValue<string[]>")]
    [InlineData("string?", "GetFieldValue<string[]>")]
    [InlineData("int", "GetFieldValue<int[]>")]
    [InlineData("long", "GetFieldValue<long[]>")]
    [InlineData("float", "GetFieldValue<float[]>")]
    [InlineData("double", "GetFieldValue<double[]>")]
    [InlineData("System.Guid", "GetFieldValue<global::System.Guid[]>")]
    public void Array_reader_reads_the_element_array(string elementSyntax, string reader)
    {
        var (_, element) = TypeOf(elementSyntax);

        PrimitiveCatalog.GetArrayReaderMethod(element).Should().Be(reader);
    }

    [Theory]
    [InlineData("string", "text[]")]
    [InlineData("string?", "text[]")]
    [InlineData("int", "integer[]")]
    [InlineData("long", "bigint[]")]
    [InlineData("float", "real[]")]
    [InlineData("double", "double precision[]")]
    [InlineData("System.Guid", "uuid[]")]
    public void Postgres_array_type_name_names_the_element_array(string elementSyntax, string typeName)
    {
        var (_, element) = TypeOf(elementSyntax);

        PrimitiveCatalog.GetPostgresArrayTypeName(element).Should().Be(typeName);
    }

    [Theory]
    [InlineData("byte")]
    [InlineData("decimal")]
    [InlineData("CustomerRow")]
    public void Postgres_array_type_name_is_null_for_other_elements(string elementSyntax)
    {
        var (_, element) = TypeOf(elementSyntax);

        PrimitiveCatalog.GetPostgresArrayTypeName(element).Should().BeNull();
    }

    [Theory]
    [InlineData("byte")]
    [InlineData("decimal")]
    [InlineData("CustomerRow")]
    public void Array_reader_is_null_for_other_elements(string elementSyntax)
    {
        var (_, element) = TypeOf(elementSyntax);

        PrimitiveCatalog.GetArrayReaderMethod(element).Should().BeNull();
    }

    private static (Compilation Compilation, ITypeSymbol Type) TypeOf(string typeSyntax)
    {
        var compilation = TypeFixture.CreateCompilation($$"""
            #nullable enable
            public sealed record CustomerRow(int Id);
            public sealed record Labels(string Name, string[] Tags);
            public class Anchor { public {{typeSyntax}} Value => default!; }
            """);
        var anchor = compilation.GetTypeByMetadataName("Anchor")!;
        var property = (IPropertySymbol)anchor.GetMembers("Value")[0];
        // An unresolved type would classify as Unknown for the wrong reason.
        property.Type.TypeKind.Should().NotBe(TypeKind.Error);
        return (compilation, property.Type);
    }
}
