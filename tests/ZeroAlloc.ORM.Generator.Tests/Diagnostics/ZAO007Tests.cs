using System.Globalization;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Diagnostics;

public class ZAO007Tests
{
    [Fact]
    public void IAsyncEnumerable_without_EnumeratorCancellation_emits_ZAO007()
    {
        var source = """
            using System.Collections.Generic;
            using System.Data.Async;
            using System.Threading;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT 1")]
                public partial IAsyncEnumerable<int> StreamAsync(CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAO007", System.StringComparison.Ordinal));
    }

    [Fact]
    public void IAsyncEnumerable_with_no_CancellationToken_emits_ZAO007()
    {
        var source = """
            using System.Collections.Generic;
            using System.Data.Async;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT 1")]
                public partial IAsyncEnumerable<int> StreamAsync();
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        Assert.Contains(result.Results[0].Diagnostics, d => string.Equals(d.Id, "ZAO007", System.StringComparison.Ordinal));
    }

    [Fact]
    public void ZAO007_message_differs_between_no_CT_and_missing_attribute_cases()
    {
        var withCt = """
            using System.Collections.Generic;
            using System.Data.Async;
            using System.Threading;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT 1")]
                public partial IAsyncEnumerable<int> StreamAsync(CancellationToken ct);
            }
            """;
        var withoutCt = """
            using System.Collections.Generic;
            using System.Data.Async;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT 1")]
                public partial IAsyncEnumerable<int> StreamAsync();
            }
            """;
        var withCtResult = GeneratorHarness.RunGenerator(withCt).Results[0].Diagnostics;
        var withoutCtResult = GeneratorHarness.RunGenerator(withoutCt).Results[0].Diagnostics;

        var withCtMessage = FindZao007Message(withCtResult);
        var withoutCtMessage = FindZao007Message(withoutCtResult);

        Assert.Contains("no CancellationToken parameter", withoutCtMessage, System.StringComparison.Ordinal);
        Assert.Contains("lacks [EnumeratorCancellation]", withCtMessage, System.StringComparison.Ordinal);
        Assert.NotEqual(withCtMessage, withoutCtMessage, System.StringComparer.Ordinal);
    }

    private static string FindZao007Message(System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics)
    {
        foreach (var d in diagnostics)
        {
            if (string.Equals(d.Id, "ZAO007", System.StringComparison.Ordinal))
                return d.GetMessage(CultureInfo.InvariantCulture);
        }
        throw new Xunit.Sdk.XunitException("ZAO007 not found in diagnostics");
    }

    [Fact]
    public void IAsyncEnumerable_with_EnumeratorCancellation_does_not_emit_ZAO007()
    {
        var source = """
            using System.Collections.Generic;
            using System.Data.Async;
            using System.Runtime.CompilerServices;
            using System.Threading;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT 1")]
                public partial IAsyncEnumerable<int> StreamAsync([EnumeratorCancellation] CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO007", System.StringComparison.Ordinal));
    }
}
