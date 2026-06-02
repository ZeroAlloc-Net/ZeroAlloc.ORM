using System.Linq;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Diagnostics;

// v1.0 Phase D (v1.3 — BulkInsert) — diagnostic surface tests for
// ZAO070..ZAO074. The classifier fires these in a strict short-circuiting
// sequence (collection -> VALUES tuple -> placeholder resolution -> return
// type shape); each test below isolates a single failing check by passing
// the earlier ones.
//
// ZAO074 is the outlier: it is surfaced from the [Query] / [StoredProcedure]
// pipeline when a method carries a companion `[Command(Kind = BulkInsert)]`
// attribute. That pairing necessarily also triggers ZAO005 (multiple
// pipeline attributes) — Assert.Contains tolerates that extra diagnostic.
public class BulkInsertDiagnosticsTests
{
    [Fact]
    public void ZAO070_fires_when_no_collection_parameter()
    {
        // Scalar `int a` parameter — no IEnumerable<TRow>-shaped collection,
        // so the classifier short-circuits before VALUES / placeholder /
        // return-type checks and emits ZAO070.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO X (A) VALUES (@A)", Kind = CommandKind.BulkInsert)]
                public partial Task<int> InsertAsync(int a, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;
        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAO070", System.StringComparison.Ordinal));
    }

    [Fact]
    public void ZAO071_fires_when_SQL_has_zero_VALUES_tuples()
    {
        // INSERT ... SELECT — no `VALUES (...)` tuple at all, so the parser
        // returns TupleCount = 0 and ZAO071 fires.
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
                [Command("INSERT INTO Orders (CustomerId, Total) SELECT 1, 2", Kind = CommandKind.BulkInsert)]
                public partial Task<int> InsertAsync(IReadOnlyList<OrderRow> orders, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;
        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAO071", System.StringComparison.Ordinal));
    }

    [Fact]
    public void ZAO071_fires_when_SQL_has_multiple_VALUES_tuples()
    {
        // VALUES (1, 2), (3, 4) — the generator's chunk-multiplication owns
        // multi-row expansion at runtime; source-level multi-tuples are a
        // misuse that yields ZAO071 with TupleCount = 2.
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
                [Command("INSERT INTO Orders (CustomerId, Total) VALUES (1, 2), (3, 4)", Kind = CommandKind.BulkInsert)]
                public partial Task<int> InsertAsync(IReadOnlyList<OrderRow> orders, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;
        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAO071", System.StringComparison.Ordinal));
    }

    [Fact]
    public void ZAO072_fires_when_placeholder_has_no_matching_property()
    {
        // TRow exposes Foo; SQL references @Bar. Collection + VALUES checks
        // pass cleanly, then placeholder resolution finds no matching public
        // property and emits ZAO072 (one per unresolved placeholder).
        var source = """
            using System.Collections.Generic;
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderRow(int Foo);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO Orders (Bar) VALUES (@Bar)", Kind = CommandKind.BulkInsert)]
                public partial Task<int> InsertAsync(IReadOnlyList<OrderRow> orders, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;
        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAO072", System.StringComparison.Ordinal));
    }

    [Fact]
    public void ZAO073_fires_when_return_type_is_wrong_shape()
    {
        // Task<string> — neither Task<int> (rows-affected) nor
        // Task<IReadOnlyList<TIdentity>> (identity buffer). All earlier checks
        // pass, return-type shape check fails -> ZAO073.
        var source = """
            using System.Collections.Generic;
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderRow(int CustomerId);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO Orders (CustomerId) VALUES (@CustomerId)", Kind = CommandKind.BulkInsert)]
                public partial Task<string> InsertAsync(IReadOnlyList<OrderRow> orders, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;
        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAO073", System.StringComparison.Ordinal));
    }

    [Fact]
    public void ZAO074_fires_when_BulkInsert_kind_on_companion_Command()
    {
        // `Kind = CommandKind.BulkInsert` lives exclusively on
        // CommandAttribute, so the only way to misapply it to a non-Command
        // pipeline is to pair `[Query]` (or `[StoredProcedure]`) with a
        // companion `[Command(Kind = BulkInsert)]`. The pipeline-union
        // dedupe (TransformMethod -> allMethods.SelectMany) keeps the [Query]
        // entry over the [Command] one when both are present on the same
        // method, so the surviving model is the [Query] one whose
        // !isCommandAttribute branch contains the ZAO074 emit. ZAO005 fires
        // alongside (multiple pipeline attributes) — Assert.Contains
        // tolerates the co-fire.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT 1")]
                [Command("INSERT INTO X (A) VALUES (@A)", Kind = CommandKind.BulkInsert)]
                public partial Task<int> RunAsync(CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;
        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAO074", System.StringComparison.Ordinal));
    }
}
