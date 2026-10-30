namespace ZeroAlloc.ORM;

/// <summary>
/// The PostgreSQL type a <see cref="System.DateTime"/> column is stored as, which the generator
/// cannot see from the entity. Set it for every column with
/// <see cref="EntityMapDefaultsAttribute.DateTimeStorage"/> or for one column with
/// <see cref="ColumnMapAttribute.DateTimeStorage"/>. It decides how a LINQ query renders
/// <c>.Date</c> on the column, and the type a <c>DateTime</c> parameter binds as when a LINQ
/// query or command compares it with the column or sets it into the column.
/// </summary>
/// <remarks>
/// A parameter that meets a <see cref="Timestamp"/> or <see cref="TimestampTz"/> column binds
/// with that type explicitly, as EF Core's Npgsql provider binds it. Npgsql then rejects a value
/// whose <see cref="System.DateTime.Kind"/> does not fit the column with an
/// <see cref="System.ArgumentException"/> before the command is sent, rather than PostgreSQL
/// shifting it through the session's <c>TimeZone</c>. A parameter that would meet both a
/// <c>timestamp</c> and a <c>timestamptz</c> column reports ZAO102. <c>[Query]</c> and
/// <c>[Command]</c> methods have no map, so this does not apply to them.
/// </remarks>
public enum DateTimeStorage
{
    /// <summary>
    /// Not declared: <c>.Date</c> renders <c>date_trunc('day', col)</c>, which over a
    /// <c>timestamptz</c> cuts at the day of the session's <c>TimeZone</c>. A parameter compared
    /// with the column binds as Npgsql infers from its <see cref="System.DateTime.Kind"/>: a
    /// <c>timestamptz</c> for <see cref="System.DateTimeKind.Utc"/>, a <c>timestamp</c> otherwise,
    /// which PostgreSQL converts through the session's <c>TimeZone</c> to meet a column of the
    /// other type.
    /// </summary>
    Default = 0,

    /// <summary>
    /// A <c>timestamp</c>, without time zone: <c>.Date</c> renders <c>date_trunc('day', col)</c>,
    /// which is exact. A parameter compared with or set into the column binds as a
    /// <c>timestamp</c>: a <see cref="System.DateTimeKind.Local"/> or
    /// <see cref="System.DateTimeKind.Unspecified"/> value is sent as its wall clock, and Npgsql
    /// rejects a <see cref="System.DateTimeKind.Utc"/> one.
    /// </summary>
    Timestamp = 1,

    /// <summary>
    /// A <c>timestamptz</c>, which Npgsql reads as a UTC <see cref="System.DateTime"/>:
    /// <c>.Date</c> renders <c>date_trunc('day', col, 'UTC')</c>, the UTC day whatever the
    /// session's <c>TimeZone</c>, read with Kind Utc as C#'s <c>.Date</c> keeps it. Needs
    /// PostgreSQL 12 or later. A parameter compared with or set into the column binds as a
    /// <c>timestamptz</c>: a <see cref="System.DateTimeKind.Utc"/> value is sent as its moment,
    /// and Npgsql rejects a <see cref="System.DateTimeKind.Local"/> or
    /// <see cref="System.DateTimeKind.Unspecified"/> one.
    /// </summary>
    TimestampTz = 2,
}
