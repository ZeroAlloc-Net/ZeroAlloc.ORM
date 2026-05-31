namespace ZeroAlloc.ORM.Generator.Model;

// v0.4 Phase E — materialization plan for `[StoredProcedure]` methods returning
// a named tuple whose field names map to C# parameters (output positions) and/or
// to result-set positions (row / scalar / list, same as MultiResultElement).
//
// Two element lists keep the emit conditional simple: ResultElements is iterated
// to walk the reader (one ReadAsync per row position, one List collector per
// list position), then OutputElements is iterated to copy DbParameter.Value into
// the returned tuple. Tuple-position ordering is preserved separately on
// TupleElementOrder so the final `return (...)` expression matches the user's
// declaration verbatim.
//
// Output-only sub-case (Task E.3): ResultElements is empty. The emit switches
// from ExecuteReaderAsync to ExecuteNonQueryAsync — the parameter collection
// is populated regardless of which Execute* method runs as long as the
// CommandType is StoredProcedure and the parameter direction is Output.
//
// Cache-safety: every field is a string or EquatableArray<string>; no Roslyn
// symbols leak in. The model is consumed downstream by EmitSprocWithOutputParams
// alongside QueryMethodModel.

// One output-parameter tuple position. Emit semantics:
//   1. The C# parameter `MatchingParameterName` is bound with
//      Direction = ParameterDirection.Output.
//   2. After the command runs (reader drained + disposed, or ExecuteNonQueryAsync
//      returned), the parameter's `.Value` is unboxed through
//      BuildScalarConvertExpression keyed on TypeName and wrapped via the
//      Convention if the tuple type is a value-object / single-arg-ctor / enum.
//
//   TupleFieldName            -- name of the tuple element as declared by the
//                                 user (e.g. "NewOrderId"). Used by the emit
//                                 only for naming the readback local; the
//                                 tuple-position ordering lives in
//                                 TupleElementOrder on the parent model.
//   MatchingParameterName     -- name of the matching C# parameter (e.g.
//                                 "newOrderId"). The emit uses this to:
//                                  * format the DbParameter name (`@newOrderId`)
//                                  * locate the captured `__p_newOrderId` local
//   TypeName                  -- fully-qualified UNWRAPPED type display of the
//                                 tuple element. Used by BuildScalarConvertExpression
//                                 to pick the Convert.ToXxx funnel. For VO /
//                                 single-arg-ctor / enum the UnderlyingReader on
//                                 Convention drives the cast target instead.
//   IsNullable                -- true when the tuple element type is annotated
//                                 nullable (`int?` / `string?`). Currently
//                                 forwarded for symmetry with ColumnBinding; the
//                                 emit may guard `.Value` for DBNull when set.
//   Convention                -- ConventionInfo for value-object / enum / etc.
//                                 wrapping. Null for bare primitives.
internal sealed record SprocOutputParam(
    string TupleFieldName,
    string MatchingParameterName,
    string TypeName,
    bool IsNullable,
    ConventionInfo? Convention);

// Discriminator for TupleElementOrder entries — distinguishes "this slot is the
// i-th OUTPUT element" vs "this slot is the i-th RESULT element". The emit walks
// TupleElementOrder in order to build the return-tuple expression and picks the
// matching index in OutputElements / ResultElements per entry.
internal enum SprocTupleSlotKind
{
    Output,
    Result,
}

// One tuple-position slot. The pair (Kind, IndexWithinKind) routes the slot to
// the matching list (Output -> OutputElements, Result -> ResultElements) at
// emit time so the final tuple expression interleaves output and result
// positions in declaration order.
internal sealed record SprocTupleSlot(
    SprocTupleSlotKind Kind,
    int IndexWithinKind);

// Top-level model. Mirrors MultiResultMaterializationModel's cache-safety contract
// (strings + EquatableArray) so it can sit on QueryMethodModel without breaking
// incremental-generator equality.
//
//   TupleTypeDisplay     -- fully-qualified tuple type display incl. any outer
//                            nullable annotation. Used verbatim in the partial
//                            method's return-type position.
//   OutputElements       -- one per tuple field that matched a C# parameter, in
//                            declaration order.
//   ResultElements       -- one per tuple field that did NOT match a C# parameter,
//                            in declaration order. Each is a MultiResultElement
//                            (Scalar / Row / List) reusing the existing emit
//                            machinery. Empty for the output-only sub-case
//                            (Task E.3 -- ExecuteNonQueryAsync path).
//   TupleElementOrder    -- interleaved (Output | Result, index) entries in the
//                            tuple declaration order. Length equals
//                            OutputElements.Length + ResultElements.Length.
internal sealed record SprocOutputParamsMaterializationModel(
    string TupleTypeDisplay,
    EquatableArray<SprocOutputParam> OutputElements,
    EquatableArray<MultiResultElement> ResultElements,
    EquatableArray<SprocTupleSlot> TupleElementOrder);
