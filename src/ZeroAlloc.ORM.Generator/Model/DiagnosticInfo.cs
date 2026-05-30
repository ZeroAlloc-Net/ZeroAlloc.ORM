using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ZeroAlloc.ORM.Generator.Model;

/// <summary>
/// Cache-safe representation of a Diagnostic to emit. Captures only string + value-type
/// fields so the model can be cached by the incremental pipeline without referencing
/// SyntaxNode / Compilation / Location.
/// </summary>
internal sealed record DiagnosticInfo(
    string DescriptorId,
    LocationInfo? Location,
    EquatableArray<string> MessageArgs) : IEquatable<DiagnosticInfo>;

internal sealed record LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan) : IEquatable<LocationInfo>
{
    public Location ToLocation()
        => Microsoft.CodeAnalysis.Location.Create(FilePath, TextSpan, LineSpan);

    public static LocationInfo? From(Location? location)
    {
        if (location is null || location == Location.None) return null;
        var lineSpan = location.GetLineSpan();
        return new LocationInfo(lineSpan.Path ?? string.Empty, location.SourceSpan, lineSpan.Span);
    }
}
