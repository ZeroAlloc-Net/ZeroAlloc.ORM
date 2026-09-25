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

    // Inverse of GetScalarReaderMethod: map an IDataReader.GetXxx method name back
    // to the C# (display) type name used for a scalar cast. Used by the generator
    // when it has only the reader-method string in scope (e.g. ConventionInfo
    // carries UnderlyingReader, not the original ITypeSymbol) and needs to render
    // a `(T)__result` cast against the boxed `object?` returned by
    // ExecuteScalarAsync.
    //
    // Throws InvalidOperationException for unrecognized readers — the table is
    // populated by GetScalarReaderMethod, so an unrecognized reader implies a
    // generator bug, not a user-recoverable condition. Fail fast over silently
    // emitting `(object)__result`.
    public static string GetScalarCastTypeFromReader(string? readerMethod)
        => readerMethod switch
        {
            "GetInt32" => "int",
            "GetInt64" => "long",
            "GetInt16" => "short",
            "GetByte" => "byte",
            "GetBoolean" => "bool",
            "GetDecimal" => "decimal",
            "GetDouble" => "double",
            "GetFloat" => "float",
            "GetString" => "string",
            "GetDateTime" => "global::System.DateTime",
            "GetGuid" => "global::System.Guid",
            "GetFieldValue<global::System.DateTimeOffset>" => "global::System.DateTimeOffset",
            "GetFieldValue<global::System.TimeSpan>" => "global::System.TimeSpan",
            "GetFieldValue<byte[]>" => "byte[]",
            _ => throw new InvalidOperationException(
                $"PrimitiveCatalog.GetScalarCastTypeFromReader: unrecognized reader method '{readerMethod}'. The table is populated by GetScalarReaderMethod; an unrecognized entry implies a generator bug."),
        };

    // Map an IDataReader.GetXxx method name, as GetScalarReaderMethod returns it, to the
    // System.Data.DbType member a parameter of that type declares. Returns the member
    // name ("Int32") for the generator to emit as `global::System.Data.DbType.Int32`.
    //
    // Keyed on the reader, not the ITypeSymbol, so value objects and enums map through
    // the primitive they store as, the same way reads and scalar casts do.
    //
    // DateTime maps to DateTime2, not DateTime. On SQL Server DbType.DateTime is the
    // legacy `datetime`, which rounds to 1/300 s, so a datetime2 output declared that
    // way loses its fraction. Since Npgsql 6, DbType.DateTime maps to `timestamptz`
    // and DbType.DateTime2 to `timestamp`, which suits an Unspecified or Local
    // DateTime, so DateTime2 is the right choice there too.
    //
    // Throws for an unrecognized reader, like GetScalarCastTypeFromReader: the table is
    // populated by GetScalarReaderMethod, so a miss is a generator bug.
    public static string GetDbTypeNameFromReader(string? readerMethod)
        => readerMethod switch
        {
            "GetInt32" => "Int32",
            "GetInt64" => "Int64",
            "GetInt16" => "Int16",
            "GetByte" => "Byte",
            "GetBoolean" => "Boolean",
            "GetDecimal" => "Decimal",
            "GetDouble" => "Double",
            "GetFloat" => "Single",
            "GetString" => "String",
            "GetDateTime" => "DateTime2",
            "GetGuid" => "Guid",
            "GetFieldValue<global::System.DateTimeOffset>" => "DateTimeOffset",
            "GetFieldValue<global::System.TimeSpan>" => "Time",
            "GetFieldValue<byte[]>" => "Binary",
            _ => throw new InvalidOperationException(
                $"PrimitiveCatalog.GetDbTypeNameFromReader: unrecognized reader method '{readerMethod}'. The table is populated by GetScalarReaderMethod; an unrecognized entry implies a generator bug."),
        };

    // byte[] is the canonical BLOB carrier in ADO.NET. It is recognized as an
    // array of SpecialType.System_Byte; the array itself has no SpecialType.
    private static bool IsByteArray(ITypeSymbol type)
        => type is IArrayTypeSymbol arr && arr.ElementType.SpecialType == SpecialType.System_Byte;
}
