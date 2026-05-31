using System.Linq;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

public class CompileSmokeTests
{
    [Fact]
    public void Scalar_int_emit_compiles_cleanly()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT 1")]
                public partial Task<int> GetOneAsync(CancellationToken ct);
            }
            """;
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);

        // Filter to errors the generator could be responsible for.
        // Some baseline errors from missing references may be unavoidable in the test harness;
        // verify nothing matches the primary-ctor capture bug pattern (CS1061/CS0103/CS9113).
        var bugClass = compileDiagnostics
            .AsEnumerable()
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113")
            .ToArray();
        Assert.Empty(bugClass);
    }

    [Fact]
    public void Nullable_scalar_emit_compiles_cleanly()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Name FROM Users LIMIT 1")]
                public partial Task<string?> GetNameAsync(CancellationToken ct);
            }
            """;
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);

        // Same bug-class filter as Scalar_int_emit_compiles_cleanly: primary-ctor
        // capture/missing-member style errors that would indicate a generator bug
        // rather than a missing reference in the harness.
        var bugClass = compileDiagnostics
            .AsEnumerable()
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113")
            .ToArray();
        Assert.Empty(bugClass);
    }

    [Fact]
    public void FlatRow_emit_compiles_cleanly()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderRow(int Id, int CustomerId, decimal Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id")]
                public partial Task<OrderRow?> GetByIdAsync(int id, CancellationToken ct);
            }
            """;
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);

        // Same bug-class filter as the other smoke tests. The unbound @id parameter
        // in the SQL is a runtime concern (resolved by Phase 6 binding); it doesn't
        // surface as CS1061/CS0103/CS9113 at compile time so the smoke test stays
        // green even though parameter binding hasn't landed yet.
        var bugClass = compileDiagnostics
            .AsEnumerable()
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113")
            .ToArray();
        Assert.Empty(bugClass);
    }

    [Fact]
    public void FlatRow_preserves_user_parameter_order()
    {
        // Regression: BuildParameterList used to always append CancellationToken last,
        // ignoring the user's declared parameter order. Declarations like
        // `(CancellationToken ct, int id)` produced mismatched partials (CS8795/CS0759).
        // The generator must emit the parameter list verbatim in the user's order.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderRow(int Id, int CustomerId, decimal Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id")]
                public partial Task<OrderRow?> GetByIdAsync(CancellationToken ct, int id);
            }
            """;
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);

        var partialMismatch = compileDiagnostics
            .AsEnumerable()
            .Where(d => d.Id is "CS8795" or "CS0759" or "CS1061" or "CS0103")
            .ToArray();
        Assert.Empty(partialMismatch);
    }

    [Fact]
    public void Param_name_override_emit_compiles_cleanly()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT 1 WHERE @orderId = 42")]
                public partial Task<int> SearchAsync([Param(Name = "@orderId")] int id, CancellationToken ct);
            }
            """;
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);
        var bugClass = compileDiagnostics
            .AsEnumerable()
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113")
            .ToArray();
        Assert.Empty(bugClass);
    }

    [Fact]
    public void Nullable_parameter_emit_compiles_cleanly()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT 1 WHERE @id IS NULL OR @id = 0")]
                public partial Task<int> SearchAsync(int? id, CancellationToken ct);
            }
            """;
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);
        var bugClass = compileDiagnostics
            .AsEnumerable()
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113")
            .ToArray();
        Assert.Empty(bugClass);
    }

    [Fact]
    public void Keyword_parameter_name_emit_compiles_cleanly()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                // 'class' is a C# keyword; the generator must escape its reference in the emitted body.
                [Query("SELECT 1 WHERE @class = 1")]
                public partial Task<int> SearchAsync(int @class, CancellationToken ct);
            }
            """;
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);
        var bugClass = compileDiagnostics
            .AsEnumerable()
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113" or "CS1525")
            .ToArray();
        Assert.Empty(bugClass);
    }

    [Fact]
    public void Extended_primitive_types_emit_compiles_cleanly()
    {
        var source = """
            using System;
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record TimestampedRow(int Id, DateTimeOffset CreatedAt, TimeSpan Duration, byte[] Payload);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, CreatedAt, Duration, Payload FROM Events LIMIT 1")]
                public partial Task<TimestampedRow?> GetLatestAsync(CancellationToken ct);
            }
            """;
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);
        var bugClass = compileDiagnostics
            .AsEnumerable()
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113")
            .ToArray();
        Assert.Empty(bugClass);
    }

    [Fact]
    public void Scalar_emit_honors_user_cancellation_token_name()
    {
        // Regression: scalar emitters used to hardcode `(CancellationToken ct)` and
        // reference `ct` in the body. A user who named their CT parameter
        // `cancellationToken` would see a partial-signature/name mismatch.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT 1")]
                public partial Task<int> GetOneAsync(CancellationToken cancellationToken);
            }
            """;
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);

        var bugClass = compileDiagnostics
            .AsEnumerable()
            .Where(d => d.Id is "CS0103" or "CS8795" or "CS0759" or "CS1061")
            .ToArray();
        Assert.Empty(bugClass);
    }

    [Fact]
    public void Static_factory_materialization_emit_compiles_cleanly()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public readonly struct Score
            {
                public int Value { get; }
                private Score(int v) { Value = v; }
                public static Score From(int value) => new(value);
            }

            public sealed record GameRow(int Id, Score Score);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, Score FROM Games LIMIT 1")]
                public partial Task<GameRow?> GetFirstAsync(CancellationToken ct);
            }
            """;
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);
        var bugClass = compileDiagnostics
            .AsEnumerable()
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113")
            .ToArray();
        Assert.Empty(bugClass);
    }

    [Fact]
    public void Single_arg_record_emit_compiles_cleanly()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public readonly partial record struct OrderId(int Value);

            public sealed record OrderRow(OrderId Id, decimal Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, Total FROM Orders WHERE Id = @id")]
                public partial Task<OrderRow?> GetByIdAsync(OrderId id, CancellationToken ct);
            }
            """;
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);
        var bugClass = compileDiagnostics
            .AsEnumerable()
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113")
            .ToArray();
        Assert.Empty(bugClass);
    }

    [Fact]
    public void ValueObject_parameter_binding_emit_compiles_cleanly()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;
            using ZeroAlloc.ValueObjects;

            namespace TestApp;

            [ValueObject]
            public readonly partial struct OrderId
            {
                public int Value { get; }
                public OrderId(int v) { Value = v; }
                public static OrderId From(int value) => new(value);
            }

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT 1 WHERE @id = 42")]
                public partial Task<int> SearchAsync(OrderId id, CancellationToken ct);
            }
            """;
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);
        var bugClass = compileDiagnostics
            .AsEnumerable()
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113")
            .ToArray();
        Assert.Empty(bugClass);
    }

    [Fact]
    public void ValueObject_materialization_emit_compiles_cleanly()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;
            using ZeroAlloc.ValueObjects;

            namespace TestApp;

            [ValueObject]
            public readonly partial struct OrderId
            {
                public int Value { get; }
                public OrderId(int v) { Value = v; }
                public static OrderId From(int value) => new(value);
            }

            public sealed record OrderRow(OrderId Id, decimal Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, Total FROM Orders LIMIT 1")]
                public partial Task<OrderRow?> GetFirstAsync(CancellationToken ct);
            }
            """;
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);
        var bugClass = compileDiagnostics
            .AsEnumerable()
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113")
            .ToArray();
        Assert.Empty(bugClass);
    }

    [Fact]
    public void Enum_int_round_trip_emit_compiles_cleanly()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public enum OrderStatus { Pending, Cancelled }

            public sealed record OrderRow(int Id, OrderStatus Status);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, Status FROM Orders WHERE Status = @status LIMIT 1")]
                public partial Task<OrderRow?> SearchAsync(OrderStatus status, CancellationToken ct);
            }
            """;
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);
        var bugClass = compileDiagnostics
            .AsEnumerable()
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113" or "CS0030" or "CS0266")
            .ToArray();
        Assert.Empty(bugClass);
    }

    [Fact]
    public void Enum_StoreAsString_round_trip_emit_compiles_cleanly()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            [StoreAsString]
            public enum OrderStatus { Pending, Cancelled }

            public sealed record OrderRow(int Id, OrderStatus Status);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, Status FROM Orders WHERE Status = @status LIMIT 1")]
                public partial Task<OrderRow?> SearchAsync(OrderStatus status, CancellationToken ct);
            }
            """;
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);
        var bugClass = compileDiagnostics
            .AsEnumerable()
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113" or "CS0030" or "CS0266")
            .ToArray();
        Assert.Empty(bugClass);
    }

    [Fact]
    public void DomainEntity_class_emit_compiles_cleanly()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed class Order
            {
                public int Id { get; }
                public int CustomerId { get; }
                public decimal Total { get; }
                public Order(int id, int customerId, decimal total) =>
                    (Id, CustomerId, Total) = (id, customerId, total);
            }

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id")]
                public partial Task<Order?> GetByIdAsync(int id, CancellationToken ct);
            }
            """;
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);
        var bugClass = compileDiagnostics
            .AsEnumerable()
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113" or "CS0266")
            .ToArray();
        Assert.Empty(bugClass);
    }

    [Fact]
    public void MultiResultSet_batch_emit_compiles_cleanly()
    {
        // v0.3 Phase B.2 — IAsyncDbBatch path must produce a body that compiles. The
        // emitted SQL parameters are bound per-statement; the tuple return ctor is
        // assembled from per-element locals. CS9113 is included in the bug-class
        // filter because the primary-ctor capture has historically tripped here.
        var source =
            "using System.Collections.Generic;\n" +
            "using System.Data.Async;\n" +
            "using System.Threading;\n" +
            "using System.Threading.Tasks;\n" +
            "using ZeroAlloc.ORM;\n" +
            "\n" +
            "namespace TestApp;\n" +
            "\n" +
            "public sealed record OrderRow(int Id, int CustomerId, decimal Total);\n" +
            "public sealed record OrderLineRow(string Sku, int Quantity);\n" +
            "\n" +
            "public sealed partial class Repo(IAsyncDbConnection connection)\n" +
            "{\n" +
            "    [Query(\"SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id; SELECT Sku, Quantity FROM OrderLines WHERE OrderId = @id;\", Batch = BatchMode.Always)]\n" +
            "    public partial Task<(OrderRow Head, List<OrderLineRow> Lines)?> GetWithLinesAsync(int id, CancellationToken ct);\n" +
            "}\n";
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);
        var bugClass = compileDiagnostics
            .AsEnumerable()
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113" or "CS8795" or "CS0759")
            .ToArray();
        Assert.Empty(bugClass);
    }

    [Fact]
    public void MultiResultSet_joined_emit_compiles_cleanly()
    {
        // v0.3 Phase B.3 — ;-joined fallback path must compile. Single command, one
        // parameter binding, but the body still walks NextResultAsync between
        // tuple-element materializations.
        var source =
            "using System.Collections.Generic;\n" +
            "using System.Data.Async;\n" +
            "using System.Threading;\n" +
            "using System.Threading.Tasks;\n" +
            "using ZeroAlloc.ORM;\n" +
            "\n" +
            "namespace TestApp;\n" +
            "\n" +
            "public sealed record OrderRow(int Id, int CustomerId, decimal Total);\n" +
            "public sealed record OrderLineRow(string Sku, int Quantity);\n" +
            "\n" +
            "public sealed partial class Repo(IAsyncDbConnection connection)\n" +
            "{\n" +
            "    [Query(\"SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id; SELECT Sku, Quantity FROM OrderLines WHERE OrderId = @id;\", Batch = BatchMode.Never)]\n" +
            "    public partial Task<(OrderRow Head, List<OrderLineRow> Lines)?> GetWithLinesAsync(int id, CancellationToken ct);\n" +
            "}\n";
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);
        var bugClass = compileDiagnostics
            .AsEnumerable()
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113" or "CS8795" or "CS0759")
            .ToArray();
        Assert.Empty(bugClass);
    }

    [Fact]
    public void MultiResultSet_autobatch_emit_compiles_cleanly()
    {
        // v0.3 Phase B.4 — BatchWithFallback emit branches on __conn.CanCreateBatch.
        // Both inner branches must compile; the bug-class filter would catch a
        // mis-rendered IsAsyncDbBatch identifier or a misplaced try/finally close.
        var source =
            "using System.Collections.Generic;\n" +
            "using System.Data.Async;\n" +
            "using System.Threading;\n" +
            "using System.Threading.Tasks;\n" +
            "using ZeroAlloc.ORM;\n" +
            "\n" +
            "namespace TestApp;\n" +
            "\n" +
            "public sealed record OrderRow(int Id, int CustomerId, decimal Total);\n" +
            "public sealed record OrderLineRow(string Sku, int Quantity);\n" +
            "\n" +
            "public sealed partial class Repo(IAsyncDbConnection connection)\n" +
            "{\n" +
            "    [Query(\"SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id; SELECT Sku, Quantity FROM OrderLines WHERE OrderId = @id;\")]\n" +
            "    public partial Task<(OrderRow Head, List<OrderLineRow> Lines)?> GetWithLinesAsync(int id, CancellationToken ct);\n" +
            "}\n";
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);
        var bugClass = compileDiagnostics
            .AsEnumerable()
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113" or "CS8795" or "CS0759")
            .ToArray();
        Assert.Empty(bugClass);
    }

    [Fact]
    public void Streaming_emit_compiles_cleanly()
    {
        // v0.3 Phase C.2 — IAsyncEnumerable<T> yield-based iterator must compile.
        // The bug-class filter catches partial-signature mismatches (CS8795/CS0759)
        // — those would fire if [EnumeratorCancellation] gets stamped on a parameter
        // the user's declaration doesn't carry the attribute on, or if the iterator
        // state machine sees a non-async signature.
        var source =
            "using System.Collections.Generic;\n" +
            "using System.Data.Async;\n" +
            "using System.Runtime.CompilerServices;\n" +
            "using System.Threading;\n" +
            "using ZeroAlloc.ORM;\n" +
            "\n" +
            "namespace TestApp;\n" +
            "\n" +
            "public sealed record OrderRow(int Id, int CustomerId, decimal Total);\n" +
            "\n" +
            "public sealed partial class Repo(IAsyncDbConnection connection)\n" +
            "{\n" +
            "    [Query(\"SELECT Id, CustomerId, Total FROM Orders WHERE CustomerId = @customerId ORDER BY Id\")]\n" +
            "    public partial IAsyncEnumerable<OrderRow> StreamByCustomerAsync(int customerId, [EnumeratorCancellation] CancellationToken ct);\n" +
            "}\n";
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);
        var bugClass = compileDiagnostics
            .AsEnumerable()
            // CS8419 (iterator cannot have ref-like locals) and CS4032 (cannot await in
            // an iterator's finally) are streaming-specific bug classes — if the emit
            // regresses we want them to surface as failures, not pass quietly.
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113" or "CS8795" or "CS0759" or "CS8419" or "CS4032")
            .ToArray();
        Assert.Empty(bugClass);
    }

    [Fact]
    public void Command_NonQuery_Task_int_emit_compiles_cleanly()
    {
        // v0.4 Phase A.2 — [Command(Kind = NonQuery)] returning Task<int> must
        // produce a body that compiles. CS8795 / CS0759 catch partial-signature
        // mismatches; the other CS-codes catch generator output regressions.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO Orders (CustomerId, Total) VALUES (@cust, @total)")]
                public partial Task<int> InsertOrderAsync(int cust, decimal total, CancellationToken ct);
            }
            """;
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);
        var bugClass = compileDiagnostics
            .AsEnumerable()
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113" or "CS8795" or "CS0759")
            .ToArray();
        Assert.Empty(bugClass);
    }

    [Fact]
    public void Command_NonQuery_Task_void_emit_compiles_cleanly()
    {
        // v0.4 Phase A.2 — [Command(Kind = NonQuery)] returning Task (no value)
        // emits ExecuteNonQueryAsync without a return statement. Tests that the
        // signature-rendering path for arity-0 Task / ValueTask doesn't trip the
        // partial-signature checks.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("DELETE FROM Orders WHERE Id = @id")]
                public partial Task DeleteOrderAsync(int id, CancellationToken ct);
            }
            """;
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);
        var bugClass = compileDiagnostics
            .AsEnumerable()
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113" or "CS8795" or "CS0759")
            .ToArray();
        Assert.Empty(bugClass);
    }

    [Fact]
    public void Command_Scalar_Task_int_emit_compiles_cleanly()
    {
        // v0.4 Phase B.1 — [Command(Kind = Scalar)] returning Task<int> must
        // emit a body that compiles. CS0030/CS0266 catch invalid scalar casts;
        // CS8795/CS0759 catch partial-signature mismatches.
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
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);
        var bugClass = compileDiagnostics
            .AsEnumerable()
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113" or "CS8795" or "CS0759" or "CS0030" or "CS0266")
            .ToArray();
        Assert.Empty(bugClass);
    }

    [Fact]
    public void Command_Scalar_Task_nullable_decimal_emit_compiles_cleanly()
    {
        // v0.4 Phase B.1 — [Command(Kind = Scalar)] returning Task<decimal?> must
        // emit the DBNull/null guard and the `(decimal)__result` cast cleanly.
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
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);
        var bugClass = compileDiagnostics
            .AsEnumerable()
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113" or "CS8795" or "CS0759" or "CS0030" or "CS0266")
            .ToArray();
        Assert.Empty(bugClass);
    }

    [Fact]
    public void Command_Identity_Task_int_emit_compiles_cleanly()
    {
        // v0.4 Phase C.1 — [Command(Kind = Identity)] returning Task<int> must
        // emit a body that compiles. CS0030/CS0266 catch invalid scalar casts;
        // CS8795/CS0759 catch partial-signature mismatches. The Sqlite RETURNING
        // syntax is just literal text in CommandText so no runtime support is
        // required from the emit; this verifies the materialization expression
        // and null guard compile cleanly.
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
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);
        var bugClass = compileDiagnostics
            .AsEnumerable()
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113" or "CS8795" or "CS0759" or "CS0030" or "CS0266")
            .ToArray();
        Assert.Empty(bugClass);
    }

    [Fact]
    public void Command_Identity_Task_value_object_emit_compiles_cleanly()
    {
        // v0.4 Phase C.1 — [Command(Kind = Identity)] returning a VO wrapping
        // int must emit `new OrderId(Convert.ToInt32(__result!, ic))` cleanly.
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
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);
        var bugClass = compileDiagnostics
            .AsEnumerable()
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113" or "CS8795" or "CS0759" or "CS0030" or "CS0266")
            .ToArray();
        Assert.Empty(bugClass);
    }

    [Fact]
    public void Keyword_CancellationToken_name_emit_compiles_cleanly()
    {
        // Regression: when a user names their CancellationToken parameter with a
        // C# keyword (e.g. `@event`), the emitted body must `@`-prefix the identifier
        // when forwarding to OpenAsync/ReadAsync/ExecuteScalarAsync. Otherwise the
        // emit reads `OpenAsync(event)` which trips CS1525.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT 1")]
                public partial Task<int> GetAsync(CancellationToken @event);  // @event = C# keyword
            }
            """;
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);
        var bugClass = compileDiagnostics
            .AsEnumerable()
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113" or "CS1525")
            .ToArray();
        Assert.Empty(bugClass);
    }

    [Fact]
    public void StoredProcedure_scalar_emit_compiles_cleanly()
    {
        // v0.4 Phase D.2 — single-result-set [StoredProcedure] on a scalar return.
        // Same bug-class filter as the [Query] smoke tests; verifies the emit's
        // CommandText = "usp_..." + CommandType = StoredProcedure block doesn't
        // reference an undeclared local or call a missing member.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [StoredProcedure("usp_GetCount")]
                public partial Task<int> GetCountAsync(int customerId, CancellationToken ct);
            }
            """;
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);
        var bugClass = compileDiagnostics
            .AsEnumerable()
            // CS8795/CS0759 catch partial-method signature mismatches — e.g. a
            // regression in BuildParameterList for sprocs that desyncs the emit
            // signature from the user-declared partial. The other CS-codes catch
            // missing-member / undeclared-local / unused-parameter regressions.
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113" or "CS8795" or "CS0759")
            .ToArray();
        Assert.Empty(bugClass);
    }

    [Fact]
    public void StoredProcedure_multi_result_tuple_emit_compiles_cleanly()
    {
        // v0.4 Phase D.3 — multi-result-set [StoredProcedure] returning a tuple.
        // BatchMode.Never (the sproc default) routes through the joined-statements
        // single-command path which already walks NextResultAsync per element.
        var source = """
            using System.Collections.Generic;
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderRow(int Id, int CustomerId, decimal Total);
            public sealed record OrderLineRow(int OrderId, int Sku, int Quantity);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [StoredProcedure("usp_GetOrderWithLines")]
                public partial Task<(OrderRow Head, IReadOnlyList<OrderLineRow> Lines)?> GetOrderWithLinesAsync(
                    int id, CancellationToken ct);
            }
            """;
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);
        var bugClass = compileDiagnostics
            .AsEnumerable()
            // CS8795/CS0759 catch partial-method signature mismatches; CS8419/CS4032
            // catch the streaming / multi-result-set specific bug classes (iterator
            // can't have ref-like locals; can't await in iterator's finally) shared
            // with the multi-result peer at line ~613.
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113" or "CS8795" or "CS0759" or "CS8419" or "CS4032")
            .ToArray();
        Assert.Empty(bugClass);
    }

    [Fact]
    public void StoredProcedure_flatrow_emit_compiles_cleanly()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderRow(int Id, int CustomerId, decimal Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [StoredProcedure("usp_GetOrder")]
                public partial Task<OrderRow?> GetOrderAsync(int id, CancellationToken ct);
            }
            """;
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);
        var bugClass = compileDiagnostics
            .AsEnumerable()
            // CS8795/CS0759 catch partial-method signature mismatches in the
            // FlatRow sproc path (parameter list / return-type sync).
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113" or "CS8795" or "CS0759")
            .ToArray();
        Assert.Empty(bugClass);
    }

    [Fact]
    public void StoredProcedure_output_params_with_result_row_emit_compiles_cleanly()
    {
        // v0.4 Phase E.2 — [StoredProcedure] returning a tuple where one tuple
        // field matches a C# parameter (Direction=Output) AND another tuple field
        // is a result row. The emit binds the output param, drains the reader so
        // the parameter value is populated, then reads the value back into the
        // returned tuple. Smoke test catches partial-signature mismatches and
        // unbound locals in the new EmitSprocWithOutputParams body.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderRow(int Id, int CustomerId, decimal Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [StoredProcedure("usp_InsertOrder")]
                public partial Task<(OrderRow Result, int NewOrderId)> InsertAsync(
                    int customerId, int newOrderId, CancellationToken ct);
            }
            """;
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);
        var bugClass = compileDiagnostics
            .AsEnumerable()
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113" or "CS8795" or "CS0759")
            .ToArray();
        Assert.Empty(bugClass);
    }

    [Fact]
    public void StoredProcedure_output_only_emit_compiles_cleanly()
    {
        // v0.4 Phase E.3 — every tuple field matches a C# parameter. Emit uses
        // ExecuteNonQueryAsync instead of ExecuteReaderAsync; no reader local,
        // no drain loop. Smoke test catches the ExecuteNonQueryAsync-vs-Reader
        // branch regression (e.g. leaving an `await using var __reader = ...`
        // line in the output-only emit would surface as a missing __reader
        // declaration error here).
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [StoredProcedure("usp_InsertOrder")]
                public partial Task<(int NewOrderId, int Status)> InsertAsync(
                    int customerId, int newOrderId, int status, CancellationToken ct);
            }
            """;
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);
        var bugClass = compileDiagnostics
            .AsEnumerable()
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113" or "CS8795" or "CS0759")
            .ToArray();
        Assert.Empty(bugClass);
    }

    [Fact]
    public void StoredProcedure_output_params_value_object_output_emit_compiles_cleanly()
    {
        // Value-object output: the int readback from the parameter is wrapped in
        // the VO's positional ctor before being assigned to the tuple position.
        // Smoke test verifies the ConventionDiscovery funnel on output positions
        // doesn't emit invalid C# (e.g. mismatched cast targets or undeclared
        // factory references).
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderRow(int Id, int CustomerId, decimal Total);
            public sealed record OrderId(int Value);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [StoredProcedure("usp_InsertOrder")]
                public partial Task<(OrderRow Result, OrderId NewOrderId)> InsertAsync(
                    int customerId, OrderId newOrderId, CancellationToken ct);
            }
            """;
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);
        var bugClass = compileDiagnostics
            .AsEnumerable()
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113" or "CS8795" or "CS0759")
            .ToArray();
        Assert.Empty(bugClass);
    }

    // v0.5 Phase A.2 — composite scalar emit compiles cleanly.
    [Fact]
    public void Composite_scalar_emit_compiles_cleanly()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public readonly record struct Money(decimal Amount, string Currency);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Amount, Currency FROM Orders WHERE Id = @id")]
                public partial Task<Money> GetTotalAsync(int id, CancellationToken ct);
            }
            """;
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);
        var bugClass = compileDiagnostics
            .AsEnumerable()
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113" or "CS8795" or "CS0759")
            .ToArray();
        Assert.Empty(bugClass);
    }

    // v0.5 Phase A.2 — composite scalar with a value-object inner ctor parameter
    // compiles cleanly. Exercises the layered convention emit
    // (`new Money(reader.GetDecimal(0), OrderId.From(reader.GetInt32(1)))`).
    [Fact]
    public void Composite_scalar_with_value_object_inner_emit_compiles_cleanly()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;
            using ZeroAlloc.ValueObjects;

            namespace TestApp;

            [ValueObject]
            public readonly partial struct OrderId
            {
                public int Value { get; }
                public OrderId(int v) { Value = v; }
                public static OrderId From(int value) => new(value);
            }

            public readonly record struct Money(decimal Amount, OrderId Currency);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Amount, Currency FROM Orders WHERE Id = @id")]
                public partial Task<Money> GetTotalAsync(int id, CancellationToken ct);
            }
            """;
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);
        var bugClass = compileDiagnostics
            .AsEnumerable()
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113" or "CS8795" or "CS0759")
            .ToArray();
        Assert.Empty(bugClass);
    }

    // v0.5 Phase A.3 — composite nested in FlatRow compiles cleanly.
    [Fact]
    public void Composite_nested_in_flat_row_emit_compiles_cleanly()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public readonly record struct Money(decimal Amount, string Currency);
            public sealed record OrderRow(int Id, Money Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, Amount, Currency FROM Orders WHERE Id = @id")]
                public partial Task<OrderRow?> GetByIdAsync(int id, CancellationToken ct);
            }
            """;
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);
        var bugClass = compileDiagnostics
            .AsEnumerable()
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113" or "CS8795" or "CS0759")
            .ToArray();
        Assert.Empty(bugClass);
    }

    // v0.5 Phase A.3 — composite nested in DomainEntity (class with single
    // public ctor) compiles cleanly. GetOrdinal-keyed reads layered with the
    // composite construction.
    [Fact]
    public void Composite_nested_in_domain_entity_emit_compiles_cleanly()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public readonly record struct Money(decimal Amount, string Currency);

            public class OrderEntity
            {
                public int Id { get; }
                public Money Total { get; }
                public OrderEntity(int id, Money total) { Id = id; Total = total; }
            }

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, Amount, Currency FROM Orders WHERE Id = @id")]
                public partial Task<OrderEntity?> GetByIdAsync(int id, CancellationToken ct);
            }
            """;
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);
        var bugClass = compileDiagnostics
            .AsEnumerable()
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113" or "CS8795" or "CS0759")
            .ToArray();
        Assert.Empty(bugClass);
    }

    [Fact]
    public void Composite_parameter_binding_emit_compiles_cleanly()
    {
        // v0.5 Phase B.2 — composite parameter unpacks into N DbParameter
        // blocks named `@total_Amount` / `@total_Currency`. The emitted
        // accessor `@total.Amount` relies on the positional-record
        // auto-generated property symmetry; the smoke test catches
        // accessor/local-name regressions that would surface as CS1061
        // (`Money` has no `Amount` member) or CS0103.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public readonly record struct Money(decimal Amount, string Currency);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("UPDATE Orders SET Amount = @total_Amount, Currency = @total_Currency WHERE Id = 1", Kind = CommandKind.NonQuery)]
                public partial Task<int> UpdateAsync(Money total, CancellationToken ct);
            }
            """;
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);
        var bugClass = compileDiagnostics
            .AsEnumerable()
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113" or "CS8795" or "CS0759")
            .ToArray();
        Assert.Empty(bugClass);
    }

    [Fact]
    public void Composite_parameter_alongside_primitive_emit_compiles_cleanly()
    {
        // v0.5 Phase B.2 — pins the mixed-binding case: a primitive parameter
        // (`int id`) and a composite parameter (`Money total`) on the same
        // method must produce well-formed `__p_id` + `__p_total_Amount` /
        // `__p_total_Currency` locals without local-name collisions or
        // missing accessors.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public readonly record struct Money(decimal Amount, string Currency);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("UPDATE Orders SET Amount = @total_Amount, Currency = @total_Currency WHERE Id = @id", Kind = CommandKind.NonQuery)]
                public partial Task<int> UpdateAsync(int id, Money total, CancellationToken ct);
            }
            """;
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);
        var bugClass = compileDiagnostics
            .AsEnumerable()
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113" or "CS8795" or "CS0759")
            .ToArray();
        Assert.Empty(bugClass);
    }
}
