#nullable enable
using AwesomeAssertions;
using Xunit;
using ZeroAlloc.ORM.Linq;

namespace ZeroAlloc.ORM.Linq.Tests;

public sealed class AggregationSurfaceTests
{
    private const string Guidance = "read at build time";
    private const string Call = "Call the [LinqQuery] method";

    private sealed class Event
    {
        public int InputTokens { get; set; }
        public int? OutputTokens { get; set; }
        public decimal Cost { get; set; }
        public string Model { get; set; } = "";
    }

    private static void Throws(Action act) =>
        act.Should().Throw<InvalidOperationException>().WithMessage($"*{Guidance}*{Call}*");

    [Fact]
    public void Group_and_join_operators_throw()
    {
        IQuery<Event> events = null!;
        IGroup<string, Event> group = null!;

        Throws(() => events.GroupBy(e => e.Model));
        Throws(() => events.Join(events, o => o.Model, i => i.Model, (o, i) => o.InputTokens + i.InputTokens));
        Throws(() => group.Sum(e => e.InputTokens));
        Throws(() => group.Count());
        Throws(() => group.LongCount());
    }

    [Fact]
    public void Scalar_aggregates_throw()
    {
        IQuery<Event> table = null!;

        Throws(() => table.Sum(e => e.InputTokens));
        Throws(() => table.Min(e => e.Cost));
        Throws(() => table.Max(e => e.Model));
        Throws(() => table.Average(e => e.InputTokens));
    }

    [Fact]
    public void Group_aggregates_throw_for_every_overload()
    {
        IGroup<string, Event> g = null!;

        Throws(() => g.Sum(e => (long)e.InputTokens));
        Throws(() => g.Sum(e => e.Cost));
        Throws(() => g.Sum(e => (double)e.Cost));
        Throws(() => g.Sum(e => e.OutputTokens));
        Throws(() => g.Sum(e => (long?)e.OutputTokens));
        Throws(() => g.Sum(e => (decimal?)e.Cost));
        Throws(() => g.Sum(e => (double?)e.OutputTokens));
        Throws(() => g.Min(e => e.OutputTokens));
        Throws(() => g.Max(e => e.Model));
        Throws(() => g.Average(e => e.InputTokens));
        Throws(() => g.Average(e => (long)e.InputTokens));
        Throws(() => g.Average(e => e.Cost));
        Throws(() => g.Average(e => e.OutputTokens));
        Throws(() => g.Average(e => (decimal?)e.Cost));
    }

    [Fact]
    public void Scalar_overloads_bind_to_the_intended_type()
    {
        IQuery<Event> q = null!;

        Func<IQuery<long>> sumLong = () => q.Sum(e => (long)e.InputTokens);
        Func<IQuery<double?>> avgNullable = () => q.Average(e => (double?)e.OutputTokens);
        Func<IQuery<double>> avgInt = () => q.Average(e => e.InputTokens);
        Func<IQuery<decimal?>> avgDecimal = () => q.Average(e => (decimal?)e.Cost);
        Func<IQuery<int?>> sumNullable = () => q.Sum(e => e.OutputTokens);
        Func<IQuery<string>> max = () => q.Max(e => e.Model);

        Throws(() => sumLong());
        Throws(() => avgNullable());
        Throws(() => avgInt());
        Throws(() => avgDecimal());
        Throws(() => sumNullable());
        Throws(() => max());
    }

    [Fact]
    public void Group_overloads_bind_to_the_intended_type()
    {
        IGroup<string, Event> g = null!;

        Func<long> sumLong = () => g.Sum(e => (long)e.InputTokens);
        Func<double?> avgNullable = () => g.Average(e => (double?)e.OutputTokens);
        Func<double> avgInt = () => g.Average(e => e.InputTokens);

        Throws(() => sumLong());
        Throws(() => avgNullable());
        Throws(() => avgInt());
    }
}
