using AwesomeAssertions;
using AwesomeAssertions.Execution;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// [LinqQuery] Where clauses against a real PostgreSQL. Each case boots its own container, so
// related checks share a case.
[Trait("Provider", "Postgres")]
public sealed class PostgresLinqWhereTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 21, 8, 0, 0, TimeSpan.Zero);

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

    private sealed record Seed(Guid Id, ProjectId Project, string Title, WorkStatus Status, WorkKind Kind, DateTimeOffset CreatedAt, DateTimeOffset? DueAt, int Order);

    private static async Task SeedAsync(PostgresFixture fx, params Seed[] rows)
    {
        var repo = new LinqRepo(fx.Connection);
        foreach (var r in rows)
        {
            await repo.InsertTaskAsync(r.Id, r.Project, r.Title, r.Status, r.Kind.ToString(), r.CreatedAt, r.DueAt, [], r.Order, CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private static Seed Row(string title, int order, WorkStatus status = WorkStatus.Open, WorkKind kind = WorkKind.Bug, ProjectId? project = null, DateTimeOffset? createdAt = null, DateTimeOffset? dueAt = null)
        => new(Guid.NewGuid(), project ?? ProjectId.From(Guid.NewGuid()), title, status, kind, createdAt ?? Created, dueAt, order);

    private static IEnumerable<string> Titles(IEnumerable<TaskLine> rows) => rows.Select(r => r.Title).Order(StringComparer.Ordinal);

    [Fact]
    public async Task Equality_range_and_null_checks()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        await SeedAsync(
            fx,
            Row("one", 1),
            Row("two", 2, dueAt: Created.AddDays(1)),
            Row("three", 3),
            Row("four", 4, dueAt: Created.AddDays(2))).ConfigureAwait(false);
        var repo = new LinqWhereRepo(fx.Connection);

        Titles(await repo.RangeAsync(2, 4, CancellationToken.None).ConfigureAwait(false)).Should().Equal("three", "two");
        Titles(await repo.ExactAsync(2, "one", CancellationToken.None).ConfigureAwait(false)).Should().Equal("four", "three", "two");
        Titles(await repo.WithoutDueAsync(CancellationToken.None).ConfigureAwait(false)).Should().Equal("one", "three");
        Titles(await repo.WithDueAsync(CancellationToken.None).ConfigureAwait(false)).Should().Equal("four", "two");
    }

    [Fact]
    public async Task Typed_id_and_enum_int_and_string_filters()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var project = ProjectId.From(Guid.NewGuid());
        await SeedAsync(
            fx,
            Row("match", 1, WorkStatus.Done, WorkKind.Feature, project),
            Row("other project", 2, WorkStatus.Done, WorkKind.Feature),
            Row("open", 3, WorkStatus.Open, WorkKind.Feature, project),
            Row("bug", 4, WorkStatus.Done, WorkKind.Bug, project)).ConfigureAwait(false);
        var repo = new LinqWhereRepo(fx.Connection);

        Titles(await repo.TypedAsync(project, WorkStatus.Done, WorkKind.Feature, CancellationToken.None).ConfigureAwait(false))
            .Should().Equal("match");
        Titles(await repo.TypedAsync(project, WorkStatus.Done, WorkKind.Bug, CancellationToken.None).ConfigureAwait(false))
            .Should().Equal("bug");
        Titles(await repo.FeaturesAsync(CancellationToken.None).ConfigureAwait(false)).Should().Equal("open");
        Titles(await repo.ProjectValueAsync(project.Value, CancellationToken.None).ConfigureAwait(false))
            .Should().Equal("bug", "match", "open");
    }

    [Fact]
    public async Task Contains_on_guid_array_parameter()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var a = Row("a", 1);
        var b = Row("b", 2);
        var c = Row("c", 3);
        await SeedAsync(fx, a, b, c).ConfigureAwait(false);
        var repo = new LinqWhereRepo(fx.Connection);

        Titles(await repo.AmongAsync([a.Id, c.Id, Guid.NewGuid()], CancellationToken.None).ConfigureAwait(false)).Should().Equal("a", "c");
        (await repo.AmongAsync([], CancellationToken.None).ConfigureAwait(false)).Should().BeEmpty();
    }

    [Fact]
    public async Task Contains_escapes_like_wildcards()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        await SeedAsync(fx, Row("a%b", 1), Row("a_b", 2), Row("axb", 3), Row("a\\b", 4)).ConfigureAwait(false);
        var repo = new LinqWhereRepo(fx.Connection);

        Titles(await repo.ContainingAsync("%", CancellationToken.None).ConfigureAwait(false)).Should().Equal("a%b");
        Titles(await repo.ContainingAsync("_", CancellationToken.None).ConfigureAwait(false)).Should().Equal("a_b");
        Titles(await repo.ContainingAsync("\\", CancellationToken.None).ConfigureAwait(false)).Should().Equal("a\\b");
        Titles(await repo.ContainingAsync("x", CancellationToken.None).ConfigureAwait(false)).Should().Equal("axb");
        Titles(await repo.StartingAsync("a%", CancellationToken.None).ConfigureAwait(false)).Should().Equal("a%b");
        Titles(await repo.StartingAsync("a", CancellationToken.None).ConfigureAwait(false)).Should().HaveCount(4);
        Titles(await repo.EndingAsync("_b", CancellationToken.None).ConfigureAwait(false)).Should().Equal("a_b");
        Titles(await repo.EndingAsync("\\b", CancellationToken.None).ConfigureAwait(false)).Should().Equal("a\\b");
    }

    [Fact]
    public async Task ILike_is_case_insensitive()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        await SeedAsync(fx, Row("Write The Plan", 1), Row("review", 2)).ConfigureAwait(false);
        var repo = new LinqWhereRepo(fx.Connection);

        Titles(await repo.LikeAsync("%the plan%", CancellationToken.None).ConfigureAwait(false)).Should().Equal("Write The Plan");
        Titles(await repo.LikeAsync("REV_EW", CancellationToken.None).ConfigureAwait(false)).Should().Equal("review");
    }

    [Fact]
    public async Task Null_optional_filters_return_all_rows()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var project = ProjectId.From(Guid.NewGuid());
        var first = Row("first", 1, WorkStatus.Open, WorkKind.Bug, project, Created);
        var second = Row("second", 2, WorkStatus.Done, WorkKind.Feature, createdAt: Created.AddDays(3));
        await SeedAsync(fx, first, second).ConfigureAwait(false);
        var repo = new LinqWhereRepo(fx.Connection);
        var before = Created.AddDays(1);

        foreach (var query in new Func<int?, WorkStatus?, Guid?, DateTimeOffset?, ProjectId?, WorkKind?, Task<List<TaskLine>>>[]
        {
            (o, s, i, c, p, k) => repo.OptionalAsync(o, s, i, c, p, k, CancellationToken.None),
            (o, s, i, c, p, k) => repo.OptionalOrAsync(o, s, i, c, p, k, CancellationToken.None),
        })
        {
            Titles(await query(null, null, null, null, null, null).ConfigureAwait(false)).Should().Equal("first", "second");
            Titles(await query(2, null, null, null, null, null).ConfigureAwait(false)).Should().Equal("second");
            Titles(await query(null, WorkStatus.Open, null, null, null, null).ConfigureAwait(false)).Should().Equal("first");
            Titles(await query(null, null, second.Id, null, null, null).ConfigureAwait(false)).Should().Equal("second");
            Titles(await query(null, null, null, before, null, null).ConfigureAwait(false)).Should().Equal("first");
            Titles(await query(null, null, null, null, project, null).ConfigureAwait(false)).Should().Equal("first");
            Titles(await query(null, null, null, null, null, WorkKind.Feature).ConfigureAwait(false)).Should().Equal("second");
            (await query(1, WorkStatus.Done, null, null, null, null).ConfigureAwait(false)).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Enum_constant_against_a_name_bound_parameter()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        await SeedAsync(fx, Row("bug", 1, kind: WorkKind.Bug), Row("feature", 2, kind: WorkKind.Feature)).ConfigureAwait(false);
        var repo = new LinqWhereRepo(fx.Connection);

        Titles(await repo.KindUnlessBugAsync(WorkKind.Bug, CancellationToken.None).ConfigureAwait(false)).Should().Equal("bug", "feature");
        Titles(await repo.KindUnlessBugAsync(WorkKind.Feature, CancellationToken.None).ConfigureAwait(false)).Should().Equal("feature");
    }

    // Guid[] and DateTime bind without a DbType facet; the cast in the null test types a null.
    [Fact]
    public async Task Null_array_and_DateTime_filters_without_a_facet()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var early = Row("early", 1);
        var late = Row("late", 2);
        var never = Row("never", 3);
        await SeedAsync(fx, early, late, never).ConfigureAwait(false);
        var repo = new LinqWhereRepo(fx.Connection);
        var start = new DateTime(2026, 10, 21, 8, 0, 0, DateTimeKind.Utc);
        await repo.StartAsync(early.Id, start, CancellationToken.None).ConfigureAwait(false);
        await repo.StartAsync(late.Id, start.AddDays(2), CancellationToken.None).ConfigureAwait(false);

        foreach (var among in new Func<Guid[]?, Task<List<TaskLine>>>[]
        {
            ids => repo.AmongOptionalAsync(ids, CancellationToken.None),
            ids => repo.AmongOptionalOrAsync(ids, CancellationToken.None),
        })
        {
            Titles(await among(null).ConfigureAwait(false)).Should().Equal("early", "late", "never");
            Titles(await among([late.Id]).ConfigureAwait(false)).Should().Equal("late");
        }

        foreach (var startedBefore in new Func<DateTime?, Task<List<TaskLine>>>[]
        {
            at => repo.StartedBeforeAsync(at, CancellationToken.None),
            at => repo.StartedBeforeOrAsync(at, CancellationToken.None),
        })
        {
            Titles(await startedBefore(null).ConfigureAwait(false)).Should().Equal("early", "late", "never");
            Titles(await startedBefore(start.AddDays(1)).ConfigureAwait(false)).Should().Equal("early");
            // Unspecified goes down as timestamp: the cast only feeds the null test, and the
            // comparison converts as PostgreSQL always does, in the session time zone, UTC here.
            Titles(await startedBefore(DateTime.SpecifyKind(start.AddDays(1), DateTimeKind.Unspecified)).ConfigureAwait(false))
                .Should().Equal("early");
        }
    }

    // A float[] has no DbType facet either: its null test is cast to real[].
    [Fact]
    public async Task Null_float_array_filter_casts_to_real()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var half = Row("half", 1);
        var tenth = Row("tenth", 2);
        await SeedAsync(fx, half, tenth, Row("zero", 3)).ConfigureAwait(false);
        var repo = new LinqWhereRepo(fx.Connection);
        await repo.ScoreAsync(half.Id, 0.5f, CancellationToken.None).ConfigureAwait(false);
        await repo.ScoreAsync(tenth.Id, 0.1f, CancellationToken.None).ConfigureAwait(false);

        Titles(await repo.AmongScoresAsync(null, CancellationToken.None).ConfigureAwait(false)).Should().Equal("half", "tenth", "zero");
        Titles(await repo.AmongScoresAsync([0.1f, 7f], CancellationToken.None).ConfigureAwait(false)).Should().Equal("tenth");
        Titles(await repo.AmongScoresAsync([], CancellationToken.None).ConfigureAwait(false)).Should().BeEmpty();
    }

    [Fact]
    public async Task Parameter_reused_in_condition_and_predicate()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        await SeedAsync(fx, Row("alpha", 1), Row("beta", 2), Row("alphabet", 3)).ConfigureAwait(false);
        var repo = new LinqWhereRepo(fx.Connection);

        Titles(await repo.ReusedAsync(null, null, CancellationToken.None).ConfigureAwait(false)).Should().Equal("alpha", "alphabet", "beta");
        Titles(await repo.ReusedAsync(2, null, CancellationToken.None).ConfigureAwait(false)).Should().Equal("alphabet", "beta");
        Titles(await repo.ReusedAsync(null, "alpha", CancellationToken.None).ConfigureAwait(false)).Should().Equal("alpha", "alphabet");
        Titles(await repo.ReusedAsync(2, "alpha", CancellationToken.None).ConfigureAwait(false)).Should().Equal("alphabet");
    }

    // Comparisons follow C#: a null compares equal to null only, and `null > p` is false, so
    // its negation is true. A WhereIf whose condition is null skips its filter.
    [Fact]
    public async Task Null_comparisons_follow_CSharp()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var due = Created.AddDays(1);
        var none = Row("none", 1);
        var low = Row("low", 2, dueAt: due);
        var high = Row("high", 3, dueAt: Created.AddDays(2));
        await SeedAsync(fx, none, low, high).ConfigureAwait(false);
        var repo = new LinqWhereRepo(fx.Connection);
        await repo.GradeAsync(low.Id, 1, 0.1f, WorkKind.Bug, CancellationToken.None).ConfigureAwait(false);
        await repo.GradeAsync(high.Id, 5, 0.2f, WorkKind.Feature, CancellationToken.None).ConfigureAwait(false);

        // Every case is checked, so a failure names each comparison that went wrong.
        using var scope = new AssertionScope();
        Titles(await repo.MinOrderIfAsync(null, CancellationToken.None).ConfigureAwait(false)).Should().Equal("high", "low", "none");
        Titles(await repo.MinOrderIfAsync(0, CancellationToken.None).ConfigureAwait(false)).Should().Equal("high", "low", "none");
        Titles(await repo.MinOrderIfAsync(2, CancellationToken.None).ConfigureAwait(false)).Should().Equal("high", "low");

        Titles(await repo.DueAsync(null, CancellationToken.None).ConfigureAwait(false)).Should().Equal("none");
        Titles(await repo.DueAsync(due, CancellationToken.None).ConfigureAwait(false)).Should().Equal("low");
        Titles(await repo.NotDueAsync(null, CancellationToken.None).ConfigureAwait(false)).Should().Equal("high", "low");
        Titles(await repo.NotDueAsync(due, CancellationToken.None).ConfigureAwait(false)).Should().Equal("high", "none");

        Titles(await repo.NotFeatureAsync(CancellationToken.None).ConfigureAwait(false)).Should().Equal("low", "none");
        Titles(await repo.OtherCategoryAsync(WorkKind.Feature, CancellationToken.None).ConfigureAwait(false)).Should().Equal("low", "none");
        Titles(await repo.OtherCategoryAsync(null, CancellationToken.None).ConfigureAwait(false)).Should().Equal("high", "low");
        Titles(await repo.OtherPriorityAsync(5, CancellationToken.None).ConfigureAwait(false)).Should().Equal("low", "none");
        Titles(await repo.NotAboveAsync(3, CancellationToken.None).ConfigureAwait(false)).Should().Equal("low", "none");

        Titles(await repo.RatedAsync(CancellationToken.None).ConfigureAwait(false)).Should().Equal("low");
    }
}
