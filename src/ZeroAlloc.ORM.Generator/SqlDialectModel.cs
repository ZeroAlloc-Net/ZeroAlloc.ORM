namespace ZeroAlloc.ORM.Generator;

// #248 — mirror of ZeroAlloc.ORM.SqlDialect. The generator reads the declared value as a number,
// so the values must match the public enum. Re-declared here so the incremental models stay free
// of symbols. Also compiled into ZeroAlloc.ORM.Integration.Tests with DialectDbTypeRejections.
internal enum SqlDialectModel
{
    SqlServer = 0,
    PostgreSql = 1,
    Sqlite = 2,
    MySql = 3,
}
