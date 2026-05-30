namespace ZeroAlloc.ORM.Generator.Model;

internal sealed record QueryMethodModel(
    string MethodName,
    string ContainingTypeFullName,
    string ContainingTypeName,
    string? Namespace,
    string Sql);

internal sealed record QueryRepositoryModel(
    string ContainingTypeFullName,
    EquatableArray<QueryMethodModel> Methods);
