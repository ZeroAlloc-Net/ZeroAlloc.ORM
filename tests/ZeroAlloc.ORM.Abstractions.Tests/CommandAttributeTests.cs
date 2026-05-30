using System;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Abstractions.Tests;

public class CommandAttributeTests
{
    [Fact]
    public void Stores_sql_string()
    {
        var attr = new CommandAttribute("DELETE FROM t");
        attr.Sql.Should().Be("DELETE FROM t");
    }

    [Fact]
    public void FromResource_defaults_to_false()
    {
        var attr = new CommandAttribute("X");
        attr.FromResource.Should().BeFalse();
    }

    [Fact]
    public void Kind_defaults_to_NonQuery()
    {
        var attr = new CommandAttribute("X");
        attr.Kind.Should().Be(CommandKind.NonQuery);
    }

    [Fact]
    public void Targets_methods_only()
    {
        var usage = typeof(CommandAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
            .Cast<AttributeUsageAttribute>()
            .Single();
        usage.ValidOn.Should().Be(AttributeTargets.Method);
    }
}
