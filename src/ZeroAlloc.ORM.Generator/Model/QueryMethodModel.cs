namespace ZeroAlloc.ORM.Generator.Model;

// Classification of the emit template a [Query] method should use.
// Phase 4.1 introduces ScalarInt; future shapes (ScalarNullable, FlatRow, etc.)
// land in later Phase 4/5 tasks. Methods that don't match any known shape stay
// Unknown and emit the v0.1 stub comment.
internal enum EmitShape
{
    Unknown,
    ScalarInt,
    NullableScalar,
    FlatRow,
    // Multi-arg class with column-name-keyed reads. Detected when the return-type
    // element is a `class` (not a record) with exactly one public ctor whose params
    // all resolve to known conventions. Distinct from FlatRow because the column
    // bind uses GetOrdinal("ColumnName") instead of a positional index — class ctor
    // parameter names map to PascalCased column identifiers.
    DomainEntity,
    // v0.3 Phase B — tuple return type where each element is independently materialized
    // from successive reader result sets. Element kinds (Scalar / Row / List) are
    // carried via MultiResultMaterializationModel.
    MultiResultSet,
    // v0.3 Phase C — IAsyncEnumerable<T> return type emitted as a yield-based
    // async iterator. Element materialization uses the same FlatRow / DomainEntity
    // models as the single-row paths; the surrounding emit replaces "ReadAsync once
    // then return" with "while ReadAsync yield return".
    Streaming,
    // v0.4 Phase A — [Command(Kind = NonQuery)] methods. Emits the open/execute/close
    // shape against ExecuteNonQueryAsync, returning the rows-affected count (or void
    // for Task / ValueTask). Scalar / Identity variants land in Phase B / Phase C.
    CommandNonQuery,
    // v0.4 Phase B — [Command(Kind = Scalar)] methods. Emits the open/execute/close
    // lifecycle around ExecuteScalarAsync. Result materialization follows the
    // ConventionDiscovery path so primitives, value-objects, single-arg-ctor records,
    // and enums all funnel through a single shape. Nullable variants (Task<T?>)
    // emit a DBNull/null guard before the cast.
    CommandScalar,
    // v0.4 Phase C — [Command(Kind = Identity)] methods. Structurally identical to
    // CommandScalar's non-nullable branch — open/execute/close around ExecuteScalarAsync
    // and a Convert.ToXxx + optional VO factory wrap. The differences are:
    //   * Identity is never nullable (the SQL contract requires the RETURNING /
    //     SCOPE_IDENTITY() clause to produce a non-null value); the null guard
    //     always throws InvalidOperationException with an Identity-specific message.
    //   * Classification only accepts int / long / Guid (or a VO wrapping one of
    //     those) — narrower than Scalar's full primitive/enum range, matching the
    //     identity-key idiom across providers.
    CommandIdentity,
    // v0.4 Phase E — [StoredProcedure] methods returning a named tuple whose
    // field names match (case-insensitive) at least one C# parameter on the
    // method. The matching tuple positions emit Direction = ParameterDirection.Output
    // on the bound DbParameter and read the parameter value back into the
    // returned tuple after the command runs. Non-matching tuple positions
    // (if any) are classified via the existing MultiResultElement rules
    // (Scalar / Row / List) so a single-result-row + output-param shape and a
    // multi-result-set + output-param shape both flow through the same emit.
    //
    // Detection rule:
    //   * At least one tuple element's name matches a C# parameter name
    //     (StringComparer.OrdinalIgnoreCase) -> SprocWithOutputParams.
    //   * Zero matches -> fall through to MultiResultSet (existing Phase D path).
    //
    // Output-only sub-case (Task E.3): every tuple element matches a C# parameter.
    // The emit detects this via SprocOutputParamsMaterializationModel.ResultElements
    // being empty and switches from ExecuteReaderAsync to ExecuteNonQueryAsync;
    // a single shape value keeps the classifier surface narrow.
    SprocWithOutputParams,
    // v0.5 Phase A — multi-column composite at scalar return position.
    // `Task<Money>` where `Money(decimal Amount, string Currency)`: the SELECT list
    // produces N columns and the emit constructs the composite via `new T(reader.GetXxx(0), ...)`.
    // Nested composites in FlatRow / DomainEntity rows still ride those shapes;
    // Composite is the standalone scalar-position shape.
    Composite,
}

// Mirror of ZeroAlloc.ORM.Abstractions.CommandKind. Re-declared on the model side
// so QueryMethodModel stays cache-safe (no symbol/Compilation refs leak in via the
// abstraction enum's metadata; this enum is a plain value type).
internal enum CommandKindModel
{
    NonQuery,
    Scalar,
    Identity,
}

// Per-method emit input. Type-scoped fields (ContainingTypeName, Namespace,
// ConnectionAccess, ConnectionResolved, ContainingTypePartial, ContainingTypeLocation)
// were hoisted to QueryRepositoryModel in R8 to remove the per-method redundancy
// and avoid the "pick Methods[0] as representative" fallback in OrmGenerator.
internal sealed record QueryMethodModel(
    string MethodName,
    string ContainingTypeFullName,
    string Sql,
    EmitShape Shape,
    BatchEmitStrategy Strategy,
    string ReturnTypeDisplay,
    string? NullableScalarReaderMethod,
    MaterializationModel? Materialization,
    MultiResultMaterializationModel? MultiResultMaterialization,
    EquatableArray<ParameterInfo> MethodParameters,
    string? CancellationTokenParameterName,
    EquatableArray<DiagnosticInfo> Diagnostics,
    // v0.4 Phase A — true when the source method is annotated with [Command]; false
    // when [Query] (the Query case is implicit). Drives the EmitShape.CommandNonQuery
    // (and future Scalar / Identity) dispatch. ZAO005 fires when a method carries
    // both [Query] and [Command]. No default — thread the value explicitly at both
    // TransformMethod call sites (Query pipeline passes `false, NonQuery`; Command
    // pipeline passes `true, kind`).
    bool IsCommand,
    CommandKindModel CommandKind,
    // HasReturnValue — true when the method declares a non-void return type that
    // emit must produce a value for. For [Command] this is `Task<int> / ValueTask<int>`;
    // for [Query] it's any non-void return. Captured authoritatively in
    // ClassifyEmitShape so EmitCommandNonQuery (and any future emit shape that
    // branches on return-arity) can read it without string-sniffing ReturnTypeDisplay.
    bool HasReturnValue,
    // v0.4 Phase D — true when the source method is annotated with [StoredProcedure];
    // false for [Query] and [Command]. Flips the emit's `__cmd.CommandText` assignment
    // from "the SQL string" to "the ProcedureName" plus an explicit
    // `__cmd.CommandType = CommandType.StoredProcedure;` line. Single-result-set
    // shapes (FlatRow / DomainEntity / Scalar / NullableScalar) and multi-result-set
    // shapes (MultiResultSet, joined-statements variant) thread the flag uniformly.
    // [Query] / [Command] both pass `false` and an empty ProcedureName.
    bool IsStoredProcedure,
    string ProcedureName,
    // v0.4 Phase E — materialization plan for [StoredProcedure] tuple returns
    // with output parameters. Set when Shape == EmitShape.SprocWithOutputParams;
    // null for every other emit shape (and for sprocs without matching tuple
    // fields, which fall through to the MultiResultSet shape with the existing
    // multiResultMaterialization carrying the plan). See
    // SprocOutputParamsMaterializationModel for the per-element structure.
    SprocOutputParamsMaterializationModel? SprocOutputParamsMaterialization,
    // v1.2 — declared accessibility of the partial method as the C# keyword(s)
    // that should be re-emitted on the implementation side. C# requires partial
    // method declarations and implementations to have *identical* accessibility
    // modifiers (CS8799), so the generator must thread the user's choice through
    // rather than hardcoding `public`. Mapping is straight from
    // Microsoft.CodeAnalysis.Accessibility to its keyword form:
    //   Public               -> "public"
    //   Internal             -> "internal"
    //   Protected            -> "protected"
    //   ProtectedOrInternal  -> "protected internal"  (C# operator)
    //   ProtectedAndInternal -> "private protected"   (C# operator)
    //   Private              -> "private"
    // Captured at TransformMethod time; never null.
    string MethodAccessibilityKeyword);

internal sealed record QueryRepositoryModel(
    string ContainingTypeFullName,
    string ContainingTypeName,
    string? Namespace,
    string ConnectionAccess,
    bool ConnectionResolved,
    bool ContainingTypePartial,
    LocationInfo? ContainingTypeLocation,
    EquatableArray<QueryMethodModel> Methods);

// Intermediate carrier emitted by TransformMethod. Bundles the method-scoped
// model with the type-scoped fields so the grouping step in OrmGenerator.Initialize
// can build a QueryRepositoryModel without re-reading symbols. Every entry in a
// group shares identical type-scoped values (same containing type), so the grouping
// just takes the first.
internal sealed record QueryMethodWithTypeContext(
    QueryMethodModel Method,
    string ContainingTypeName,
    string? Namespace,
    string ConnectionAccess,
    bool ConnectionResolved,
    bool ContainingTypePartial,
    LocationInfo? ContainingTypeLocation);
