using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests;

public class SmokeTests
{
    [Fact]
    public void HarnessLoads()
    {
        var result = GeneratorHarness.RunGenerator("public class Empty {}");
        Assert.NotNull(result);
    }
}
