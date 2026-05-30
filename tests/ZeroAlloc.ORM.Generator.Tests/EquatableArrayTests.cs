using System.Collections.Immutable;
using Xunit;
using ZeroAlloc.ORM.Generator.Model;

namespace ZeroAlloc.ORM.Generator.Tests;

// Hash/equality invariants for EquatableArray<T>. The default-state and empty-state
// must compare equal (and therefore hash equal) because both project to an empty span;
// element-wise hashing must distinguish arrays whose contents differ.
public class EquatableArrayTests
{
    [Fact]
    public void Default_equals_empty()
    {
        var def = default(EquatableArray<int>);
        var empty = EquatableArray<int>.Empty;
        Assert.True(def.Equals(empty));
    }

    [Fact]
    public void Default_and_empty_hash_match()
    {
        var def = default(EquatableArray<int>);
        var empty = EquatableArray<int>.Empty;
        Assert.Equal(def.GetHashCode(), empty.GetHashCode());
    }

    [Fact]
    public void Elementwise_hashes_match_when_elements_match()
    {
        var a = new EquatableArray<int>(ImmutableArray.Create(1, 2, 3));
        var b = new EquatableArray<int>(ImmutableArray.Create(1, 2, 3));
        var c = new EquatableArray<int>(ImmutableArray.Create(1, 2, 4));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a.GetHashCode(), c.GetHashCode());
    }
}
