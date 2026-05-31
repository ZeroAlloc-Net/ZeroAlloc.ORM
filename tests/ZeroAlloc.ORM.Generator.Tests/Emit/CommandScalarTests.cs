using System.Threading.Tasks;
using VerifyXunit;
using Xunit;
using static VerifyXunit.Verifier;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// v0.4 Phase B.1 — [Command(Kind = Scalar)] emit shape. Four coverage cells span
// the type conversion matrix:
//
//   * Task<int>              — COUNT(*) over a primitive cast from ExecuteScalarAsync.
//   * Task<decimal>          — SUM aggregate over a non-nullable primitive.
//   * Task<decimal?>         — SUM on empty table, DBNull/null guard returning null.
//   * Task<OrderId>          — record OrderId(int Value) — value-object via convention
//                              discovery; emit wraps `(int)__result!` in `new OrderId(...)`.
//
// All four share the open-on-execute / close-on-finally lifecycle, parameter
// binding via EmitParameterBinding, and the `await using var __cmd` pattern. The
// materialization step differs by type: primitive vs. nullable-primitive vs.
// value-object factory invocation.
public class CommandScalarTests
{
    [Fact]
    public Task Scalar_Task_int_emits_ExecuteScalar_with_int_cast()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("SELECT COUNT(*) FROM Orders", Kind = CommandKind.Scalar)]
                public partial Task<int> CountAsync(CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task Scalar_Task_decimal_emits_ExecuteScalar_with_decimal_cast()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("SELECT SUM(Total) FROM Orders WHERE CustomerId = @customerId", Kind = CommandKind.Scalar)]
                public partial Task<decimal> GetTotalAsync(int customerId, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task Scalar_Task_nullable_decimal_emits_DBNull_guard_and_null_return()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("SELECT SUM(Total) FROM Orders", Kind = CommandKind.Scalar)]
                public partial Task<decimal?> GetTotalOrNullAsync(CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task Scalar_Task_value_object_emits_factory_wrap_around_scalar_cast()
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
                [Command("SELECT MAX(Id) FROM Orders", Kind = CommandKind.Scalar)]
                public partial Task<OrderId> GetMaxIdAsync(CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }
}
