using System.Threading.Tasks;
using VerifyXunit;
using Xunit;
using static VerifyXunit.Verifier;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// v0.4 Phase C.1 — [Command(Kind = Identity)] emit shape. Three coverage cells
// span the identity return-type matrix:
//
//   * Task<int>              — Sqlite / Postgres `... RETURNING Id` mapped to int.
//   * Task<long>             — SQL Server bigint / Postgres bigserial RETURNING.
//   * Task<OrderId>          — record OrderId(int Value) — value-object wrapping the
//                              integer key via convention discovery.
//
// All three share the open-on-execute / close-on-finally lifecycle, parameter
// binding via EmitParameterBindingWithIndent, and the `await using var __cmd`
// pattern. Identity is NEVER nullable — the null guard always throws (the
// design contract is that the SQL MUST include a RETURNING / SCOPE_IDENTITY()
// clause that produces a non-null value).
public class CommandIdentityTests
{
    [Fact]
    public Task Identity_Task_int_emits_ExecuteScalar_with_int_cast()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO Orders (CustomerId, Total) VALUES (@cust, @total) RETURNING Id", Kind = CommandKind.Identity)]
                public partial Task<int> InsertOrderAsync(int cust, decimal total, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task Identity_Task_long_emits_ExecuteScalar_with_long_cast()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO Orders (CustomerId, Total) VALUES (@cust, @total) RETURNING Id", Kind = CommandKind.Identity)]
                public partial Task<long> InsertOrderAsync(int cust, decimal total, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task Identity_Task_value_object_emits_factory_wrap_around_scalar_cast()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public readonly partial record struct OrderId(int Value);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO Orders (CustomerId, Total) VALUES (@cust, @total) RETURNING Id", Kind = CommandKind.Identity)]
                public partial Task<OrderId> InsertOrderAsync(int cust, decimal total, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }
}
