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

// Cache-safe projection of ZeroAlloc.TypeConversions.ConventionResult. The discovery
// API returns `ISymbol` for the factory and `IPropertySymbol` for the value accessor,
// which would break incremental-generator equality if stored on QueryMethodModel /
// MaterializationModel. This record carries only the string fragments the emitter
// needs, so two equivalent compilations produce equal models.
//
//   Kind                -- which discovery rule matched (mirror of ConventionKind enum
//                          values, stored as int so the model stays free of a hard
//                          reference to the netstandard2.0 ZeroAlloc.TypeConversions
//                          assembly).
//   FactoryFullName     -- globally-qualified call target for non-primitive ctors,
//                          e.g. "global::TestApp.OrderId.From" for a ValueObject /
//                          StaticFactory, or "global::TestApp.OrderId" for a record
//                          ctor (caller prepends `new `). For Enum / EnumAsString
//                          this carries the enum's fully-qualified type name, which
//                          the emitter uses as a cast target ("(global::TestApp.OrderStatus)")
//                          or as the type argument to Enum.Parse<T>.
//   FactoryIsCtor       -- true when FactoryFullName names a type to be invoked with
//                          `new T(...)`; false when it names a static factory method
//                          to be invoked directly.
//   ValuePropertyName   -- name of the unwrap property (typically "Value"), used by
//                          parameter binding to emit `@id.Value`. Null for Enum
//                          conventions (the binding emits a cast or ToString instead).
//   UnderlyingReader    -- IDataReader.GetXxx method for the wrapped primitive,
//                          e.g. "GetInt32" for `OrderId(int Value)` or for a default
//                          int-backed enum; "GetString" for [StoreAsString] enums.
internal sealed record ConventionInfo(
    int Kind,
    string? FactoryFullName,
    bool FactoryIsCtor,
    string? ValuePropertyName,
    string? UnderlyingReader);

// One ctor parameter <-> one reader column. Stored as primitives + strings so the
// surrounding model stays cache-safe for incremental-generator equality.
//
//   GetterMethod -- IDataReader.GetXxx method name (e.g. "GetInt32").
//   IsNullable   -- ctor parameter type is a nullable reference or Nullable<T>;
//                   emit wraps the GetXxx call in an IsDBNull(N) guard.
//   TypeName     -- fully-qualified parameter type display, used when the emit
//                   needs to cast the null sentinel (e.g. `(int?)null`).
//   Convention   -- non-null only when the column resolves to a non-primitive
//                   convention (ValueObject, SingleArgCtor, StaticFactory). When
//                   null the emitter falls back to the primitive `reader.GetXxx(N)`
//                   shape — identical to v0.1 behavior.
//   ColumnName   -- SQL column identifier paired with the ctor parameter. Null for
//                   positional FlatRow shapes (ordinal-keyed reads via index); set
//                   for DomainEntity shapes where the emit pulls the ordinal via
//                   `__reader.GetOrdinal("ColumnName")` so SELECT column order is
//                   not load-bearing.
internal sealed record ColumnBinding(
    string GetterMethod,
    bool IsNullable,
    string TypeName,
    ConventionInfo? Convention = null,
    string? ColumnName = null);

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
// Method parameter info used to render the partial method signature and the
// per-parameter DbParameter binding block.
//   Name              -- C# parameter name (used as the local-variable suffix and
//                        as the default SQL parameter name).
//   TypeDisplay       -- fully-qualified type display incl. nullable annotation.
//   IsCancellationToken -- skip binding; CT is a runtime control signal.
//   ParamNameOverride -- when set via [Param(Name = "...")], emit uses this string
//                        verbatim as `ParameterName`. Null falls back to "@" + Name.
//   IsNullable        -- the C# parameter type is a nullable reference (`string?`)
//                        or `Nullable<T>` (`int?`); emit wraps `.Value` with a
//                        `(object?)x ?? DBNull.Value` guard.
internal sealed record ParameterInfo(
    string Name,
    string TypeDisplay,
    bool IsCancellationToken,
    string? ParamNameOverride = null,
    bool IsNullable = false,
    ConventionInfo? Convention = null);
