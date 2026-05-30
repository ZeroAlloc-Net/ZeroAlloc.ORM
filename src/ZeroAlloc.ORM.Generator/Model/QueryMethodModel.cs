using System.Collections.Immutable;

namespace ZeroAlloc.ORM.Generator.Model;

internal sealed record QueryMethodModel(
    string MethodName,
    string ContainingTypeFullName,
    string ContainingTypeName,
    string? Namespace,
    string Sql);

internal sealed record QueryRepositoryModel(
    string ContainingTypeFullName,
    ImmutableArray<QueryMethodModel> Methods);
