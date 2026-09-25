namespace ZeroAlloc.ORM.Integration.Tests;

// #249 — a value object whose ctor rejects a value with InvalidOperationException,
// one of the exception types the NULL-column catch filter inspects. Reading a
// non-NULL value it rejects must surface this exception unchanged: no column is
// NULL, so the filter lets it through.
public readonly record struct CheckedQuantity
{
    public CheckedQuantity(int value)
    {
        if (value < 5)
            throw new InvalidOperationException($"Quantity {value} is below the minimum of 5.");
        Value = value;
    }

    public int Value { get; }
}
