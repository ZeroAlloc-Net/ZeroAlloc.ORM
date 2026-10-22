using AwesomeAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// The last of Daedalus' seven Include sites against a real PostgreSQL: CodeAnalysisRepository
// loads a request with its owned objects and its iterations. Owned columns from the map and a
// tuple companion together, in one batch.
[Trait("Provider", "Postgres")]
public sealed class PostgresLinqIncludeTests
{
    private static async Task<PostgresFixture> CreateAsync()
    {
        PostgresFixture? fx = null;
        try
        {
            fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(LinqSchema.Ddl).ConfigureAwait(false);
            return fx;
        }
        catch
        {
            if (fx is not null)
            {
                await fx.DisposeAsync().ConfigureAwait(false);
            }
            throw;
        }
    }

    private static async Task<Guid> InsertAsync(LinqIncludeRepo repo, AnalysisRequestLine row, bool? hasFailedValidation)
    {
        var id = Guid.NewGuid();
        await repo.InsertRequestAsync(
            id,
            row.Title,
            row.Url,
            row.Branch,
            row.CommitSha,
            row.Platform,
            row.FilePath,
            row.StartLine,
            row.EndLine,
            row.PullRequestUrl,
            row.ValidationResult,
            hasFailedValidation,
            CancellationToken.None).ConfigureAwait(false);
        return id;
    }

    private static readonly AnalysisRequestLine Reviewed = new(
        "reviewed", "https://github.com/a/a", "main", "abc123", RepositoryPlatform.GitHub, "src/A.cs", 30, 40,
        "https://github.com/a/a/pull/1", """{"ok": false}""");

    private static readonly AnalysisRequestLine Pending = new(
        "pending", "https://gitlab.com/b/b", null, null, RepositoryPlatform.GitLab, "src/B.cs", null, null,
        null, null);

    // CodeAnalysisRepository: the request by id with every owned member, its outcome, and its
    // iterations in Number order, written out of order, without another request's. A request
    // without an outcome reads no outcome row; a missing request reads nothing at all.
    [Fact]
    public async Task Daedalus_code_analysis_request_with_iterations()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqIncludeRepo(fx.Connection);
        var reviewed = await InsertAsync(repo, Reviewed, hasFailedValidation: true).ConfigureAwait(false);
        var pending = await InsertAsync(repo, Pending, hasFailedValidation: null).ConfigureAwait(false);
        await repo.InsertIterationAsync(Guid.NewGuid(), reviewed, 2, "fixed the null check", CancellationToken.None).ConfigureAwait(false);
        await repo.InsertIterationAsync(Guid.NewGuid(), reviewed, 1, "found a null dereference", CancellationToken.None).ConfigureAwait(false);
        await repo.InsertIterationAsync(Guid.NewGuid(), pending, 1, "elsewhere", CancellationToken.None).ConfigureAwait(false);

        var (request, outcome, iterations) = await repo.RequestWithIterationsAsync(reviewed, CancellationToken.None).ConfigureAwait(false);
        request.Should().Be(Reviewed);
        outcome.Should().Be(new OutcomeFlag("reviewed", true));
        iterations.Should().Equal(
            new IterationLine(1, "found a null dereference"),
            new IterationLine(2, "fixed the null check"));

        var withoutOutcome = await repo.RequestWithIterationsAsync(pending, CancellationToken.None).ConfigureAwait(false);
        withoutOutcome.Request.Should().Be(Pending);
        withoutOutcome.Outcome.Should().BeNull();
        withoutOutcome.Iterations.Should().Equal(new IterationLine(1, "elsewhere"));

        var missing = await repo.RequestWithIterationsAsync(Guid.NewGuid(), CancellationToken.None).ConfigureAwait(false);
        missing.Request.Should().BeNull();
        missing.Outcome.Should().BeNull();
        missing.Iterations.Should().BeEmpty();
    }
}
