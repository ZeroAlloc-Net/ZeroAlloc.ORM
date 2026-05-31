namespace ZeroAlloc.ORM.Generator.Model;

// Per-tuple-element materialization kind for the MultiResultSet emit shape (v0.3 Phase B).
// Each kind drives a distinct reader-loop pattern:
//   * Scalar — single column, single value (one ReadAsync, IsDBNull-guarded GetXxx(0)).
//   * Row    — single row materialization (one ReadAsync, ctor invocation, return null if empty).
//   * List   — multi-row collector (while-ReadAsync loop, List<T>.Add).
internal enum MultiResultElementKind
{
    Scalar,
    Row,
    List,
}

// One tuple element's materialization plan. Mirrors the cache-safety contract of
// MaterializationModel — strings + primitives + EquatableArray<ColumnBinding> — so the
// surrounding QueryMethodModel stays equatable across incremental-generator runs.
//
//   Kind            -- discriminator: Scalar / Row / List (see MultiResultElementKind).
//   TupleFieldName  -- the element's name as declared in the tuple type
//                      (e.g. "Head", "Lines"). Used by the emitter to render the
//                      local variable suffix and for diagnostic context.
//   ElementTypeName -- fully-qualified element CLR type display. For Scalar this is
//                      the scalar type (or its enum/value-object wrapper); for Row
//                      it's the record/class type being constructed; for List it's
//                      the inner-element type (the List<T>'s `T`). Used by the
//                      emitter both for `new T(...)` ctor calls and for the
//                      `List<T>` instantiation in List-kind elements.
//   GetterMethod    -- only meaningful for Scalar; the IDataReader.GetXxx method
//                      (e.g. "GetInt32"). Null for Row/List (their columns live
//                      in Columns below).
//   Convention      -- only meaningful for Scalar; carries the
//                      ValueObject / SingleArgCtor / StaticFactory / Enum factory
//                      shape. Null when the Scalar element is a primitive.
//   IsNullable      -- Scalar nullability for the element type.
//   Columns         -- per-column bindings for Row / List elements; same shape as
//                      MaterializationModel.Columns. Empty for Scalar.
internal sealed record MultiResultElement(
    MultiResultElementKind Kind,
    string TupleFieldName,
    string ElementTypeName,
    string? GetterMethod,
    ConventionInfo? Convention,
    bool IsNullable,
    EquatableArray<ColumnBinding> Columns);

// Materialization plan for a tuple return type. Each `Elements[i]` corresponds to
// the i-th tuple element AND the i-th result set produced by the multi-statement
// SQL.
//
//   TupleTypeDisplay -- the fully-qualified tuple type display incl. the outer
//                       nullable annotation (e.g. "(global::TestApp.OrderRow Head,
//                       global::System.Collections.Generic.List<global::TestApp.OrderLineRow>
//                       Lines)?"). The emitter uses this verbatim in the partial
//                       method's return-type position.
//   ReturnsNullable  -- true when the user-declared return is `Task<(...)?>`. The
//                       emit returns `null` on first-result-set empty when set;
//                       throws ZeroAllocOrmMaterializationException otherwise.
//   Elements         -- per-tuple-element plans, in tuple declaration order.
internal sealed record MultiResultMaterializationModel(
    string TupleTypeDisplay,
    bool ReturnsNullable,
    EquatableArray<MultiResultElement> Elements);
