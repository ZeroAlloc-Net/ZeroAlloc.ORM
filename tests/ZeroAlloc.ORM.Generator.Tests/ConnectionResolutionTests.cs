using System.Threading.Tasks;
using Xunit;
using static VerifyXunit.Verifier;

namespace ZeroAlloc.ORM.Generator.Tests;

public class ConnectionResolutionTests
{
    [Fact]
    public Task PrimaryCtor_param_resolves()
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
                public partial Task<int> GetAsync(CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task PrivateField_resolves()
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
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task Property_resolves()
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
        return Verify(GeneratorHarness.RunGenerator(source));
    }
}
