using FluentAssertions;
using Xunit;

namespace ZeroAlloc.TypeConversions.Tests;

public class SingleArgCtorDiscoveryTests
{
    [Fact]
    public void Record_struct_with_one_primitive_param_is_SingleArgCtor()
    {
        var compilation = TypeFixture.CreateCompilation("public readonly record struct OrderId(int Value);");
        var type = compilation.GetTypeByMetadataName("OrderId")!;

        var result = ConventionDiscovery.Resolve(type, new ConventionContext(compilation));

        result.Kind.Should().Be(ConventionKind.SingleArgCtor);
        result.Factory.Should().NotBeNull();
        result.Value.Should().NotBeNull();
        result.Value!.Name.Should().Be("Value");
    }

    [Fact]
    public void Record_class_with_one_primitive_param_is_SingleArgCtor()
    {
        var compilation = TypeFixture.CreateCompilation("public record OrderId(string Value);");
        var type = compilation.GetTypeByMetadataName("OrderId")!;

        var result = ConventionDiscovery.Resolve(type, new ConventionContext(compilation));

        result.Kind.Should().Be(ConventionKind.SingleArgCtor);
    }

    [Fact]
    public void Plain_class_with_one_primitive_param_is_not_SingleArgCtor()
    {
        var compilation = TypeFixture.CreateCompilation("""
            public class OrderId
            {
                public OrderId(int v) { Value = v; }
                public int Value { get; }
            }
            """);
        var type = compilation.GetTypeByMetadataName("OrderId")!;

        var result = ConventionDiscovery.Resolve(type, new ConventionContext(compilation));

        result.Kind.Should().NotBe(ConventionKind.SingleArgCtor);
    }

    [Fact]
    public void Record_with_two_params_is_not_SingleArgCtor()
    {
        var compilation = TypeFixture.CreateCompilation("public record Point(int X, int Y);");
        var type = compilation.GetTypeByMetadataName("Point")!;

        var result = ConventionDiscovery.Resolve(type, new ConventionContext(compilation));

        result.Kind.Should().NotBe(ConventionKind.SingleArgCtor);
    }

    [Fact]
    public void Record_with_non_primitive_param_is_not_SingleArgCtor()
    {
        var compilation = TypeFixture.CreateCompilation("""
            public class Inner {}
            public record Wrapper(Inner Value);
            """);
        var type = compilation.GetTypeByMetadataName("Wrapper")!;

        var result = ConventionDiscovery.Resolve(type, new ConventionContext(compilation));

        result.Kind.Should().NotBe(ConventionKind.SingleArgCtor);
    }
}
