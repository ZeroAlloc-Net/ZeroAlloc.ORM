using FluentAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Abstractions.Tests;

public class StoreAsStringAttributeTests
{
    [Fact]
    public void Targets_enum_only()
    {
        var usage = typeof(StoreAsStringAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
            .Cast<AttributeUsageAttribute>()
            .Single();
        usage.ValidOn.Should().Be(AttributeTargets.Enum);
    }

    [Fact]
    public void Has_parameterless_ctor()
    {
        _ = new StoreAsStringAttribute();
    }
}
