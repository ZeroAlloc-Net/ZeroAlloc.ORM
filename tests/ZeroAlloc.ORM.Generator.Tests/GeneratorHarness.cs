using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ZeroAlloc.ORM.Generator.Tests;

internal static class GeneratorHarness
{
    public static GeneratorDriverRunResult RunGenerator(string source)
    {
        var (driver, _) = RunDriver(source);
        return driver.GetRunResult();
    }

    public static (GeneratorDriverRunResult RunResult, ImmutableArray<Diagnostic> CompileDiagnostics) RunGeneratorAndCompile(string source)
    {
        var (driver, updatedCompilation) = RunDriver(source);

        var compileDiagnostics = updatedCompilation.GetDiagnostics()
            .AsEnumerable()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();

        return (driver.GetRunResult(), compileDiagnostics);
    }

    private static (GeneratorDriver Driver, Compilation UpdatedCompilation) RunDriver(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = BuildReferences();

        var compilation = CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new ZeroAlloc.ORM.Generator.OrmGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);
        return (driver, updatedCompilation);
    }

    // R10 — explicit force-load of every assembly the snapshot sources reference,
    // followed by AppDomain.GetAssemblies() to pick up framework refs the host
    // already loaded. Replaces a bare AppDomain.GetAssemblies() walk that quietly
    // depended on whatever xunit happened to have JIT-pulled by test time.
    //
    // Strategy: touch a sentinel type from each non-framework assembly via the
    // ForceLoadAssemblies list below — that guarantees they're resident regardless
    // of which test triggered the harness. Then add every non-dynamic assembly
    // currently in the AppDomain (deduped by filename). Tests that need a NEW
    // non-framework ref add their type to ForceLoadAssemblies; no other change.
    //
    // The fully-explicit "TRUSTED_PLATFORM_ASSEMBLIES + explicit list" approach was
    // tried first; it broke Verify.SourceGenerators' ModuleInitializer because
    // pulling in TPA leaked a stale path resolution for snapshot directories.
    // Sticking with AppDomain.GetAssemblies() keeps Verify's path-derive happy.
    private static ImmutableArray<MetadataReference> BuildReferences()
    {
        // Each entry forces its containing assembly to load before we walk the
        // AppDomain. New non-framework references go here.
        var forceLoadAssemblies = new[]
        {
            typeof(ZeroAlloc.ORM.QueryAttribute).Assembly,   // ZeroAlloc.ORM.Abstractions
            typeof(ZeroAlloc.ValueObjects.ValueObjectAttribute).Assembly, // ZA.ValueObjects (Phase C)
            // AdoNet.Async. The previous comment here claimed this was loaded
            // transitively through the generator's ProjectReference graph and
            // needed no explicit touch. It was not: the generator is referenced
            // with ReferenceOutputAssembly="false", System.Data.Async.dll never
            // reached the test output, and so IAsyncDbConnection never bound in
            // any harness compilation. Every emitted snippet failed with CS0246
            // and the CS1061-style assertions downstream matched nothing.
            typeof(System.Data.Async.IAsyncDbConnection).Assembly,
            // ZeroAlloc.ORM itself: emitted code throws
            // ZeroAllocOrmMaterializationException, which is declared there
            // rather than in Abstractions.
            typeof(ZeroAlloc.ORM.ZeroAllocOrmMaterializationException).Assembly,
        };
        _ = forceLoadAssemblies;

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
