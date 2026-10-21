using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ZeroAlloc.ORM.Generator.Model;

namespace ZeroAlloc.ORM.Generator.Linq;

/// <summary>
/// The LINQ front end. <see cref="Read"/> takes one <c>[LinqQuery]</c> method, finds and checks its
/// companion, walks the companion's chain through <c>IOperation</c> and returns a symbol-free
/// <see cref="LinqTranslation"/>. It is the single entry point later operators extend:
/// <list type="bullet">
/// <item>an operator is a case in <c>ReadOperator</c> (LinqQueryReader.Chain.cs) that fills <see cref="ChainBuilder"/>;</item>
/// <item>a value form is a case in <c>ReadOperand</c> (LinqQueryReader.Values.cs);</item>
/// <item>a result shape is a case in <c>ClassifyShape</c> (LinqQueryReader.Shape.cs).</item>
/// </list>
/// The companion never runs; only its syntax and semantic model are read.
/// </summary>
internal static partial class LinqQueryReader
{
    private const string LinqNamespace = "ZeroAlloc.ORM.Linq";
    private const string DialectAttributeFullName = "ZeroAlloc.ORM.DialectAttribute";
    private const int PostgreSqlDialect = (int)SqlDialectModel.PostgreSql;

    public static LinqTranslation Read(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        var method = (IMethodSymbol)ctx.TargetSymbol;
        var methodSyntax = (MethodDeclarationSyntax)ctx.TargetNode;
        var companionName = ctx.Attributes[0].ConstructorArguments.FirstOrDefault().Value as string ?? string.Empty;
        var comment = "LINQ: " + method.ContainingType.Name + "." + companionName;

        var dialectError = CheckDialect(method, methodSyntax, ctx.SemanticModel.Compilation);
        if (dialectError is not null) return Failed(comment, dialectError);

        var companion = ResolveCompanion(ctx, method, companionName, ct);
        if (companion.Error is not null) return Failed(comment, companion.Error);

        var context = new ReaderContext(ctx.SemanticModel.Compilation, companion.Syntax!, ct);
        var parameterError = MatchParameters(method, companion.Symbol!, companion.Syntax!, context);
        if (parameterError is not null) return Failed(comment, parameterError);

        var shape = ClassifyShape(method.ReturnType, companion.RowType!);
        if (shape is null)
        {
            return Failed(comment, LinqDiagnostics.ShapeMismatch(
                method.Name,
                method.ReturnType.ToDisplayString(),
                companion.Symbol!.ReturnType.ToDisplayString(),
                methodSyntax.ReturnType.GetLocation()));
        }

        var statement = ReadStatement(companion.Body!, shape.Value, context);
        if (statement is null) return Failed(comment, context.Diagnostics.ToArray());

        return new LinqTranslation(statement, shape.Value, default, comment, default);
    }

    private static LinqTranslation Failed(string comment, params DiagnosticInfo[] diagnostics)
        => LinqTranslation.Failed(comment, new EquatableArray<DiagnosticInfo>(diagnostics.ToImmutableArray()));

    // ZAO108 — LINQ renders PostgreSQL only. The repository's own [Dialect] wins over the
    // assembly's, as for [Query]; Inherited = false, so only the type's own attribute counts.
    private static DiagnosticInfo? CheckDialect(IMethodSymbol method, MethodDeclarationSyntax syntax, Compilation compilation)
    {
        var dialect = ReadDialect(method.ContainingType.GetAttributes())
            ?? ReadDialect(compilation.Assembly.GetAttributes());
        if (dialect == PostgreSqlDialect) return null;
        var found = dialect is { } value
            ? "it declares SqlDialect." + ((SqlDialectModel)value).ToString()
            : "it declares no dialect";
        return LinqDiagnostics.DialectNotPostgres(method.Name, found, syntax.Identifier.GetLocation());
    }

    private static int? ReadDialect(ImmutableArray<AttributeData> attributes)
    {
        foreach (var attribute in attributes)
        {
            if (string.Equals(attribute.AttributeClass?.ToDisplayString(), DialectAttributeFullName, StringComparison.Ordinal)
                && attribute.ConstructorArguments.Length == 1
                && attribute.ConstructorArguments[0].Value is int dialect)
            {
                return dialect;
            }
        }
        return null;
    }

    // ZAO101 — the companion takes the method's value parameters: everything but the
    // CancellationToken and the transaction, by name, type and order. Each companion parameter
    // then renders as the SQL name its method parameter binds with.
    private static DiagnosticInfo? MatchParameters(
        IMethodSymbol method,
        IMethodSymbol companion,
        MethodDeclarationSyntax companionSyntax,
        ReaderContext context)
    {
        var valueParameters = method.Parameters
            .Where(p => !IsCancellationToken(p.Type) && !OrmGenerator.IsIAsyncDbTransaction(p.Type))
            .ToList();
        var matches = valueParameters.Count == companion.Parameters.Length;
        for (var i = 0; matches && i < valueParameters.Count; i++)
        {
            matches = string.Equals(valueParameters[i].Name, companion.Parameters[i].Name, StringComparison.Ordinal)
                && SymbolEqualityComparer.Default.Equals(valueParameters[i].Type, companion.Parameters[i].Type);
        }
        if (!matches)
        {
            var expected = "(" + string.Join(", ", valueParameters.Select(p => p.ToDisplayString(ParameterFormat))) + ")";
            return LinqDiagnostics.ParameterMismatch(
                companion.Name, method.Name, expected, companionSyntax.ParameterList.GetLocation());
        }

        for (var i = 0; i < valueParameters.Count; i++)
        {
            var sqlName = OrmGenerator.ReadParamNameOverride(valueParameters[i]) ?? valueParameters[i].Name;
            context.SqlNames[companion.Parameters[i]] = sqlName;
        }
        return null;
    }

    private static readonly SymbolDisplayFormat ParameterFormat = SymbolDisplayFormat.MinimallyQualifiedFormat
        .WithParameterOptions(SymbolDisplayParameterOptions.IncludeType | SymbolDisplayParameterOptions.IncludeName);

    private static bool IsCancellationToken(ITypeSymbol type)
        => string.Equals(type.ToDisplayString(), "System.Threading.CancellationToken", StringComparison.Ordinal);

    // Whether `type` is the generic type `ns.metadataName`, such as System.Threading.Tasks.Task`1.
    private static bool IsGeneric(ITypeSymbol type, string ns, string metadataName, out ITypeSymbol argument)
    {
        argument = null!;
        if (type is not INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } named
            || !string.Equals(named.ConstructedFrom.MetadataName, metadataName, StringComparison.Ordinal)
            || !string.Equals(named.ContainingNamespace?.ToDisplayString(), ns, StringComparison.Ordinal))
        {
            return false;
        }
        argument = named.TypeArguments[0];
        return true;
    }

    /// <summary>What one read needs across the chain: the companion's model, parameters and diagnostics.</summary>
    private sealed class ReaderContext(Compilation compilation, MethodDeclarationSyntax companion, CancellationToken ct)
    {
        public SemanticModel Model { get; } = compilation.GetSemanticModel(companion.SyntaxTree);

        public CancellationToken CancellationToken { get; } = ct;

        // Companion parameter -> the SQL name its method parameter binds with.
        public Dictionary<IParameterSymbol, string> SqlNames { get; } = new(SymbolEqualityComparer.Default);

        public List<DiagnosticInfo> Diagnostics { get; } = new();

        public void Report(DiagnosticInfo diagnostic) => Diagnostics.Add(diagnostic);
    }
}
