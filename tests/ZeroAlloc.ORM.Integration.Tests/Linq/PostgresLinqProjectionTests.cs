using AwesomeAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// [LinqQuery] projections against a real PostgreSQL. Each case boots its own container, so
// related checks share a case.
[Trait("Provider", "Postgres")]
public sealed class PostgresLinqProjectionTests
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

    [Fact]
    public async Task Projection_round_trips_typed_id_enum_array_and_nullable()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqRepo(fx.Connection);
        var project = ProjectId.From(Guid.NewGuid());
        var due = new DateTimeOffset(2026, 11, 1, 12, 30, 0, TimeSpan.Zero);
        var withDue = Guid.NewGuid();
        var withoutDue = Guid.NewGuid();
        await repo.InsertTaskAsync(withDue, project, "a", WorkStatus.Done, "Feature", Created, due, ["x", "y"], 1, CancellationToken.None)
            .ConfigureAwait(false);
        await repo.InsertTaskAsync(withoutDue, project, "b", WorkStatus.Open, "Bug", Created, null, [], 2, CancellationToken.None)
            .ConfigureAwait(false);

        var rows = await repo.ProjectionsAsync(CancellationToken.None).ConfigureAwait(false);

        rows.Should().HaveCount(2);
        var first = rows.Single(r => r.Id == withDue);
        first.ProjectId.Should().Be(project);
        first.Status.Should().Be(WorkStatus.Done);
        first.Kind.Should().Be(WorkKind.Feature);
        first.Tags.Should().Equal("x", "y");
        first.DueAt.Should().Be(due);
        var second = rows.Single(r => r.Id == withoutDue);
        second.Status.Should().Be(WorkStatus.Open);
        second.Kind.Should().Be(WorkKind.Bug);
        second.Tags.Should().BeEmpty();
        second.DueAt.Should().BeNull();

        var single = await repo.FirstProjectionAsync(CancellationToken.None).ConfigureAwait(false);
        single.Should().NotBeNull();
        rows.Should().ContainEquivalentOf(single);

        var streamed = new List<TaskProjection>();
        await foreach (var row in repo.StreamProjectionsAsync(CancellationToken.None).ConfigureAwait(false))
        {
            streamed.Add(row);
        }
        streamed.Select(r => r.Id).Should().BeEquivalentTo([withDue, withoutDue]);
    }

    [Fact]
    public async Task Select_of_one_value_reads_a_list_of_it()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqRepo(fx.Connection);
        var project = ProjectId.From(Guid.NewGuid());
        var due = new DateTimeOffset(2026, 11, 1, 12, 30, 0, TimeSpan.Zero);
        await repo.InsertTaskAsync(Guid.NewGuid(), project, "b", WorkStatus.Done, "Feature", Created, null, [], 2, CancellationToken.None)
            .ConfigureAwait(false);
        await repo.InsertTaskAsync(Guid.NewGuid(), project, "a", WorkStatus.Open, "Bug", Created, due, [], 1, CancellationToken.None)
            .ConfigureAwait(false);
        await repo.InsertTaskAsync(Guid.NewGuid(), project, "c", WorkStatus.Done, "Bug", Created, null, [], 3, CancellationToken.None)
            .ConfigureAwait(false);
        var ct = CancellationToken.None;

        (await repo.TitlesAsync(2, ct).ConfigureAwait(false)).Should().Equal("b", "c");
        (await repo.TitlesAsync(9, ct).ConfigureAwait(false)).Should().BeEmpty();
        (await repo.KindsAsync(ct).ConfigureAwait(false)).Should().Equal(WorkKind.Bug, WorkKind.Feature, WorkKind.Bug);
        (await repo.DueDatesAsync(ct).ConfigureAwait(false)).Should().Equal(due, null, null);
        (await repo.StatusesAsync(ct).ConfigureAwait(false)).Should().Equal(WorkStatus.Open, WorkStatus.Done);
    }

    // #429 — a value-type typed id reads as one column: a LINQ Select of it into a list, and
    // [Query] Task<ProjectId> and Task<ProjectId?>, the nullable one null for no row.
    [Fact]
    public async Task Struct_id_reads_as_one_column()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqRepo(fx.Connection);
        var alpha = ProjectId.From(Guid.NewGuid());
        var beta = ProjectId.From(Guid.NewGuid());
        await repo.InsertTaskAsync(Guid.NewGuid(), beta, "b", WorkStatus.Open, "Bug", Created, null, [], 2, CancellationToken.None)
            .ConfigureAwait(false);
        await repo.InsertTaskAsync(Guid.NewGuid(), alpha, "a", WorkStatus.Open, "Bug", Created, null, [], 1, CancellationToken.None)
            .ConfigureAwait(false);
        var ct = CancellationToken.None;

        (await repo.ProjectIdsAsync(ct).ConfigureAwait(false)).Should().Equal(alpha, beta);
        (await repo.ProjectIdOfAsync(2, ct).ConfigureAwait(false)).Should().Be(beta);
        (await repo.MaybeProjectIdOfAsync(1, ct).ConfigureAwait(false)).Should().Be(alpha);
        (await repo.MaybeProjectIdOfAsync(9, ct).ConfigureAwait(false)).Should().BeNull();
    }

    [Fact]
    public async Task Reserved_and_mixed_case_identifiers_are_quoted()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqRepo(fx.Connection);
        var id = Guid.NewGuid();
        await repo.InsertProjectAsync(id, "Daedalus", 7, CancellationToken.None).ConfigureAwait(false);

        var projects = await repo.ProjectLinesAsync(CancellationToken.None).ConfigureAwait(false);

        projects.Should().ContainSingle().Which.Should().Be(new ProjectLine(id, 7, "Daedalus"));
    }

    [Fact]
    public async Task Named_projection_arguments_follow_ctor_order()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqRepo(fx.Connection);
        var id = Guid.NewGuid();
        await repo.InsertTaskAsync(id, ProjectId.From(Guid.NewGuid()), "write the plan", WorkStatus.Open, "Feature", Created, null, [], 42, CancellationToken.None)
            .ConfigureAwait(false);

        var lines = await repo.LinesAsync(CancellationToken.None).ConfigureAwait(false);

        lines.Should().ContainSingle().Which.Should().Be(new TaskLine(id, "write the plan", 42));
    }

    [Fact]
    public async Task Parameters_and_literals_read_back_into_member_types()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqRepo(fx.Connection);
        var id = Guid.NewGuid();
        await repo.InsertTaskAsync(id, ProjectId.From(Guid.NewGuid()), "t", WorkStatus.Open, "Bug", Created, null, [], 1, CancellationToken.None)
            .ConfigureAwait(false);

        var rows = await repo.LiteralsAsync("from a parameter", CancellationToken.None).ConfigureAwait(false);

        rows.Should().ContainSingle().Which.Should().Be(new LiteralRow(id, "from a parameter", 42L, 0.5, null, Origin.Imported));

        // A projected parameter without a facet is cast, so a null still has a type.
        var at = new DateTime(2026, 10, 21, 9, 30, 0, DateTimeKind.Utc);
        await new LinqWhereRepo(fx.Connection).StartAsync(id, Created.UtcDateTime, CancellationToken.None).ConfigureAwait(false);
        (await repo.StampedAsync(null, CancellationToken.None).ConfigureAwait(false))
            .Should().ContainSingle().Which.Should().Be(new StampRow(id, null));
        (await repo.StampedAsync(at, CancellationToken.None).ConfigureAwait(false))
            .Should().ContainSingle().Which.Should().Be(new StampRow(id, at));
        (await repo.StampedBeforeAsync(null, CancellationToken.None).ConfigureAwait(false))
            .Should().ContainSingle().Which.Should().Be(new StampRow(id, null));
        (await repo.StampedBeforeAsync(at, CancellationToken.None).ConfigureAwait(false))
            .Should().ContainSingle().Which.Should().Be(new StampRow(id, at));
    }
}
