using System;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Abstractions.Tests;

public class StoreAsStringAttributeTests
{
    [Fact]
    public void Can_be_instantiated()
    {
        var attr = new StoreAsStringAttribute();
        attr.Should().NotBeNull();
    }

    [Fact]
    public void Targets_enums_only()
    {
        var usage = typeof(StoreAsStringAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
            .Cast<AttributeUsageAttribute>()
            .Single();
        usage.ValidOn.Should().Be(AttributeTargets.Enum);
    }
}
