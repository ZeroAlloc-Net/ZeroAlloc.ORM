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
// Includes the CancellationToken parameter so we can preserve the user's original
// parameter ordering — if the user declares `(CancellationToken ct, int id)` the
// emitted partial must match verbatim, otherwise partial-method matching fails
// (CS8795/CS0759). The IsCancellationToken flag lets emit reference the CT by the
// user's chosen name (e.g. `await ...Async(cancellationToken)`).
internal sealed record ParameterInfo(string Name, string TypeDisplay, bool IsCancellationToken);
