using Microsoft.CodeAnalysis;

namespace ZeroAlloc.ORM.Generator;

[Generator(LanguageNames.CSharp)]
public sealed class OrmGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // v0.1 — wires nothing yet. Subsequent tasks attach the [Query] scanner.
    }
}
