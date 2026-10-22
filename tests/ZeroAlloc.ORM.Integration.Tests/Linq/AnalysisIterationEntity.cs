namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// Daedalus' CodeAnalysisIteration: one model pass over a code analysis request.
public sealed class AnalysisIterationEntity
{
    public Guid Id { get; private set; }
    public Guid RequestId { get; private set; }
    public int Number { get; private set; }
    public string Summary { get; private set; } = "";
}
