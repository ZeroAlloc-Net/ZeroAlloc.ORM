using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests.Postgres;

// v2.0, #219 — a [StoredProcedure] with one input and two OUT parameters.
// For CommandType.StoredProcedure Npgsql writes the call itself, naming each
// argument from the parameter's name with any `@` or `:` trimmed:
// `CALL scale_proc("amount" := $1, "doubled" := NULL, "tripled" := NULL)`.
// The OUT values come back as a result row whose columns Npgsql matches to
// the output parameters by that same trimmed name.
//
// All-lowercase names because Postgres folds the procedure's unquoted
// parameter names; see StoredProcedureRepo for the full note.
public sealed partial class PostgresParameterBindingProcedureRepo(IAsyncDbConnection connection)
{
    [StoredProcedure("scale_proc")]
    public partial Task<(int Doubled, int Tripled)> ScaleAsync(
        int amount,
        int doubled,
        int tripled,
        CancellationToken ct);
}
