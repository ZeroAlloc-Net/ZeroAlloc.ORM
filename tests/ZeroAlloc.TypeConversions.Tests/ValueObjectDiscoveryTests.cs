using FluentAssertions;
using Xunit;

namespace ZeroAlloc.TypeConversions.Tests;

public class ValueObjectDiscoveryTests
{
    [Fact]
    public void ValueObject_attribute_detected_on_partial_struct()
    {
        var compilation = TypeFixture.CreateCompilation("""
            using ZeroAlloc.ValueObjects;

            [ValueObject]
            public readonly partial struct OrderId(int value)
            {
                public int Value { get; } = value;
                public static OrderId From(int v) => new(v);
            }
            """);
        var type = compilation.GetTypeByMetadataName("OrderId")!;

        var result = ConventionDiscovery.Resolve(type, new ConventionContext(compilation));

        result.Kind.Should().Be(ConventionKind.ValueObject);
    }

    [Fact]
    public void ValueObject_attribute_detected_returns_factory_and_value()
    {
        var compilation = TypeFixture.CreateCompilation("""
            using ZeroAlloc.ValueObjects;

            [ValueObject]
            public readonly partial struct OrderId(int value)
            {
                public int Value { get; } = value;
                public static OrderId From(int v) => new(v);
            }
            """);
        var type = compilation.GetTypeByMetadataName("OrderId")!;

        var result = ConventionDiscovery.Resolve(type, new ConventionContext(compilation));

        result.Value.Should().NotBeNull();
        result.Value!.Name.Should().Be("Value");
        result.Factory.Should().NotBeNull();
        result.Factory!.Name.Should().Be("From");
    }

    [Fact]
    public void Plain_struct_without_attribute_is_not_ValueObject()
    {
        var compilation = TypeFixture.CreateCompilation("""
            public readonly struct OrderId
            {
                public int Value { get; }
                public OrderId(int v) { Value = v; }
                public static OrderId From(int v) => new(v);
            }
            """);
        var type = compilation.GetTypeByMetadataName("OrderId")!;

        var result = ConventionDiscovery.Resolve(type, new ConventionContext(compilation));

        result.Kind.Should().NotBe(ConventionKind.ValueObject);
    }
}
