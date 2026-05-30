namespace ZeroAlloc.ORM.Generator.Model;

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
    EquatableArray<DiagnosticInfo> Diagnostics);

internal sealed record QueryRepositoryModel(
    string ContainingTypeFullName,
    string ContainingTypeName,
    string? Namespace,
    EquatableArray<QueryMethodModel> Methods);
