using System;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Abstractions.Tests;

public class QueryAttributeTests
{
    [Fact]
    public void Stores_sql_string()
    {
        var attr = new QueryAttribute("SELECT 1");
        attr.Sql.Should().Be("SELECT 1");
    }

    [Fact]
    public void FromResource_defaults_to_false()
    {
        var attr = new QueryAttribute("X");
        attr.FromResource.Should().BeFalse();
    }

    [Fact]
    public void Batch_defaults_to_Auto()
    {
        var attr = new QueryAttribute("X");
        attr.Batch.Should().Be(BatchMode.Auto);
    }

    [Fact]
    public void Targets_methods_only()
    {
        var usage = typeof(QueryAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
            .Cast<AttributeUsageAttribute>()
            .Single();
        usage.ValidOn.Should().Be(AttributeTargets.Method);
    }
}
