namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// Daedalus' CodeAnalysisRequest, trimmed to its owned objects. Never constructed by the ORM.
public sealed class AnalysisRequestEntity
{
    public Guid Id { get; private set; }
    public string Title { get; private set; } = "";
    public RepositoryReference Repository { get; private set; } = null!;
    public CodeLocation Location { get; private set; } = null!;
    public AnalysisOutcome? Outcome { get; private set; }
    public AnalysisReview? Review { get; private set; }
}
