using System;
using System.Collections.Generic;
using System.Data;

namespace ZeroAlloc.ORM.Generator;

// #248 — the (bound type, DbType) pairs each dialect's provider rejects when it binds an input
// parameter. ZAO015 reports a [Param(DbType)] override that falls in the declared dialect's set.
//
// A pair is rejected only when the provider throws for every value tried; a value the provider
// converts or reinterprets counts as accepted. The sets come from the #243 probe: every DbType
// member and the undefined value 999 against each bound type, with SELECT @p on a live server,
// the invariant culture and printable values:
//
//   * SqlServer  — Microsoft.Data.SqlClient 7.1 against SQL Server 2022.
//   * PostgreSql — Npgsql 10 against PostgreSQL 16.
//   * Sqlite     — Microsoft.Data.Sqlite ignores DbType and rejects nothing, 999 included.
//   * MySql      — MySqlConnector 2.6 accepted all 378 pairs. MySql.Data rejects some, but a
//                  pair both documented libraries reject would be needed to report it, so the
//                  set is empty.
//
// The bound type is keyed by the reader method PrimitiveCatalog.GetScalarReaderMethod returns
// for it, so a value object or an enum is checked as the primitive it binds as. DateOnly and
// TimeOnly were not probed, so they are never reported. DialectDbTypeRejectionParityTests in
// the integration suite re-runs the probe against the SqlServer, PostgreSql and Sqlite
// providers and fails when a provider no longer agrees with this table.
//
// Only an input or input-output parameter sends a value. A pure output parameter is not
// checked: Npgsql accepts any DbType on one, and SqlClient was not probed for it.
//
// This file is also compiled into ZeroAlloc.ORM.Integration.Tests, with SqlDialectModel.cs, so
// it depends on nothing but the BCL.
internal static class DialectDbTypeRejections
{
    private static readonly Dictionary<string, ulong> SqlServer = new(StringComparer.Ordinal)
    {
        ["GetInt32"] = Mask(DbType.Binary, DbType.Date, DbType.DateTime, DbType.Guid, DbType.SByte, DbType.Time, DbType.UInt16, DbType.UInt32, DbType.UInt64, DbType.VarNumeric, DbType.DateTime2, DbType.DateTimeOffset),
        ["GetInt64"] = Mask(DbType.Binary, DbType.Date, DbType.DateTime, DbType.Guid, DbType.SByte, DbType.Time, DbType.UInt16, DbType.UInt32, DbType.UInt64, DbType.VarNumeric, DbType.DateTime2, DbType.DateTimeOffset),
        ["GetInt16"] = Mask(DbType.Binary, DbType.Date, DbType.DateTime, DbType.Guid, DbType.SByte, DbType.Time, DbType.UInt16, DbType.UInt32, DbType.UInt64, DbType.VarNumeric, DbType.DateTime2, DbType.DateTimeOffset),
        ["GetByte"] = Mask(DbType.Binary, DbType.Date, DbType.DateTime, DbType.Guid, DbType.SByte, DbType.Time, DbType.UInt16, DbType.UInt32, DbType.UInt64, DbType.VarNumeric, DbType.DateTime2, DbType.DateTimeOffset),
        ["GetBoolean"] = Mask(DbType.Binary, DbType.Date, DbType.DateTime, DbType.Guid, DbType.SByte, DbType.Time, DbType.UInt16, DbType.UInt32, DbType.UInt64, DbType.VarNumeric, DbType.DateTime2, DbType.DateTimeOffset),
        ["GetDecimal"] = Mask(DbType.Binary, DbType.Date, DbType.DateTime, DbType.Guid, DbType.SByte, DbType.Time, DbType.UInt16, DbType.UInt32, DbType.UInt64, DbType.VarNumeric, DbType.DateTime2, DbType.DateTimeOffset),
        ["GetDouble"] = Mask(DbType.Binary, DbType.Date, DbType.DateTime, DbType.Guid, DbType.SByte, DbType.Time, DbType.UInt16, DbType.UInt32, DbType.UInt64, DbType.VarNumeric, DbType.DateTime2, DbType.DateTimeOffset),
        ["GetFloat"] = Mask(DbType.Binary, DbType.Date, DbType.DateTime, DbType.Guid, DbType.SByte, DbType.Time, DbType.UInt16, DbType.UInt32, DbType.UInt64, DbType.VarNumeric, DbType.DateTime2, DbType.DateTimeOffset),
        ["GetString"] = Mask(DbType.Binary, DbType.Guid, DbType.SByte, DbType.UInt16, DbType.UInt32, DbType.UInt64, DbType.VarNumeric),
        ["GetDateTime"] = Mask(DbType.Binary, DbType.Byte, DbType.Boolean, DbType.Currency, DbType.Decimal, DbType.Double, DbType.Guid, DbType.Int16, DbType.Int32, DbType.Int64, DbType.SByte, DbType.Single, DbType.Time, DbType.UInt16, DbType.UInt32, DbType.UInt64, DbType.VarNumeric),
        ["GetFieldValue<global::System.DateTimeOffset>"] = Mask(DbType.AnsiString, DbType.Binary, DbType.Byte, DbType.Boolean, DbType.Currency, DbType.Date, DbType.DateTime, DbType.Decimal, DbType.Double, DbType.Guid, DbType.Int16, DbType.Int32, DbType.Int64, DbType.SByte, DbType.Single, DbType.String, DbType.Time, DbType.UInt16, DbType.UInt32, DbType.UInt64, DbType.VarNumeric, DbType.AnsiStringFixedLength, DbType.StringFixedLength, DbType.Xml, DbType.DateTime2),
        ["GetFieldValue<global::System.TimeSpan>"] = Mask(DbType.AnsiString, DbType.Binary, DbType.Byte, DbType.Boolean, DbType.Currency, DbType.Date, DbType.DateTime, DbType.Decimal, DbType.Double, DbType.Guid, DbType.Int16, DbType.Int32, DbType.Int64, DbType.SByte, DbType.Single, DbType.String, DbType.UInt16, DbType.UInt32, DbType.UInt64, DbType.VarNumeric, DbType.AnsiStringFixedLength, DbType.StringFixedLength, DbType.Xml, DbType.DateTime2, DbType.DateTimeOffset),
        ["GetGuid"] = Mask(DbType.AnsiString, DbType.Binary, DbType.Byte, DbType.Boolean, DbType.Currency, DbType.Date, DbType.DateTime, DbType.Decimal, DbType.Double, DbType.Int16, DbType.Int32, DbType.Int64, DbType.SByte, DbType.Single, DbType.String, DbType.Time, DbType.UInt16, DbType.UInt32, DbType.UInt64, DbType.VarNumeric, DbType.AnsiStringFixedLength, DbType.StringFixedLength, DbType.Xml, DbType.DateTime2, DbType.DateTimeOffset),
        ["GetFieldValue<byte[]>"] = Mask(DbType.AnsiString, DbType.Byte, DbType.Boolean, DbType.Currency, DbType.Date, DbType.DateTime, DbType.Decimal, DbType.Double, DbType.Guid, DbType.Int16, DbType.Int32, DbType.Int64, DbType.SByte, DbType.Single, DbType.String, DbType.Time, DbType.UInt16, DbType.UInt32, DbType.UInt64, DbType.VarNumeric, DbType.AnsiStringFixedLength, DbType.StringFixedLength, DbType.Xml, DbType.DateTime2, DbType.DateTimeOffset),
    };

    private static readonly Dictionary<string, ulong> PostgreSql = new(StringComparer.Ordinal)
    {
        ["GetInt32"] = Mask(DbType.AnsiString, DbType.Binary, DbType.Boolean, DbType.Currency, DbType.DateTime, DbType.Double, DbType.Guid, DbType.SByte, DbType.Single, DbType.String, DbType.Time, DbType.UInt16, DbType.UInt32, DbType.UInt64, DbType.AnsiStringFixedLength, DbType.StringFixedLength, DbType.Xml, DbType.DateTime2, DbType.DateTimeOffset),
        ["GetInt64"] = Mask(DbType.AnsiString, DbType.Binary, DbType.Boolean, DbType.Currency, DbType.Date, DbType.Double, DbType.Guid, DbType.SByte, DbType.Single, DbType.String, DbType.UInt16, DbType.UInt32, DbType.UInt64, DbType.AnsiStringFixedLength, DbType.StringFixedLength, DbType.Xml),
        ["GetInt16"] = Mask(DbType.AnsiString, DbType.Binary, DbType.Boolean, DbType.Currency, DbType.Date, DbType.DateTime, DbType.Double, DbType.Guid, DbType.SByte, DbType.Single, DbType.String, DbType.Time, DbType.UInt16, DbType.UInt32, DbType.UInt64, DbType.AnsiStringFixedLength, DbType.StringFixedLength, DbType.Xml, DbType.DateTime2, DbType.DateTimeOffset),
        ["GetByte"] = Mask(DbType.AnsiString, DbType.Binary, DbType.Boolean, DbType.Currency, DbType.Date, DbType.DateTime, DbType.Double, DbType.Guid, DbType.SByte, DbType.Single, DbType.String, DbType.Time, DbType.UInt16, DbType.UInt32, DbType.UInt64, DbType.AnsiStringFixedLength, DbType.StringFixedLength, DbType.Xml, DbType.DateTime2, DbType.DateTimeOffset),
        ["GetBoolean"] = Mask(DbType.AnsiString, DbType.Binary, DbType.Byte, DbType.Currency, DbType.Date, DbType.DateTime, DbType.Decimal, DbType.Double, DbType.Guid, DbType.Int16, DbType.Int32, DbType.Int64, DbType.SByte, DbType.Single, DbType.String, DbType.Time, DbType.UInt16, DbType.UInt32, DbType.UInt64, DbType.VarNumeric, DbType.AnsiStringFixedLength, DbType.StringFixedLength, DbType.Xml, DbType.DateTime2, DbType.DateTimeOffset),
        ["GetDecimal"] = Mask(DbType.AnsiString, DbType.Binary, DbType.Boolean, DbType.Date, DbType.DateTime, DbType.Double, DbType.Guid, DbType.SByte, DbType.Single, DbType.String, DbType.Time, DbType.UInt16, DbType.UInt32, DbType.UInt64, DbType.AnsiStringFixedLength, DbType.StringFixedLength, DbType.Xml, DbType.DateTime2, DbType.DateTimeOffset),
        ["GetDouble"] = Mask(DbType.AnsiString, DbType.Binary, DbType.Boolean, DbType.Currency, DbType.Date, DbType.DateTime, DbType.Guid, DbType.SByte, DbType.String, DbType.Time, DbType.UInt16, DbType.UInt32, DbType.UInt64, DbType.AnsiStringFixedLength, DbType.StringFixedLength, DbType.Xml, DbType.DateTime2, DbType.DateTimeOffset),
        ["GetFloat"] = Mask(DbType.AnsiString, DbType.Binary, DbType.Boolean, DbType.Currency, DbType.Date, DbType.DateTime, DbType.Guid, DbType.SByte, DbType.String, DbType.Time, DbType.UInt16, DbType.UInt32, DbType.UInt64, DbType.AnsiStringFixedLength, DbType.StringFixedLength, DbType.Xml, DbType.DateTime2, DbType.DateTimeOffset),
        ["GetString"] = Mask(DbType.Binary, DbType.Byte, DbType.Boolean, DbType.Currency, DbType.Date, DbType.DateTime, DbType.Decimal, DbType.Double, DbType.Guid, DbType.Int16, DbType.Int32, DbType.Int64, DbType.SByte, DbType.Single, DbType.Time, DbType.UInt16, DbType.UInt32, DbType.UInt64, DbType.VarNumeric, DbType.DateTime2, DbType.DateTimeOffset),
        ["GetDateTime"] = Mask(DbType.AnsiString, DbType.Binary, DbType.Byte, DbType.Boolean, DbType.Currency, DbType.Decimal, DbType.Double, DbType.Guid, DbType.Int16, DbType.Int32, DbType.Int64, DbType.SByte, DbType.Single, DbType.String, DbType.Time, DbType.UInt16, DbType.UInt32, DbType.UInt64, DbType.VarNumeric, DbType.AnsiStringFixedLength, DbType.StringFixedLength, DbType.Xml),
        ["GetFieldValue<global::System.DateTimeOffset>"] = Mask(DbType.AnsiString, DbType.Binary, DbType.Byte, DbType.Boolean, DbType.Currency, DbType.Date, DbType.Decimal, DbType.Double, DbType.Guid, DbType.Int16, DbType.Int32, DbType.Int64, DbType.SByte, DbType.Single, DbType.String, DbType.Time, DbType.UInt16, DbType.UInt32, DbType.UInt64, DbType.VarNumeric, DbType.AnsiStringFixedLength, DbType.StringFixedLength, DbType.Xml, DbType.DateTime2),
        ["GetFieldValue<global::System.TimeSpan>"] = Mask(DbType.AnsiString, DbType.Binary, DbType.Byte, DbType.Boolean, DbType.Currency, DbType.Date, DbType.DateTime, DbType.Decimal, DbType.Double, DbType.Guid, DbType.Int16, DbType.Int32, DbType.Int64, DbType.SByte, DbType.Single, DbType.String, DbType.UInt16, DbType.UInt32, DbType.UInt64, DbType.VarNumeric, DbType.AnsiStringFixedLength, DbType.StringFixedLength, DbType.Xml, DbType.DateTime2, DbType.DateTimeOffset),
        ["GetGuid"] = Mask(DbType.AnsiString, DbType.Binary, DbType.Byte, DbType.Boolean, DbType.Currency, DbType.Date, DbType.DateTime, DbType.Decimal, DbType.Double, DbType.Int16, DbType.Int32, DbType.Int64, DbType.SByte, DbType.Single, DbType.String, DbType.Time, DbType.UInt16, DbType.UInt32, DbType.UInt64, DbType.VarNumeric, DbType.AnsiStringFixedLength, DbType.StringFixedLength, DbType.Xml, DbType.DateTime2, DbType.DateTimeOffset),
        ["GetFieldValue<byte[]>"] = Mask(DbType.Byte, DbType.Boolean, DbType.Currency, DbType.Date, DbType.DateTime, DbType.Decimal, DbType.Double, DbType.Guid, DbType.Int16, DbType.Int32, DbType.Int64, DbType.SByte, DbType.Single, DbType.Time, DbType.UInt16, DbType.UInt32, DbType.UInt64, DbType.VarNumeric, DbType.DateTime2, DbType.DateTimeOffset),
    };

    // True when the dialect's provider rejects a parameter bound through readerMethod, with
    // dbType as its DbType. False when it accepts the pair, and when the pair was not probed:
    // an unknown dialect, or a defined DbType on a bound type outside the table.
    public static bool Rejects(SqlDialectModel dialect, string readerMethod, int dbType)
    {
        var table = dialect switch
        {
            SqlDialectModel.SqlServer => SqlServer,
            SqlDialectModel.PostgreSql => PostgreSql,
            _ => null,
        };
        if (table is null)
            return false;

        // Both providers reject a value outside the enum, such as (DbType)999, when it is
        // assigned, whatever the value's type.
        if (!Enum.IsDefined(typeof(DbType), dbType))
            return true;
        return table.TryGetValue(readerMethod, out var mask) && (mask & Bit((DbType)dbType)) != 0;
    }

    // The bound types the table covers, as reader methods.
    public static IEnumerable<string> ProbedReaderMethods => SqlServer.Keys;

    // The provider a dialect stands for, as a diagnostic names it.
    public static string ProviderName(SqlDialectModel dialect) => dialect switch
    {
        SqlDialectModel.SqlServer => "Microsoft.Data.SqlClient",
        SqlDialectModel.PostgreSql => "Npgsql",
        SqlDialectModel.Sqlite => "Microsoft.Data.Sqlite",
        SqlDialectModel.MySql => "MySqlConnector",
        _ => dialect.ToString(),
    };

    private static ulong Mask(params DbType[] dbTypes)
    {
        ulong mask = 0;
        foreach (var dbType in dbTypes)
            mask |= Bit(dbType);
        return mask;
    }

    // Every DbType member is below 64.
    private static ulong Bit(DbType dbType) => 1UL << (int)dbType;
}
