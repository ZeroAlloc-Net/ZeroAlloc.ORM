using FluentAssertions;
using Xunit;

namespace ZeroAlloc.TypeConversions.Tests;

// Priority ladder (design doc Section 3): when a single type matches multiple
// rules, the higher-priority rule wins. These tests pin the ladder so future
// refactors of ConventionDiscovery.Resolve can't silently reorder it.
public class PriorityOrderTests
{
    [Fact]
    public void Type_with_ValueObject_and_From_factory_resolves_as_ValueObject()
    {
        var compilation = TypeFixture.CreateCompilation("""
            using ZeroAlloc.ValueObjects;

            [ValueObject]
            public readonly partial struct OrderId
            {
                public int Value { get; }
                private OrderId(int v) { Value = v; }
                public static OrderId From(int v) => new(v);
            }
            """);
        var type = compilation.GetTypeByMetadataName("OrderId")!;

        var result = ConventionDiscovery.Resolve(type, new ConventionContext(compilation));

        result.Kind.Should().Be(ConventionKind.ValueObject);
    }

    [Fact]
    public void Record_with_static_From_resolves_as_StaticFactory_not_SingleArgCtor()
    {
        // The factory often encodes invariants over the primary ctor (e.g. guard
        // clauses), so when both are present StaticFactory wins.
        var compilation = TypeFixture.CreateCompilation("""
            public readonly record struct Score(int Value)
            {
                public static Score From(int v) => new(v);
            }
            """);
        var type = compilation.GetTypeByMetadataName("Score")!;

        var result = ConventionDiscovery.Resolve(type, new ConventionContext(compilation));

        result.Kind.Should().Be(ConventionKind.StaticFactory);
    }

    [Fact]
    public void Primitive_int_beats_everything_else()
    {
        var compilation = TypeFixture.CreateCompilation("public class Anchor {}");
        var type = compilation.GetTypeByMetadataName("System.Int32")!;

        var result = ConventionDiscovery.Resolve(type, new ConventionContext(compilation));

        result.Kind.Should().Be(ConventionKind.Primitive);
    }

    [Fact]
    public void Arbitrary_class_falls_through_to_Unknown()
    {
        var compilation = TypeFixture.CreateCompilation("public class Foo { public int X; }");
        var type = compilation.GetTypeByMetadataName("Foo")!;

        var result = ConventionDiscovery.Resolve(type, new ConventionContext(compilation));

        result.Kind.Should().Be(ConventionKind.Unknown);
    }
}
