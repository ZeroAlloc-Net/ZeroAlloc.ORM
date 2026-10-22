using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using ZeroAlloc.ORM.Generator.Diagnostics;

namespace ZeroAlloc.ORM.Generator.Linq;

/// <summary>
/// ZAO109 — code calls a <c>[LinqQuery]</c> or <c>[LinqCommand]</c> companion, or takes it as a method group. The
/// companion is read at build time and never runs. An analyzer rather than a generator step, so
/// it sees every invocation and method reference through <c>IOperation</c>, and a compilation
/// that does not reference ZeroAlloc.ORM.Linq registers nothing.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LinqCompanionCallAnalyzer : DiagnosticAnalyzer
{
    private const string LinqQueryAttributeMetadataName = "ZeroAlloc.ORM.Linq.LinqQueryAttribute";
    private const string LinqCommandAttributeMetadataName = "ZeroAlloc.ORM.Linq.LinqCommandAttribute";

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
            var query = start.Compilation.GetTypeByMetadataName(LinqQueryAttributeMetadataName);
            if (query is null) return;
            var attributes = new Attributes(query, start.Compilation.GetTypeByMetadataName(LinqCommandAttributeMetadataName));

            var companions = new ConcurrentDictionary<INamedTypeSymbol, ImmutableDictionary<string, (string Method, string Attribute)>>(SymbolEqualityComparer.Default);
            start.RegisterOperationAction(
                operation => Analyze(operation, attributes, companions),
                OperationKind.Invocation,
                OperationKind.MethodReference);
        });
    }

    private static void Analyze(
        OperationAnalysisContext context,
        Attributes attributes,
        ConcurrentDictionary<INamedTypeSymbol, ImmutableDictionary<string, (string Method, string Attribute)>> companions)
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
        var byCompanion = companions.GetOrAdd(type, t => CompanionsOf(t, attributes));
        if (byCompanion.TryGetValue(target.Name, out var named))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.ZAO109_LinqCompanionCalled,
                context.Operation.Syntax.GetLocation(),
                target.Name,
                named.Method,
                named.Attribute));
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

    // The companions the type's [LinqQuery] and [LinqCommand] methods name, each with the method
    // that names it and that method's attribute.
    private static ImmutableDictionary<string, (string Method, string Attribute)> CompanionsOf(INamedTypeSymbol type, Attributes attributes)
    {
        var result = ImmutableDictionary.CreateBuilder<string, (string Method, string Attribute)>(StringComparer.Ordinal);
        foreach (var member in type.GetMembers())
        {
            if (member is not IMethodSymbol method) continue;
            foreach (var data in method.GetAttributes())
            {
                if (attributes.LabelOf(data.AttributeClass) is { } label
                    && data.ConstructorArguments.Length == 1
                    && data.ConstructorArguments[0].Value is string { Length: > 0 } companion
                    && !result.ContainsKey(companion))
                {
                    result.Add(companion, (method.Name, label));
                }
            }
        }
        return result.ToImmutable();
    }

    // The two attributes that name a companion; LinqCommand is null against an older
    // ZeroAlloc.ORM.Linq that does not declare it.
    private sealed class Attributes(INamedTypeSymbol query, INamedTypeSymbol? command)
    {
        public string? LabelOf(INamedTypeSymbol? attribute)
        {
            if (SymbolEqualityComparer.Default.Equals(attribute, query)) return LinqDiagnostics.LinqQuery;
            if (command is not null && SymbolEqualityComparer.Default.Equals(attribute, command)) return LinqDiagnostics.LinqCommand;
            return null;
        }
    }
}
