using System.Threading.Tasks;
using VerifyXunit;
using Xunit;
using static VerifyXunit.Verifier;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

public class StaticFactoryMaterializationTests
{
    // A hand-rolled wrapper struct with no [ValueObject] attribute, not a record —
    // only a `static T From(TPrim)` factory. ConventionDiscovery returns StaticFactory
    // and the emit invokes the factory directly: `global::TestApp.Score.From(...)`.
    [Fact]
    public Task Static_factory_struct_emits_From_call()
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
        return Verify(GeneratorHarness.RunGenerator(source));
    }
}
