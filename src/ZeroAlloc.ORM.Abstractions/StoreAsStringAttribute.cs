namespace ZeroAlloc.ORM;

/// <summary>
/// Forces enum round-trip as string rather than the underlying integer.
/// Applied at the enum type level so every read/write of that enum agrees on wire format.
/// </summary>
[AttributeUsage(AttributeTargets.Enum)]
public sealed class StoreAsStringAttribute : Attribute;
