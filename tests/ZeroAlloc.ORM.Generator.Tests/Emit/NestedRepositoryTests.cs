using Xunit;

using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// Issue #238 — a repository declared inside one or more containing types must
// have its generated half wrapped in a matching chain of partial declarations,
// outermost first, so the two halves join instead of landing as an unrelated
// namespace-level class.
//
// Note: each test below uses a distinct repository simple name (OrderRepository,
// InvoiceRepository, ItemRepository, ReceiptRepository) even across different
// containers. Two nested classes sharing the same simple name inside different
// containers would collide on the generator's hint name (`{Name}.g.cs`) — that's
// tracked separately under #237 (hint-name uniqueness); this suite avoids the
// collision rather than depending on that fix.
public class NestedRepositoryTests
{
    [Fact]
    public void One_level_of_nesting_wraps_generated_class_in_containing_type()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public partial class Data
            {
                public partial class OrderRepository(IAsyncDbConnection connection)
                {
                    [Query("SELECT 1")]
                    public partial Task<int> GetOneAsync(CancellationToken ct);
                }
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        GeneratorSnapshot.Verify(result);
    }

    [Fact]
    public void Two_levels_of_nesting_wrap_generated_class_in_both_containing_types()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public partial class Outer
            {
                public partial class Inner
                {
                    public partial class InvoiceRepository(IAsyncDbConnection connection)
                    {
                        [Query("SELECT 1")]
                        public partial Task<int> GetOneAsync(CancellationToken ct);
                    }
                }
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        GeneratorSnapshot.Verify(result);
    }

    [Fact]
    public void Generic_containing_type_re_emits_its_type_parameters()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public partial class Container<T>
            {
                public partial class ItemRepository(IAsyncDbConnection connection)
                {
                    [Query("SELECT 1")]
                    public partial Task<int> GetOneAsync(CancellationToken ct);
                }
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        GeneratorSnapshot.Verify(result);
    }

    [Fact]
    public void Record_container_re_emits_as_partial_record()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public partial record Wrapper
            {
                public partial class ReceiptRepository(IAsyncDbConnection connection)
                {
                    [Query("SELECT 1")]
                    public partial Task<int> GetOneAsync(CancellationToken ct);
                }
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        GeneratorSnapshot.Verify(result);
    }
}
