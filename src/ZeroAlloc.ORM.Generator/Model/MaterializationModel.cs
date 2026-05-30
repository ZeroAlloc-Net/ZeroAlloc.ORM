namespace ZeroAlloc.ORM.Generator.Model;

// Kind of row-to-CLR materialization the generator should emit for a query method's
// result type. v0.1 implements FlatRow (positional record with primitive ctor params);
// ScalarPrimitive is conceptually present for symmetry but ScalarInt/NullableScalar
// shapes still represent the scalar cases. DomainEntity/Custom land in v0.2+.
internal enum MaterializationKind
{
    ScalarPrimitive,
    FlatRow,
    DomainEntity,
    Custom,
}

// One ctor parameter <-> one reader column. Stored as primitives + strings so the
// surrounding model stays cache-safe for incremental-generator equality.
//
//   GetterMethod -- IDataReader.GetXxx method name (e.g. "GetInt32").
//   IsNullable   -- ctor parameter type is a nullable reference or Nullable<T>;
//                   emit wraps the GetXxx call in an IsDBNull(N) guard.
//   TypeName     -- fully-qualified parameter type display, used when the emit
//                   needs to cast the null sentinel (e.g. `(int?)null`).
internal sealed record ColumnBinding(
    string GetterMethod,
    bool IsNullable,
    string TypeName);

// Materialization plan for a single [Query] method's return row.
//
//   TargetTypeFullName -- globally-qualified target type to construct (positional record).
//   Columns            -- ordinal-positioned ctor-param bindings.
//
// Cache-safe: record + primitives + EquatableArray<ColumnBinding>.
internal sealed record MaterializationModel(
    MaterializationKind Kind,
    string TargetTypeFullName,
    EquatableArray<ColumnBinding> Columns);

// Method parameter info used to render the partial method signature.
// Excludes CancellationToken (passed through separately so the emit body can call
// `await ...Async(ct)` without having to scan the parameter list at emit time).
internal sealed record ParameterInfo(string Name, string TypeDisplay);
