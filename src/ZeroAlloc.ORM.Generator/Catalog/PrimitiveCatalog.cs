using System;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.ORM.Generator.Catalog;

// Lookup table for C# primitive scalar types -> IDataReader.GetXxx getter.
// Used by both result materialization (scalar return, FlatRow column read) and
// (future) parameter binding diagnostics. v0.1 surface: the built-in primitive
// catalog from Section 3 of the design doc.
internal static class PrimitiveCatalog
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
            _ when string.Equals(type.ToDisplayString(), "System.Guid", StringComparison.Ordinal) => "GetGuid",
            _ => null,
        };
    }
}
