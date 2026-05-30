using System;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Abstractions.Tests;

public class MaterializeAttributeTests
{
    [Fact]
    public void Strategy_defaults_to_Auto()
    {
        var attr = new MaterializeAttribute();
        attr.Strategy.Should().Be(MaterializeStrategy.Auto);
    }

    [Fact]
    public void Factory_defaults_to_null()
    {
        var attr = new MaterializeAttribute();
        attr.Factory.Should().BeNull();
    }

    [Fact]
    public void Targets_return_value_class_and_struct()
    {
        var usage = typeof(MaterializeAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        const AttributeTargets expected =
            AttributeTargets.ReturnValue
            | AttributeTargets.Class
            | AttributeTargets.Struct;

        usage.ValidOn.Should().Be(expected);
    }
}
