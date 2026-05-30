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
}

// TODO(v0.2): hoist all type-scoped fields to QueryRepositoryModel:
//   - ContainingTypeName, Namespace (Phase 2.2)
//   - ConnectionAccess, ConnectionResolved, ContainingTypePartial, ContainingTypeLocation (Phase 3.3-3.10)
// Currently stored per-method as redundancy; minor cache-key bloat (N method-models
// instead of 1 type-model). Hoist requires updating snapshots which is why deferred.
internal sealed record QueryMethodModel(
    string MethodName,
    string ContainingTypeFullName,
    string ContainingTypeName,
    string? Namespace,
    string Sql,
    string ConnectionAccess,
    bool ConnectionResolved,
    bool ContainingTypePartial,
    LocationInfo? ContainingTypeLocation,
    EmitShape Shape,
    string ReturnTypeDisplay,
    string? NullableScalarReaderMethod,
    EquatableArray<DiagnosticInfo> Diagnostics);

internal sealed record QueryRepositoryModel(
    string ContainingTypeFullName,
    string ContainingTypeName,
    string? Namespace,
    EquatableArray<QueryMethodModel> Methods);
