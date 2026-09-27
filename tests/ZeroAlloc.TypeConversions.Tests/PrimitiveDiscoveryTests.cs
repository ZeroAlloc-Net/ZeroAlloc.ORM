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
    [InlineData("System.DateOnly")]
    [InlineData("System.TimeOnly")]
    public void Primitive_for_supported_scalar_types(string metadataName)
    {
        var compilation = TypeFixture.CreateCompilation("public class Anchor {}");
        var type = compilation.GetTypeByMetadataName(metadataName)!;

        var result = ConventionDiscovery.Resolve(type, new ConventionContext(compilation));

        result.Kind.Should().Be(ConventionKind.Primitive);
    }

    // #256 — DateOnly and TimeOnly read through GetFieldValue<T>, which Npgsql,
    // SqlClient and Microsoft.Data.Sqlite all implement for them, and declare
    // DbType.Date and DbType.Time on an output parameter.
    [Theory]
    [InlineData("System.DateOnly", "GetFieldValue<global::System.DateOnly>", "global::System.DateOnly", "Date")]
    [InlineData("System.TimeOnly", "GetFieldValue<global::System.TimeOnly>", "global::System.TimeOnly", "Time")]
    public void DateOnly_and_TimeOnly_map_to_their_reader_cast_type_and_DbType(
        string metadataName, string reader, string castType, string dbType)
    {
        var compilation = TypeFixture.CreateCompilation("public class Anchor {}");
        var type = compilation.GetTypeByMetadataName(metadataName)!;

        PrimitiveCatalog.GetScalarReaderMethod(type).Should().Be(reader);
        PrimitiveCatalog.GetScalarCastTypeFromReader(reader).Should().Be(castType);
        PrimitiveCatalog.GetDbTypeNameFromReader(reader).Should().Be(dbType);
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
