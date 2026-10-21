using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using ZeroAlloc.ORM.Generator.Diagnostics;

namespace ZeroAlloc.ORM.Generator.Linq;

/// <summary>
/// ZAO109 — code calls a <c>[LinqQuery]</c> companion, or takes it as a method group. The
/// companion is read at build time and never runs. An analyzer rather than a generator step, so
/// it sees every invocation and method reference through <c>IOperation</c>, and a compilation
/// that does not reference ZeroAlloc.ORM.Linq registers nothing.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LinqCompanionCallAnalyzer : DiagnosticAnalyzer
{
    private const string LinqQueryAttributeMetadataName = "ZeroAlloc.ORM.Linq.LinqQueryAttribute";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.ZAO109_LinqCompanionCalled);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            var attribute = start.Compilation.GetTypeByMetadataName(LinqQueryAttributeMetadataName);
            if (attribute is null) return;

            var companions = new ConcurrentDictionary<INamedTypeSymbol, ImmutableDictionary<string, string>>(SymbolEqualityComparer.Default);
            start.RegisterOperationAction(
                operation => Analyze(operation, attribute, companions),
                OperationKind.Invocation,
                OperationKind.MethodReference);
        });
    }

    private static void Analyze(
        OperationAnalysisContext context,
        INamedTypeSymbol attribute,
        ConcurrentDictionary<INamedTypeSymbol, ImmutableDictionary<string, string>> companions)
    {
        var target = context.Operation switch
        {
            IInvocationOperation invocation => invocation.TargetMethod,
            IMethodReferenceOperation reference when !InNameOf(reference) => reference.Method,
            _ => null,
        };
        if (target is null) return;
        target = target.ReducedFrom ?? target;

        var type = target.ContainingType.OriginalDefinition;
        var byCompanion = companions.GetOrAdd(type, t => CompanionsOf(t, attribute));
        if (byCompanion.TryGetValue(target.Name, out var method))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.ZAO109_LinqCompanionCalled,
                context.Operation.Syntax.GetLocation(),
                target.Name,
                method));
        }
    }

    // `nameof(Companion)` names the method without calling it, as [LinqQuery] itself does.
    private static bool InNameOf(IOperation operation)
    {
        for (var parent = operation.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is INameOfOperation) return true;
        }
        return false;
    }

    // The companions the type's [LinqQuery] methods name, each with the method that names it.
    private static ImmutableDictionary<string, string> CompanionsOf(INamedTypeSymbol type, INamedTypeSymbol attribute)
    {
        var result = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var member in type.GetMembers())
        {
            if (member is not IMethodSymbol method) continue;
            foreach (var data in method.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(data.AttributeClass, attribute)
                    && data.ConstructorArguments.Length == 1
                    && data.ConstructorArguments[0].Value is string { Length: > 0 } companion
                    && !result.ContainsKey(companion))
                {
                    result.Add(companion, method.Name);
                }
            }
        }
        return result.ToImmutable();
    }
}
