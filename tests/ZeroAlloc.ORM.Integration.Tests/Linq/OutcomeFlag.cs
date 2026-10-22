namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// HasFailedValidation of the optional Outcome: NULL for a request without one, so bool?.
internal sealed record OutcomeFlag(string Title, bool? Failed);
