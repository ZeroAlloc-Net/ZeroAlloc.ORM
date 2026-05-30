using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace ZeroAlloc.ORM.Generator.Model;

/// <summary>
/// Element-wise equatable wrapper around <see cref="ImmutableArray{T}"/>. The default
/// equality on <see cref="ImmutableArray{T}"/> is reference equality of the underlying
/// array, which defeats incremental-generator caching when models are re-built per run.
/// This wrapper compares element-by-element via <typeparamref name="T"/>'s own equality.
/// </summary>
internal readonly record struct EquatableArray<T>(ImmutableArray<T> Values) : IEnumerable<T>
    where T : IEquatable<T>
{
    public int Count => Values.IsDefault ? 0 : Values.Length;

    public int Length => Values.IsDefault ? 0 : Values.Length;

    public T this[int index] => Values[index];

    public bool Equals(EquatableArray<T> other) => Values.AsSpan().SequenceEqual(other.Values.AsSpan());

    public override int GetHashCode()
    {
        // Treat default-state and empty-state as hash-equal — Equals() already treats
        // them as equal (both yield an empty span), so the hash must agree to satisfy
        // the GetHashCode contract.
        if (Values.IsDefault || Values.Length == 0) return 0;
        var hash = 17;
        foreach (var v in Values)
        {
            hash = unchecked(hash * 31 + (v?.GetHashCode() ?? 0));
        }
        return hash;
    }

    // Struct enumerator: foreach (var x in equatableArray) binds to this overload by
    // duck-typing and avoids the boxing + heap-allocation that an IEnumerator<T> path
    // would incur. The interface-typed GetEnumerator overloads remain for LINQ and any
    // consumer holding an IEnumerable<T> reference.
    public Enumerator GetEnumerator() => new Enumerator(Values);

    IEnumerator<T> IEnumerable<T>.GetEnumerator()
    {
        var values = Values.IsDefault ? ImmutableArray<T>.Empty : Values;
        return ((IEnumerable<T>)values).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<T>)this).GetEnumerator();

    public static EquatableArray<T> Empty => new(ImmutableArray<T>.Empty);

    public struct Enumerator
    {
        private readonly ImmutableArray<T> _values;
        private int _index;

        internal Enumerator(ImmutableArray<T> values)
        {
            _values = values.IsDefault ? ImmutableArray<T>.Empty : values;
            _index = -1;
        }

        public T Current => _values[_index];

        public bool MoveNext()
        {
            var next = _index + 1;
            if (next >= _values.Length) return false;
            _index = next;
            return true;
        }
    }
}
