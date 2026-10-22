using System;
using ZeroAlloc.ORM.Generator.Model;

namespace ZeroAlloc.ORM.Generator.Linq;

/// <summary>
/// One mapped entity, read from a <c>[TableMap]</c> and its <c>[ColumnMap]</c> overrides. Holds no
/// symbols, so the incremental pipeline can cache it.
/// </summary>
/// <param name="EntityTypeFullName">
/// The entity type, fully qualified with <c>global::</c>; the key a query's source type is matched on.
/// </param>
/// <param name="Table">The table name.</param>
/// <param name="Schema">The schema, or null for the connection default.</param>
/// <param name="Columns">
/// Every public readable instance property of the entity that holds a value, plus those a
/// <c>[ColumnMap]</c> names and minus those it marks <c>Ignore</c>: base type first, then
/// declaration order.
/// </param>
internal sealed record EntityMapModel(
    string EntityTypeFullName,
    string Table,
    string? Schema,
    EquatableArray<ColumnMapModel> Columns) : IEquatable<EntityMapModel>;

/// <summary>One mapped property.</summary>
/// <param name="Property">The C# property name.</param>
/// <param name="Column">The column name: the property name unless a <c>[ColumnMap]</c> overrides it.</param>
/// <param name="EnumAsString">Whether the enum property is stored as its member name.</param>
/// <param name="DateTimeStorage">
/// For a <c>DateTime</c> or <c>DateTime?</c> property, how it is stored: its <c>[ColumnMap]</c>
/// setting, else the assembly's <c>[EntityMapDefaults]</c>, else <see cref="DateTimeColumnStorage.Default"/>.
/// <see cref="DateTimeColumnStorage.None"/> for any other property.
/// </param>
internal sealed record ColumnMapModel(
    string Property,
    string Column,
    bool EnumAsString,
    DateTimeColumnStorage DateTimeStorage = DateTimeColumnStorage.None) : IEquatable<ColumnMapModel>;

/// <summary>
/// The generator's view of <c>ZeroAlloc.ORM.DateTimeStorage</c> for one column, with
/// <see cref="None"/> for a column that is not a <c>DateTime</c>. The other members keep the
/// public enum's values plus one.
/// </summary>
internal enum DateTimeColumnStorage
{
    /// <summary>Not a <c>DateTime</c> column.</summary>
    None = 0,

    /// <summary>A <c>DateTime</c> column whose store type nothing declares.</summary>
    Default = 1,

    /// <summary>A <c>DateTime</c> column declared a <c>timestamp</c>.</summary>
    Timestamp = 2,

    /// <summary>A <c>DateTime</c> column declared a <c>timestamptz</c>.</summary>
    TimestampTz = 3,
}

/// <summary>
/// What one map class yields: its model when the entity resolved, and the diagnostics found in it.
/// The table attribute's location (file path, then position) orders the duplicate check across map classes.
/// </summary>
internal sealed record EntityMapReadResult(
    EntityMapModel? Model,
    EquatableArray<DiagnosticInfo> Diagnostics,
    LocationInfo? TableMapLocation) : IEquatable<EntityMapReadResult>;

/// <summary>
/// Every map in the compilation, after the duplicate check: the maps Task-5 consumers combine with
/// query methods, and the ZAO106 diagnostics to report.
/// </summary>
internal sealed record EntityMapSet(
    EquatableArray<EntityMapModel> Maps,
    EquatableArray<DiagnosticInfo> Diagnostics) : IEquatable<EntityMapSet>;
