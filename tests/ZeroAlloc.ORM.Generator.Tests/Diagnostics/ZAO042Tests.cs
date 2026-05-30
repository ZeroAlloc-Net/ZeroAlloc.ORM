using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Diagnostics;

// ZAO042 fires when [StoreAsString] is applied to a non-enum type. The attribute
// only has meaning on enums (it flips round-trip from integral to member-name).
// The detection lives in its own ForAttributeWithMetadataName pipeline so it
// fires even when no [Query] method references the mis-annotated type.
public class ZAO042Tests
{
    [Fact]
    public void StoreAsString_on_class_emits_ZAO042()
    {
        var source = """
            using ZeroAlloc.ORM;

            namespace TestApp;

            [StoreAsString]
            public sealed class NotAnEnum {}
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAO042", System.StringComparison.Ordinal));
    }

    [Fact]
    public void StoreAsString_on_enum_does_not_emit_ZAO042()
    {
        // Positive control — [StoreAsString] is legal on enum types.
        var source = """
            using ZeroAlloc.ORM;

            namespace TestApp;

            [StoreAsString]
            public enum OrderStatus
            {
                Pending,
                Shipped,
                Cancelled,
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO042", System.StringComparison.Ordinal));
    }
}
