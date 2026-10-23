namespace ZeroAlloc.ORM.Generator.Tests;

// The test project does not reference Npgsql. Generator features gated on Npgsql,
// such as PostgreSQL arrays and [Json], detect it by looking up
// `Npgsql.NpgsqlParameter` in the compilation, so a test that wants the
// Npgsql-present path adds this stub as a second source tree:
// `GeneratorHarness.RunGenerator(source, NpgsqlStub.Source)`.
//
// NpgsqlParameter is not sealed, so emitted `p is Npgsql.NpgsqlParameter` checks
// against an IDbDataParameter still compile.
internal static class NpgsqlStub
{
    public const string Source = """
        namespace Npgsql { public class NpgsqlParameter { public NpgsqlTypes.NpgsqlDbType NpgsqlDbType { get; set; } } }
        namespace NpgsqlTypes { public enum NpgsqlDbType { Jsonb = 14, Xid = 42 } }
        """;
}
