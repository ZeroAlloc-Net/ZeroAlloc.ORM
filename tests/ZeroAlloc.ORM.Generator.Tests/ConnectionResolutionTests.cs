using System.Threading.Tasks;
using Xunit;

using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Generator.Tests;

public class ConnectionResolutionTests
{
    [Fact]
    public void PrimaryCtor_param_resolves()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection db)
            {
                [Query("SELECT 1")]
                public partial Task<int> GetAsync(CancellationToken ct);
            }
            """;
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public void PrivateField_resolves()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo
            {
                private readonly IAsyncDbConnection _connection;
                public Repo(IAsyncDbConnection connection) => _connection = connection;

                [Query("SELECT 1")]
                public partial Task<int> GetAsync(CancellationToken ct);
            }
            """;
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public void Property_resolves()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo
            {
                public IAsyncDbConnection Connection { get; }
                public Repo(IAsyncDbConnection connection) => Connection = connection;

                [Query("SELECT 1")]
                public partial Task<int> GetAsync(CancellationToken ct);
            }
            """;
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }
}
