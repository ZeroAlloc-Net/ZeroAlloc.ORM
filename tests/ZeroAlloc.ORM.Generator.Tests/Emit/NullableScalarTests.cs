using System.Threading.Tasks;
using Xunit;

using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

public class NullableScalarTests
{
    [Fact]
    public void Nullable_string_returns_first_row_or_null()
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
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public void Nullable_int_returns_first_row_or_null()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Age FROM Users LIMIT 1")]
                public partial Task<int?> GetAgeAsync(CancellationToken ct);
            }
            """;
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }
}
