using System.Threading.Tasks;
using VerifyXunit;
using Xunit;
using static VerifyXunit.Verifier;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// Issue #4 / v1.3 — CommandKind.BulkInsert emit shape.
// Five coverage cells: rows-affected, identity capture, IEnumerable adapter,
// VO-wrapped identity factory, and chunk-size scaling with placeholder count.
public class BulkInsertTests
{
    [Fact]
    public Task BulkInsert_Task_int_emits_chunked_NonQuery_pipeline()
    {
        var source = """
            using System.Collections.Generic;
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderRow(int CustomerId, decimal Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO Orders (CustomerId, Total) VALUES (@CustomerId, @Total)", Kind = CommandKind.BulkInsert)]
                public partial Task<int> InsertOrdersAsync(IReadOnlyList<OrderRow> orders, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task BulkInsert_Task_IReadOnlyList_int_emits_chunked_ExecuteReader_with_RETURNING()
    {
        var source = """
            using System.Collections.Generic;
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderRow(int CustomerId, decimal Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO Orders (CustomerId, Total) VALUES (@CustomerId, @Total) RETURNING Id", Kind = CommandKind.BulkInsert)]
                public partial Task<IReadOnlyList<int>> InsertOrdersAsync(IReadOnlyList<OrderRow> orders, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task BulkInsert_with_IEnumerable_parameter_emits_buffered_adapter()
    {
        var source = """
            using System.Collections.Generic;
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderRow(int CustomerId, decimal Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO Orders (CustomerId, Total) VALUES (@CustomerId, @Total)", Kind = CommandKind.BulkInsert)]
                public partial Task<int> InsertOrdersAsync(IEnumerable<OrderRow> orders, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task BulkInsert_with_ValueObject_identity_emits_factory_wrap()
    {
        var source = """
            using System.Collections.Generic;
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
                public OrderId(int value) { Value = value; }
            }

            public sealed record OrderRow(int CustomerId, decimal Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO Orders (CustomerId, Total) VALUES (@CustomerId, @Total) RETURNING Id", Kind = CommandKind.BulkInsert)]
                public partial Task<IReadOnlyList<OrderId>> InsertOrdersAsync(IReadOnlyList<OrderRow> orders, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task BulkInsert_chunk_size_scales_with_placeholder_count()
    {
        // 10-column row → chunk size 90 (900 / 10).
        var source = """
            using System.Collections.Generic;
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record WideRow(int C1, int C2, int C3, int C4, int C5, int C6, int C7, int C8, int C9, int C10);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO Wide (C1, C2, C3, C4, C5, C6, C7, C8, C9, C10) VALUES (@C1, @C2, @C3, @C4, @C5, @C6, @C7, @C8, @C9, @C10)", Kind = CommandKind.BulkInsert)]
                public partial Task<int> InsertWideAsync(IReadOnlyList<WideRow> rows, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }
}
