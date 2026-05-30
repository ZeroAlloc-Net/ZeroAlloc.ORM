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
    public void Materialization_parameterless_ctor()
    {
        // R11 — symmetry: every BCL-style exception ships a parameterless ctor so
        // adopters can `new T()` without binding to a specific message. The check
        // also documents that the default message is BCL-generated, not empty,
        // so consumers don't accidentally treat null-or-empty as "no message."
        var ex = new ZeroAllocOrmMaterializationException();
        ex.Message.Should().NotBeNullOrEmpty();
        ex.InnerException.Should().BeNull();
    }

    [Fact]
    public void VersionMismatch_takes_message()
    {
        var ex = new ZeroAllocOrmVersionMismatchException("mismatch");
        ex.Message.Should().Be("mismatch");
    }

    [Fact]
    public void VersionMismatch_wraps_inner()
    {
        var inner = new InvalidOperationException("root");
        var ex = new ZeroAllocOrmVersionMismatchException("wrap", inner);
        ex.Message.Should().Be("wrap");
        ex.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void VersionMismatch_parameterless_ctor()
    {
        var ex = new ZeroAllocOrmVersionMismatchException();
        ex.Message.Should().NotBeNullOrEmpty();
        ex.InnerException.Should().BeNull();
    }
}
