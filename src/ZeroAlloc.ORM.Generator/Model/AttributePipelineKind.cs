namespace ZeroAlloc.ORM.Generator.Model;

// v0.4 Phase D — discriminator for the three attribute pipelines feeding
// TransformMethod. Replaces the Phase A `isCommandAttribute` bool. The enum keeps
// the dispatch surface explicit (every call site picks one of three named values)
// and removes the boolean-pair ambiguity that two bools (`isCommandAttribute`,
// `isStoredProcedureAttribute`) would invite.
//
// Lives in Model/ for parity with CommandKindModel — the convention is "every
// generator-side enum that the pipeline branches on lives next to QueryMethodModel"
// so a single grep over the Model/ folder surfaces every dispatch axis.
internal enum AttributePipelineKind
{
    Query,
    Command,
    StoredProcedure,
}
