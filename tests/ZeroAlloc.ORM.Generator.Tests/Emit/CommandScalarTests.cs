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

    // v0.4 Phase B code-review Fix 4 — lock in the Enum (default int-backed)
    // scalar emit. The classification routes ConventionKind.Enum through the
    // factory-shape branch with an `(EnumType)` cast wrapping the underlying
    // Convert.ToInt32. Snapshot pins this so the Fix 3 PrimitiveCatalog
    // consolidation cannot silently drift the emit.
    [Fact]
    public Task Scalar_Task_int_enum_emits_cast_via_int_underlying()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public enum OrderStatus { Pending = 0, Shipped = 1, Closed = 2 }

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("SELECT Status FROM Orders WHERE Id = @id", Kind = CommandKind.Scalar)]
                public partial Task<OrderStatus> GetStatusAsync(int id, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    // v0.4 Phase B code-review Fix 4 + Fix 5 — lock in the EnumAsString scalar
    // emit. ConventionKind.EnumAsString routes through Enum.Parse<T> with the
    // string sourced via BuildScalarConvertExpression("string", ...) — the same
    // Convert.ToString funnel the other branches use, so the "uniform funnel"
    // comment at the top of EmitCommandScalar holds.
    [Fact]
    public Task Scalar_Task_string_enum_emits_Enum_Parse()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            [StoreAsString]
            public enum OrderStatus { Pending, Shipped, Closed }

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("SELECT Status FROM Orders WHERE Id = @id", Kind = CommandKind.Scalar)]
                public partial Task<OrderStatus> GetStatusAsync(int id, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }
}
