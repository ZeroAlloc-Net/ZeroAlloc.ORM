using System.Threading.Tasks;
using Xunit;
using static VerifyXunit.Verifier;

namespace ZeroAlloc.ORM.Generator.Tests;

public class SkeletonTests
{
    [Fact]
    public Task Empty_source_produces_no_output()
    {
        var source = "namespace Empty {}";
        var result = GeneratorHarness.RunGenerator(source);
        return Verify(result);
    }
}
