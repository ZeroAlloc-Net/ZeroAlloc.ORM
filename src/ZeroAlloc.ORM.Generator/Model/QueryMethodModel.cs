namespace ZeroAlloc.ORM.Generator.Model;

// Classification of the emit template a [Query] method should use.
// Phase 4.1 introduces ScalarInt; future shapes (ScalarNullable, FlatRow, etc.)
// land in later Phase 4/5 tasks. Methods that don't match any known shape stay
// Unknown and emit the v0.1 stub comment.
internal enum EmitShape
{
    Unknown,
    ScalarInt,
    NullableScalar,
    FlatRow,
    // Multi-arg class with column-name-keyed reads. Detected when the return-type
    // element is a `class` (not a record) with exactly one public ctor whose params
    // all resolve to known conventions. Distinct from FlatRow because the column
    // bind uses GetOrdinal("ColumnName") instead of a positional index — class ctor
    // parameter names map to PascalCased column identifiers.
    DomainEntity,
}

// Per-method emit input. Type-scoped fields (ContainingTypeName, Namespace,
// ConnectionAccess, ConnectionResolved, ContainingTypePartial, ContainingTypeLocation)
// were hoisted to QueryRepositoryModel in R8 to remove the per-method redundancy
// and avoid the "pick Methods[0] as representative" fallback in OrmGenerator.
internal sealed record QueryMethodModel(
    string MethodName,
    string ContainingTypeFullName,
    string Sql,
    EmitShape Shape,
    BatchEmitStrategy Strategy,
    string ReturnTypeDisplay,
    string? NullableScalarReaderMethod,
    MaterializationModel? Materialization,
    EquatableArray<ParameterInfo> MethodParameters,
    string? CancellationTokenParameterName,
    EquatableArray<DiagnosticInfo> Diagnostics);

internal sealed record QueryRepositoryModel(
    string ContainingTypeFullName,
    string ContainingTypeName,
    string? Namespace,
    string ConnectionAccess,
    bool ConnectionResolved,
    bool ContainingTypePartial,
    LocationInfo? ContainingTypeLocation,
    EquatableArray<QueryMethodModel> Methods);

// Intermediate carrier emitted by TransformMethod. Bundles the method-scoped
// model with the type-scoped fields so the grouping step in OrmGenerator.Initialize
// can build a QueryRepositoryModel without re-reading symbols. Every entry in a
// group shares identical type-scoped values (same containing type), so the grouping
// just takes the first.
internal sealed record QueryMethodWithTypeContext(
    QueryMethodModel Method,
    string ContainingTypeName,
    string? Namespace,
    string ConnectionAccess,
    bool ConnectionResolved,
    bool ContainingTypePartial,
    LocationInfo? ContainingTypeLocation);
