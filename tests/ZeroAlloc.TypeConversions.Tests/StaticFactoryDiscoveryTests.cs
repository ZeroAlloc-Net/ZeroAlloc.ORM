using FluentAssertions;
using Xunit;

namespace ZeroAlloc.TypeConversions.Tests;

public class StaticFactoryDiscoveryTests
{
    [Fact]
    public void Struct_with_static_From_factory_is_StaticFactory()
    {
        var compilation = TypeFixture.CreateCompilation("""
            public readonly struct Score
            {
                public int Value { get; }
                private Score(int v) { Value = v; }
                public static Score From(int value) => new(value);
            }
            """);
        var type = compilation.GetTypeByMetadataName("Score")!;

        var result = ConventionDiscovery.Resolve(type, new ConventionContext(compilation));

        result.Kind.Should().Be(ConventionKind.StaticFactory);
        result.Factory.Should().NotBeNull();
        result.Factory!.Name.Should().Be("From");
        result.Value.Should().NotBeNull();
        result.Value!.Name.Should().Be("Value");
    }

    [Fact]
    public void Class_with_static_FromValue_factory_is_StaticFactory()
    {
        var compilation = TypeFixture.CreateCompilation("""
            public sealed class Token
            {
                public string Value { get; }
                private Token(string v) { Value = v; }
                public static Token FromValue(string value) => new(value);
            }
            """);
        var type = compilation.GetTypeByMetadataName("Token")!;

        var result = ConventionDiscovery.Resolve(type, new ConventionContext(compilation));

        result.Kind.Should().Be(ConventionKind.StaticFactory);
        result.Factory!.Name.Should().Be("FromValue");
    }

    [Fact]
    public void Type_without_From_or_FromValue_factory_is_not_StaticFactory()
    {
        var compilation = TypeFixture.CreateCompilation("""
            public sealed class Token
            {
                public string Value { get; }
                private Token(string v) { Value = v; }
                public static Token Create(string value) => new(value);
            }
            """);
        var type = compilation.GetTypeByMetadataName("Token")!;

        var result = ConventionDiscovery.Resolve(type, new ConventionContext(compilation));

        result.Kind.Should().NotBe(ConventionKind.StaticFactory);
    }

    [Fact]
    public void Factory_returning_wrong_type_does_not_match()
    {
        var compilation = TypeFixture.CreateCompilation("""
            public sealed class Token
            {
                public string Value { get; }
                private Token(string v) { Value = v; }
                public static string From(string value) => value;
            }
            """);
        var type = compilation.GetTypeByMetadataName("Token")!;

        var result = ConventionDiscovery.Resolve(type, new ConventionContext(compilation));

        result.Kind.Should().NotBe(ConventionKind.StaticFactory);
    }

    [Fact]
    public void Factory_with_non_primitive_parameter_does_not_match()
    {
        var compilation = TypeFixture.CreateCompilation("""
            public sealed class Inner {}
            public sealed class Token
            {
                private Token() {}
                public static Token From(Inner inner) => new();
            }
            """);
        var type = compilation.GetTypeByMetadataName("Token")!;

        var result = ConventionDiscovery.Resolve(type, new ConventionContext(compilation));

        result.Kind.Should().NotBe(ConventionKind.StaticFactory);
    }
}
