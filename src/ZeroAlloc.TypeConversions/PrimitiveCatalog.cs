using System;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.TypeConversions;

// Lookup table for C# primitive scalar types -> IDataReader.GetXxx getter.
// Used by both result materialization (scalar return, FlatRow column read) and
// (future) parameter binding diagnostics. v0.1 surface (Section 3 of the design
// doc): int, long, short, byte, bool, decimal, double, float, string, DateTime,
// DateTimeOffset, TimeSpan, Guid, byte[]. Unsigned / sbyte / char are deferred
// to v0.2.
public static class PrimitiveCatalog
{
    // Map a supported primitive scalar type to the IDataReader.GetXxx method that
    // strongly-typed-reads it. Returns null for unsupported types.
    public static string? GetScalarReaderMethod(ITypeSymbol type)
    {
        return type.SpecialType switch
        {
            SpecialType.System_Int32 => "GetInt32",
            SpecialType.System_Int64 => "GetInt64",
            SpecialType.System_Int16 => "GetInt16",
            SpecialType.System_Byte => "GetByte",
            SpecialType.System_Boolean => "GetBoolean",
            SpecialType.System_Decimal => "GetDecimal",
            SpecialType.System_Double => "GetDouble",
            SpecialType.System_Single => "GetFloat",
            SpecialType.System_String => "GetString",
            SpecialType.System_DateTime => "GetDateTime",
            // No direct IDataReader.GetDateTimeOffset / GetTimeSpan exist, so we
            // route through the generic GetFieldValue<T> which every modern
            // ADO.NET provider implements (DbDataReader-level API).
            _ when string.Equals(type.ToDisplayString(), "System.Guid", StringComparison.Ordinal) => "GetGuid",
            _ when string.Equals(type.ToDisplayString(), "System.DateTimeOffset", StringComparison.Ordinal) => "GetFieldValue<global::System.DateTimeOffset>",
            _ when string.Equals(type.ToDisplayString(), "System.TimeSpan", StringComparison.Ordinal) => "GetFieldValue<global::System.TimeSpan>",
            _ when IsByteArray(type) => "GetFieldValue<byte[]>",
            _ => null,
        };
    }

    // Convention-discovery shortcut: "does this type round-trip via a single GetXxx
    // call?" Equivalent to "GetScalarReaderMethod returns non-null" but expressed as
    // a predicate so callers don't have to encode the null-check pattern themselves.
    public static bool IsPrimitive(ITypeSymbol type)
        => GetScalarReaderMethod(type) is not null;

    // byte[] is the canonical BLOB carrier in ADO.NET. It is recognized as an
    // array of SpecialType.System_Byte; the array itself has no SpecialType.
    private static bool IsByteArray(ITypeSymbol type)
        => type is IArrayTypeSymbol arr && arr.ElementType.SpecialType == SpecialType.System_Byte;
}
