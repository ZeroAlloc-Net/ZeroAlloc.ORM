using System;
using FluentAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Tests;

public class ExceptionTests
{
    [Fact]
    public void Materialization_takes_message()
    {
        var ex = new ZeroAllocOrmMaterializationException("boom");
        ex.Message.Should().Be("boom");
    }

    [Fact]
    public void Materialization_wraps_inner()
    {
        var inner = new InvalidOperationException("root");
        var ex = new ZeroAllocOrmMaterializationException("wrap", inner);
        ex.Message.Should().Be("wrap");
        ex.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void VersionMismatch_takes_message()
    {
        var ex = new ZeroAllocOrmVersionMismatchException("mismatch");
        ex.Message.Should().Be("mismatch");
    }
}
