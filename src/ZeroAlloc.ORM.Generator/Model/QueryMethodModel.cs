namespace ZeroAlloc.ORM.Generator.Model;

// TODO(v0.2): ContainingTypeName, Namespace, and ConnectionAccess are now redundant
// across methods of the same containing type. ContainingTypeName/Namespace are already
// hoisted to QueryRepositoryModel; ConnectionAccess will follow in v0.2 once the real
// body-emit code (Phase 4) lands and we can refactor the grouping pipeline without a
// cascade of snapshot churn.
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
