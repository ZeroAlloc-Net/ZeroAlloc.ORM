using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace ZeroAlloc.ORM.Generator.Tests;

internal static class GeneratorHarness
{
    // Each additional source becomes its own tree, e.g. NpgsqlStub.Source, which
    // declares block namespaces and so cannot share a tree with a test source's
    // file-scoped `namespace TestApp;`.
    public static GeneratorDriverRunResult RunGenerator(string source, params string[] additionalSources)
    {
        var (driver, _) = RunDriver(CreateCompilation(ParseAll(source, additionalSources)));
        return driver.GetRunResult();
    }

    public static (GeneratorDriverRunResult RunResult, ImmutableArray<Diagnostic> CompileDiagnostics) RunGeneratorAndCompile(
        string source,
        params string[] additionalSources)
    {
        var (driver, updatedCompilation) = RunDriver(CreateCompilation(ParseAll(source, additionalSources)));

        var compileDiagnostics = updatedCompilation.GetDiagnostics()
            .AsEnumerable()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();

        return (driver.GetRunResult(), compileDiagnostics);
    }

    // The compilation after the generator ran, for tests that inspect the emitted code
    // semantically or need diagnostics below error severity. The generated files carry
    // `#nullable enable`, so nullable warnings in emitted code surface here.
    public static (GeneratorDriverRunResult RunResult, Compilation Compilation) RunGeneratorAndGetCompilation(
        string source,
        params string[] additionalSources)
    {
        var (driver, updatedCompilation) = RunDriver(CreateCompilation(ParseAll(source, additionalSources)));
        return (driver.GetRunResult(), updatedCompilation);
    }

    // The file path RunGeneratorOnFile gives its source tree.
    public const string TestFilePath = "/src/Repo.cs";

    // Runs the generator on a tree with a file path and returns that tree, so a test can
    // assert that a diagnostic is bound to it: a source location, not an external-file one.
    // Additional sources get their own trees, without a path; the returned tree is always the
    // first source's.
    public static (GeneratorDriverRunResult RunResult, SyntaxTree Tree) RunGeneratorOnFile(
        string source,
        params string[] additionalSources)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, path: TestFilePath);
        var trees = new[] { syntaxTree }.Concat(additionalSources.Select(s => CSharpSyntaxTree.ParseText(s)));
        var (driver, _) = RunDriver(CreateCompilation(trees));
        return (driver.GetRunResult(), syntaxTree);
    }

    // A compilation of the given trees with the references every harness run uses.
    public static CSharpCompilation CreateCompilation(IEnumerable<SyntaxTree> syntaxTrees)
        => CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: syntaxTrees,
            references: BuildReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    // A reference to a library compiled on its own, the way a referenced project or package reaches
    // the generator: through metadata, where a private member is not imported. A reference
    // assembly, as a build passes for a project reference, drops private members altogether.
    public static MetadataReference CompileReference(string source, string assemblyName, bool referenceAssembly)
    {
        var library = CreateCompilation(new[] { CSharpSyntaxTree.ParseText(source) }).WithAssemblyName(assemblyName);
        var options = referenceAssembly
            ? new EmitOptions(metadataOnly: true, includePrivateMembers: false)
            : new EmitOptions();
        using var image = new MemoryStream();
        var emitted = library.Emit(image, options: options);
        if (!emitted.Success)
            throw new InvalidOperationException("The library does not compile: " + string.Join("; ", emitted.Diagnostics.Select(d => d.ToString())));
        return MetadataReference.CreateFromImage(image.ToArray());
    }

    private static IEnumerable<SyntaxTree> ParseAll(string source, string[] additionalSources)
        => new[] { source }.Concat(additionalSources).Select(s => CSharpSyntaxTree.ParseText(s));

    private static (GeneratorDriver Driver, Compilation UpdatedCompilation) RunDriver(Compilation compilation)
    {
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
    // tried first, back when the snapshots went through Verify.SourceGenerators; it broke
    // Verify's ModuleInitializer. Verify is gone since #204, and GeneratorSnapshot derives
    // the snapshot path from the test file, but AppDomain.GetAssemblies() stays.
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
            // System.Data.Common. Emitted code names ConnectionState,
            // IDbDataParameter, CommandBehavior and friends. The walk below only
            // sees assemblies this process already loaded, which differs between
            // machines and configurations -- it happened to be present on a
            // Windows Debug run and absent on a Linux Release one, so relying on
            // it made the reference set a property of the host rather than of
            // the test. Name it explicitly.
            typeof(System.Data.ConnectionState).Assembly,
            // ZeroAlloc.ORM.Linq: the authoring API that [LinqQuery] companion methods are written against.
            typeof(ZeroAlloc.ORM.Linq.Sql).Assembly,
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
