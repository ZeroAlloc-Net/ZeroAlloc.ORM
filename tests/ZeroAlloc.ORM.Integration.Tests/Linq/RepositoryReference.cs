namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// Owned by AnalysisRequestEntity: a value object with private setters, as Daedalus has it.
public sealed class RepositoryReference
{
    public string Url { get; private set; } = "";
    public string? Branch { get; private set; }
    public string? CommitSha { get; private set; }
    public RepositoryPlatform Platform { get; private set; }
}
