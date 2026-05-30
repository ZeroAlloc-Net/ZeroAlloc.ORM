using System;
using System.Collections.Immutable;
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
        // Force-load assemblies the snapshot sources commonly reference. AppDomain.GetAssemblies()
        // only sees assemblies the JIT has already pulled in, and a using-only reference in the
        // raw source string doesn't trigger a load on the host side.
        _ = typeof(ZeroAlloc.ORM.QueryAttribute).Assembly;

        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToImmutableArray();

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
}
