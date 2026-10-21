using System.Reflection;
using AwesomeAssertions;
using Xunit;
using ZeroAlloc.ORM.Linq;

namespace ZeroAlloc.ORM.Linq.Tests;

public sealed class AuthoringSurfaceTests
{
    private const string Guidance = "read at build time";
    private const string Call = "Call the [LinqQuery] method";

    [TableMap(typeof(Order), "orders", Schema = "sales")]
    [ColumnMap("Id", Column = "order_id")]
    [ColumnMap("Status", EnumAsString = true)]
    private sealed class OrderMap;

    private sealed class Order;

    [Fact]
    public void Table_throws_with_guidance()
    {
        var act = () => Sql.Table<object>();

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{Guidance}*{Call}*");
    }

    [Fact]
    public void Operators_throw()
    {
        IQuery<int> query = null!;

        ((Action)(() => query.Where(x => x > 1))).Should().Throw<InvalidOperationException>().WithMessage($"*{Guidance}*{Call}*");
        ((Action)(() => query.Select(x => x + 1))).Should().Throw<InvalidOperationException>().WithMessage($"*{Guidance}*{Call}*");
        ((Action)(() => query.Count())).Should().Throw<InvalidOperationException>().WithMessage($"*{Guidance}*{Call}*");
        ((Action)(() => _ = Sql.Row(1, 2) > Sql.Row(1, 2))).Should().Throw<InvalidOperationException>().WithMessage($"*{Guidance}*{Call}*");
    }

    [Fact]
    public void Map_attributes_expose_values()
    {
        var table = typeof(OrderMap).GetCustomAttribute<TableMapAttribute>();
        table.Should().NotBeNull();
        table!.Entity.Should().Be<Order>();
        table.Table.Should().Be("orders");
        table.Schema.Should().Be("sales");

        var columns = typeof(OrderMap).GetCustomAttributes<ColumnMapAttribute>().OrderBy(c => c.Property, StringComparer.Ordinal).ToArray();
        columns.Should().HaveCount(2);
        columns[0].Property.Should().Be("Id");
        columns[0].Column.Should().Be("order_id");
        columns[0].EnumAsString.Should().BeFalse();
        columns[1].Property.Should().Be("Status");
        columns[1].Column.Should().BeNull();
        columns[1].EnumAsString.Should().BeTrue();
    }
}
