namespace ZeroAlloc.ORM.Generator.Model;

// Kind of row-to-CLR materialization the generator should emit for a query method's
// result type. v0.1 implements FlatRow (positional record with primitive ctor params);
// ScalarPrimitive is conceptually present for symmetry but ScalarInt/NullableScalar
// shapes still represent the scalar cases. DomainEntity/Custom land in v0.2+.
//
// v0.5 Phase A — Composite is a scalar-position multi-column type (Money(decimal,
// string) returned as Task<Money>). The MaterializationModel.Columns carries the
// flattened inner-column list; the emitter produces `new T(reader.GetXxx(0), ...)`.
// Nested composites (`record OrderRow(int Id, Money Total)`) reuse FlatRow with
// ColumnBinding.InnerColumns populated for the composite ctor parameter.
internal enum MaterializationKind
{
    ScalarPrimitive,
    FlatRow,
    DomainEntity,
    Custom,
    Composite,
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
//   TypeName     -- fully-qualified parameter type display, UNWRAPPED (no trailing
//                   `?`, reference-nullability annotation stripped). The emit
//                   appends `?` based on IsNullable when it needs to cast a null
//                   sentinel (e.g. `({TypeName}?)null`). Stored unwrapped so the
//                   scalar-emit cast target maps directly without re-stripping.
//   Convention   -- non-null only when the column resolves to a non-primitive
//                   convention (ValueObject, SingleArgCtor, StaticFactory). When
//                   null the emitter falls back to the primitive `reader.GetXxx(N)`
//                   shape — identical to v0.1 behavior.
//   ColumnName   -- SQL column identifier paired with the ctor parameter. Null for
//                   positional FlatRow shapes (ordinal-keyed reads via index); set
//                   for DomainEntity shapes where the emit pulls the ordinal via
//                   `__reader.GetOrdinal("ColumnName")` so SELECT column order is
//                   not load-bearing.
//   InnerColumns -- v0.5 Phase A: non-empty when this ctor parameter is itself a
//                   composite (MultiArgCtor) type — the column expands into a sub-binding
//                   list. The emitter renders `new T(reader.GetXxx(ord+0), reader.GetXxx(ord+1), ...)`
//                   instead of a single read. GetterMethod / TypeName / Convention
//                   describe the composite TYPE (TypeName is the composite's FQN, used
//                   as the ctor target via `new <TypeName>(...)`); the actual primitive
//                   reads live on the inner ColumnBinding entries. Recursive composites
//                   (an InnerColumn entry itself has InnerColumns) are rejected upstream
//                   in v0.5 — ConventionDiscovery's MultiArgCtor rule refuses to classify
//                   them. Flat one-level expansion is the v0.5 contract.
// EquatableArray<ColumnBinding>.default is IsDefault==true with zero heap allocation;
// non-composite leaf bindings carry this field for free (no per-binding empty-array
// instance is materialized when InnerColumns is unused).
internal sealed record ColumnBinding(
    string GetterMethod,
    bool IsNullable,
    string TypeName,
    ConventionInfo? Convention = null,
    string? ColumnName = null,
    EquatableArray<ColumnBinding> InnerColumns = default);

// Materialization plan for a single [Query] method's return row.
//
//   TargetTypeFullName -- globally-qualified target type to construct (positional record).
//   Columns            -- ordinal-positioned ctor-param bindings.
//   IsNullable         -- v0.5 Phase C: the materialization target is itself nullable
//                         (e.g. Task<Money?> at scalar position). Only meaningful on
//                         Composite materializations today — Phase C's all-or-nothing
//                         DBNull contract reads one IsDBNull per inner column,
//                         returns null when ALL are DBNull, and throws when ANY (but
//                         not ALL) are DBNull. Non-composite materializations (FlatRow
//                         / DomainEntity) carry their own nullable handling on the
//                         outer Task<T?> shape (return null on empty result) and
//                         leave this flag false.
//
// Cache-safe: record + primitives + EquatableArray<ColumnBinding>.
internal sealed record MaterializationModel(
    MaterializationKind Kind,
    string TargetTypeFullName,
    EquatableArray<ColumnBinding> Columns,
    bool IsNullable = false);

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
    ConventionInfo? Convention = null,
    // v0.5 Phase B — composite parameter binding. Non-default when the parameter's
    // type resolves to ConventionKind.MultiArgCtor (e.g. `Money(decimal Amount,
    // string Currency)`). The binding emitter walks this list to produce one
    // DbParameter per inner field, named `@{Name}_{Field.CtorArgName}`. When
    // default (empty / IsDefault), the parameter binds via the primitive /
    // VO / enum path captured in Convention.
    //
    // CompositeTypeFullName is the globally-qualified composite type display
    // (e.g. "global::TestApp.Money"); the emitter uses it only for the
    // classifier sentinel comment. The unpacking accessor (`@total.Amount`)
    // derives from the C# parameter name and each field's PascalCased property
    // name — records auto-generate properties matching ctor arg names.
    EquatableArray<CompositeBindingField> CompositeFields = default,
    string? CompositeTypeFullName = null);

// v0.5 Phase B — one inner field of a composite parameter (e.g. `Amount` /
// `Currency` of a `Money(decimal Amount, string Currency)` parameter).
// Stored as primitives + strings + an optional ConventionInfo so the surrounding
// model stays cache-safe for incremental-generator equality.
//
//   CtorArgName       -- the ctor parameter NAME on the composite type, used to
//                        derive the emitted DbParameter name suffix
//                        (`@{paramName}_{CtorArgName}`). Composite records expose
//                        a matching property of the same name (PascalCase
//                        matches by C# convention), so the unpacking accessor
//                        is `@{paramName}.@{CtorArgName}` (the `@`-prefix on
//                        the property is defensive — keyword names like
//                        `@class` need it; ordinary identifiers are unaffected).
//   IsNullable        -- inner ctor parameter is nullable (`int?` /
//                        `Nullable<int>` / annotated reference type). Emit
//                        routes through DBNull.Value when true.
//   Convention        -- non-null when the inner field is a ValueObject /
//                        SingleArgCtor / StaticFactory / Enum / EnumAsString —
//                        mirrors the materialization-side recursive unwrap.
//                        Null for primitive fields.
internal sealed record CompositeBindingField(
    string CtorArgName,
    bool IsNullable,
    ConventionInfo? Convention);
