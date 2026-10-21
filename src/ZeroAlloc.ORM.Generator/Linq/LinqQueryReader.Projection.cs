using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace ZeroAlloc.ORM.Generator.Linq;

internal static partial class LinqQueryReader
{
    // Select(t => new Row(...)) — the SELECT list. The row is a positional record built with the
    // constructor the FlatRow materializer reads, and FlatRow reads by position, so each argument
    // goes to the slot of the parameter it binds, whatever order named arguments are written in.
    private static bool ReadSelect(IInvocationOperation invocation, ChainBuilder chain, ReaderContext context)
    {
        if (invocation.Arguments.Length != 2
            || !TryReadLambda(invocation.Arguments[1].Value, out var row, out var returned))
        {
            return Unsupported(invocation, "'Select' without a lambda", context);
        }

        if (Unwrap(returned) is not IObjectCreationOperation creation)
            return RejectProjection("Select must construct a positional record with 'new'", returned, context);

        var expectedRow = invocation.TargetMethod.TypeArguments[1];
        if (creation.Type is not INamedTypeSymbol { IsRecord: true } record
            || !SymbolEqualityComparer.Default.Equals(record, expectedRow))
        {
            return RejectProjection("'" + expectedRow.ToDisplayString() + "' is not a positional record", creation, context);
        }
        if (creation.Constructor is not { } constructor
            || !SymbolEqualityComparer.Default.Equals(constructor, OrmGenerator.FlatRowConstructor(record)))
        {
            return RejectProjection(
                "construct '" + record.Name + "' with its widest public constructor, the one rows are read through",
                creation,
                context);
        }
        if (creation.Initializer is not null)
        {
            return RejectProjection(
                "an object initializer is not read; pass every value to the constructor", creation.Initializer, context);
        }

        var items = new SqlExpression[constructor.Parameters.Length];
        foreach (var argument in creation.Arguments)
        {
            if (argument.Parameter is not { } parameter || argument.ArgumentKind == ArgumentKind.ParamArray)
                return RejectProjection("a params argument is not supported", argument, context);
            var item = ReadProjectionItem(argument.Value, row, context);
            if (item is null) return false;
            items[parameter.Ordinal] = item;
        }

        chain.Projection = ImmutableArray.Create(items);
        return true;
    }

    private static SqlExpression? ReadProjectionItem(IOperation value, IParameterSymbol row, ReaderContext context)
    {
        switch (ReadOperand(value, row, context, out var expression))
        {
            case OperandRead.Read:
                return expression;
            case OperandRead.Unsupported:
                RejectProjection(
                    "'" + value.Syntax + "' must be a mapped column, a companion parameter or a literal", value, context);
                return null;
            default:
                return null;
        }
    }

    private static bool RejectProjection(string reason, IOperation at, ReaderContext context)
    {
        context.Report(LinqDiagnostics.UnsupportedProjection(reason, at.Syntax.GetLocation()));
        return false;
    }
}
