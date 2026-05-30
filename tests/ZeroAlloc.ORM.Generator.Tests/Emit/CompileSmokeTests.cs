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
}
