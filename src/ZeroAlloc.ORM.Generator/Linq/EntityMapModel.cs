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
/// Every public readable instance property of the entity with a setter or a backing field: base
/// type first, then declaration order.
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
internal sealed record ColumnMapModel(
    string Property,
    string Column,
    bool EnumAsString) : IEquatable<ColumnMapModel>;

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
