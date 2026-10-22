using AwesomeAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// EF owned types against a real PostgreSQL: AnalysisRequests' owned Repository, Location and
// optional Outcome read as columns of the table, under dotted ColumnMap overrides and EF's
// {Owner}_{Property} default. Each case boots its own container, so related checks share a case.
[Trait("Provider", "Postgres")]
public sealed class PostgresLinqOwnedTypeTests
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

    private static Task<int> InsertAsync(LinqOwnedTypeRepo repo, AnalysisRequestLine row, bool? hasFailedValidation)
        => repo.InsertAsync(
            Guid.NewGuid(),
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
            CancellationToken.None);

    private static readonly AnalysisRequestLine Alpha = new(
        "alpha", "https://github.com/a/a", "main", "abc123", RepositoryPlatform.GitHub, "src/A.cs", 30, 40,
        "https://github.com/a/a/pull/1", """{"ok": false}""");

    private static readonly AnalysisRequestLine Beta = new(
        "beta", "https://github.com/b/b", null, null, RepositoryPlatform.GitHub, "src/B.cs", 10, null,
        null, """{"ok": false}""");

    private static readonly AnalysisRequestLine Gamma = new(
        "gamma", "https://gitlab.com/c/c", "dev", "def456", RepositoryPlatform.GitLab, "src/C.cs", 5, 6,
        "https://gitlab.com/c/c/-/merge_requests/2", """{"ok": false}""");

    private static readonly AnalysisRequestLine Delta = new(
        "delta", "https://github.com/d/d", "main", null, RepositoryPlatform.GitHub, "src/D.cs", 1, 2,
        "https://github.com/d/d/pull/3", """{"ok": true}""");

    // Every owned member round-trips through its own column, renamed or not, and Where and
    // OrderBy read the same columns.
    [Fact]
    public async Task Owned_columns_round_trip_with_overrides()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqOwnedTypeRepo(fx.Connection);
        await InsertAsync(repo, Alpha, hasFailedValidation: true).ConfigureAwait(false);
        await InsertAsync(repo, Beta, hasFailedValidation: true).ConfigureAwait(false);
        await InsertAsync(repo, Gamma, hasFailedValidation: true).ConfigureAwait(false);
        await InsertAsync(repo, Delta, hasFailedValidation: false).ConfigureAwait(false);

        // jsonb hands its text back normalized, which these values already are.
        (await repo.AllAsync(CancellationToken.None).ConfigureAwait(false))
            .Should().Equal(Alpha, Beta, Delta, Gamma);

        // GitHub and failed, by StartLine: Beta's 10 before Alpha's 30; Delta passed, Gamma is GitLab.
        (await repo.FailedAsync(RepositoryPlatform.GitHub, CancellationToken.None).ConfigureAwait(false))
            .Should().Equal(Beta, Alpha);
        (await repo.FailedAsync(RepositoryPlatform.GitLab, CancellationToken.None).ConfigureAwait(false))
            .Should().Equal(Gamma);
    }

    // A request without an outcome has every Outcome column NULL: its members read as NULL, where
    // C# would throw on the null Outcome. Its HasFailedValidation is NULL, not TRUE.
    [Fact]
    public async Task Null_owned_columns_read_as_nulls()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqOwnedTypeRepo(fx.Connection);
        var pending = Alpha with { Title = "pending", PullRequestUrl = null, ValidationResult = null };
        await InsertAsync(repo, pending, hasFailedValidation: null).ConfigureAwait(false);
        await InsertAsync(repo, Delta, hasFailedValidation: true).ConfigureAwait(false);

        (await repo.AllAsync(CancellationToken.None).ConfigureAwait(false))
            .Should().Equal(Delta, pending);
        (await repo.WithoutPullRequestAsync(CancellationToken.None).ConfigureAwait(false))
            .Should().Equal(pending);
        (await repo.FailedAsync(RepositoryPlatform.GitHub, CancellationToken.None).ConfigureAwait(false))
            .Should().Equal(Delta);
    }
}
