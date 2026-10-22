namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// Url, Branch and FilePath carry Daedalus' bare column names; every other owned member keeps
// EF's {Owner}_{Property}, such as "Repository_Platform".
[TableMap(typeof(AnalysisRequestEntity), "AnalysisRequests")]
[OwnedMap(nameof(AnalysisRequestEntity.Repository))]
[OwnedMap(nameof(AnalysisRequestEntity.Location))]
[OwnedMap(nameof(AnalysisRequestEntity.Outcome))]
[OwnedMap(nameof(AnalysisRequestEntity.Review))]
[ColumnMap("Repository.Url", Column = "RepositoryUrl")]
[ColumnMap("Repository.Branch", Column = "RepositoryBranch")]
[ColumnMap("Location.FilePath", Column = "FilePath")]
internal static class AnalysisRequestMap;
