namespace ZeroAlloc.ORM.Generator.Model;

// #248 — `[Dialect(SqlDialect.X)]` on a repository type. Dialect is the declared
// SqlDialect value; TypeFullName matches QueryRepositoryModel.ContainingTypeFullName.
internal sealed record TypeDialect(string TypeFullName, int Dialect);

// #248 — every declared dialect in the compilation: one entry per repository type
// that carries `[Dialect]`, and the assembly-level default, null when there is none.
internal sealed record DialectDeclarations(
    EquatableArray<TypeDialect> Types,
    int? AssemblyDefault);

// #248 — a repository and the dialect that applies to it: its own `[Dialect]`, else
// the assembly default, else null.
internal sealed record RepositoryWithDialect(
    QueryRepositoryModel Repository,
    int? Dialect);
