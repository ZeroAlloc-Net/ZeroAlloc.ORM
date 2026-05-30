using System;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Abstractions.Tests;

public class StoredProcedureAttributeTests
{
    [Fact]
    public void Stores_procedure_name()
    {
        var attr = new StoredProcedureAttribute("dbo.GetOrderById");
        attr.ProcedureName.Should().Be("dbo.GetOrderById");
    }

    [Fact]
    public void Batch_defaults_to_Never()
    {
        var attr = new StoredProcedureAttribute("dbo.X");
        attr.Batch.Should().Be(BatchMode.Never);
    }

    [Fact]
    public void Targets_methods_only()
    {
        var usage = typeof(StoredProcedureAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
            .Cast<AttributeUsageAttribute>()
            .Single();
        usage.ValidOn.Should().Be(AttributeTargets.Method);
    }
}
