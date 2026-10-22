namespace ZeroAlloc.ORM;

/// <summary>
/// The PostgreSQL type a <see cref="System.DateTime"/> column is stored as, which the generator
/// cannot see from the entity. Set it for every column with
/// <see cref="EntityMapDefaultsAttribute.DateTimeStorage"/> or for one column with
/// <see cref="ColumnMapAttribute.DateTimeStorage"/>. It changes only how a LINQ query renders
/// <c>.Date</c> on the column.
/// </summary>
public enum DateTimeStorage
{
    /// <summary>
    /// Not declared: <c>.Date</c> renders <c>date_trunc('day', col)</c>, which over a
    /// <c>timestamptz</c> cuts at the day of the session's <c>TimeZone</c>.
    /// </summary>
    Default = 0,

    /// <summary>
    /// A <c>timestamp</c>, without time zone: <c>.Date</c> renders <c>date_trunc('day', col)</c>,
    /// which is exact.
    /// </summary>
    Timestamp = 1,

    /// <summary>
    /// A <c>timestamptz</c>, which Npgsql reads as a UTC <see cref="System.DateTime"/>:
    /// <c>.Date</c> renders <c>date_trunc('day', col, 'UTC')</c>, the UTC day whatever the
    /// session's <c>TimeZone</c>, read with Kind Utc as C#'s <c>.Date</c> keeps it. Needs
    /// PostgreSQL 12 or later.
    /// </summary>
    TimestampTz = 2,
}
