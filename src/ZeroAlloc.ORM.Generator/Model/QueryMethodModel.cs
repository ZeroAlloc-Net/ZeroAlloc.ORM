namespace ZeroAlloc.ORM.Generator.Model;

// TODO(v0.2): ContainingTypeName and Namespace are now redundant with the hoisted
// fields on QueryRepositoryModel. Keeping them here for now to avoid a cascade of
// test updates and to keep the grouping pipeline intact; revisit once Task 2.3 / 3.x
// add code that genuinely needs per-method namespace data.
internal sealed record QueryMethodModel(
    string MethodName,
    string ContainingTypeFullName,
    string ContainingTypeName,
    string? Namespace,
    string Sql);

internal sealed record QueryRepositoryModel(
    string ContainingTypeFullName,
    string ContainingTypeName,
    string? Namespace,
    EquatableArray<QueryMethodModel> Methods);
