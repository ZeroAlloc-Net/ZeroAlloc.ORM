namespace ZeroAlloc.ORM.AotSmoke;

// User struct single-column value object, bound through the single-arg-ctor convention.
public readonly record struct Sku(int Value);
