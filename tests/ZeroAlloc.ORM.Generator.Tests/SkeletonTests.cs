using System.Threading.Tasks;
using Xunit;

using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Generator.Tests;

public class SkeletonTests
{
    [Fact]
    public void Empty_source_produces_no_output()
    {
        var source = "namespace Empty {}";
        var result = GeneratorHarness.RunGenerator(source);
        GeneratorSnapshot.Verify(result);
    }
}
