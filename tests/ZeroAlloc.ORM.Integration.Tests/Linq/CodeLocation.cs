namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// Owned by AnalysisRequestEntity.
public sealed class CodeLocation
{
    public string FilePath { get; private set; } = "";
    public int? StartLine { get; private set; }
    public int? EndLine { get; private set; }
}
