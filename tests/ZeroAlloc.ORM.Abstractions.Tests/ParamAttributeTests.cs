using System;
using System.Data;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Abstractions.Tests;

public class ParamAttributeTests
{
    [Fact]
    public void Name_defaults_to_null()
    {
        var attr = new ParamAttribute();
        attr.Name.Should().BeNull();
    }

    [Fact]
    public void DbType_defaults_to_Object()
    {
        var attr = new ParamAttribute();
        attr.DbType.Should().Be(DbType.Object);
    }

    [Fact]
    public void Targets_parameters_only()
    {
        var usage = typeof(ParamAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
            .Cast<AttributeUsageAttribute>()
            .Single();
        usage.ValidOn.Should().Be(AttributeTargets.Parameter);
    }
}
