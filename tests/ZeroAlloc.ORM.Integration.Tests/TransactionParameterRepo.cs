using System.Data.Async;

namespace ZeroAlloc.ORM.Integration.Tests;

// v1.5 Task 7 — repo surface for the IAsyncDbTransaction parameter feature.
// Two method signatures cover the matrix exercised by TransactionParameterTests:
//
//   * InsertAsync  — NonQuery INSERT that flows the caller-supplied
//                    IAsyncDbTransaction through `__cmd.Transaction = @tx;`.
//   * CountAsync   — Scalar COUNT(*) used to observe the post-commit /
//                    post-rollback row count (no transaction parameter — the
//                    auto-commit reader sees only persisted state).
public sealed partial class TransactionParameterRepo(IAsyncDbConnection connection)
{
    [Command("INSERT INTO Things (Name) VALUES (@name)")]
    public partial Task<int> InsertAsync(string name, IAsyncDbTransaction tx, CancellationToken ct);

    [Command("SELECT COUNT(*) FROM Things", Kind = CommandKind.Scalar)]
    public partial Task<int> CountAsync(CancellationToken ct);
}
