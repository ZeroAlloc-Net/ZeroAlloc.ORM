using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ZeroAlloc.TypeConversions.Tests;

// Small Compilation fixture for ConventionDiscovery unit tests. Models GeneratorHarness'
// "force-load referenced assemblies then walk the AppDomain" pattern but does not run a
// source generator — these tests probe the discovery API directly against a tiny ad-hoc
// compilation.
internal static class TypeFixture
{
    public static Compilation CreateCompilation(string source)
    {
        // Touch types from the non-framework assemblies our test sources reference so
        // they're resident in the AppDomain before we enumerate metadata refs. Without
        // this, tests that compile sources mentioning [ValueObject] or [StoreAsString]
        // fail with "type not found" because the host hadn't loaded the assembly yet.
        _ = typeof(ZeroAlloc.ValueObjects.ValueObjectAttribute).Assembly;
        _ = typeof(ZeroAlloc.ORM.StoreAsStringAttribute).Assembly;

        var tree = CSharpSyntaxTree.ParseText(source);
        var references = BuildReferences();
        return CSharpCompilation.Create(
            assemblyName: "TypeConversionsTestAssembly",
            syntaxTrees: new[] { tree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static ImmutableArray<MetadataReference> BuildReferences()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var refs = ImmutableArray.CreateBuilder<MetadataReference>();
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.IsDynamic) continue;
            if (string.IsNullOrWhiteSpace(asm.Location)) continue;
            var fileName = Path.GetFileName(asm.Location);
            if (!seen.Add(fileName)) continue;
            refs.Add(MetadataReference.CreateFromFile(asm.Location));
        }
        return refs.ToImmutable();
    }
}
