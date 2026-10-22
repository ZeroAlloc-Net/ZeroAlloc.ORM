using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Xunit;
using ZeroAlloc.ORM.Generator.Linq;
using ZeroAlloc.ORM.Generator.Model;

namespace ZeroAlloc.ORM.Generator.Tests.Linq;

// Every walk over the SQL tree switches on the concrete record and throws on one it does not
// know, so a missed case would crash the whole generator. This test builds one instance of
// every concrete SqlExpression and SqlStatement record found by reflection and runs each walk
// over it, so a record added without its cases fails here instead of in a user's build.
public class SqlAstExhaustivenessTests
{
    private const string Entity = "global::TestApp.TaskEntity";

    private static readonly EntityMapModel Map = new(
        Entity,
        "Tasks",
        Schema: null,
        new EquatableArray<ColumnMapModel>(ImmutableArray.Create(new ColumnMapModel("Order", "Order", EnumAsString: false))));

    private static readonly SqlMemberExpression Column = new("Order", Location: null, ColumnPicksEnumStorage: false);

    private static readonly SqlTableSource From = new(Entity, Location: null);

    public static TheoryData<string> Expressions() => Names(typeof(SqlExpression));

    public static TheoryData<string> Statements() => Names(typeof(SqlStatement));

    [Theory]
    [MemberData(nameof(Expressions))]
    public void Every_walk_handles_the_expression(string name)
    {
        var expression = (SqlExpression)Create(TypeNamed(name));
        var select = Select(expression);

        Walk(select);
        Walk(new SqlAggregateStatement(SqlAggregate.Count, select));
        Walk(new SqlScalarAggregateStatement(Call(expression), select));
        Walk(new SqlDeleteStatement(From, new EquatableArray<SqlExpression>(ImmutableArray.Create(Unaliased(expression)))));
        Walk(Update(Unaliased(expression)));
        _ = LinqQueryReader.MayBeNull(expression, new HashSet<SqlExpression>());
    }

    [Theory]
    [MemberData(nameof(Statements))]
    public void Every_walk_handles_the_statement(string name)
        => Walk((SqlStatement)Create(TypeNamed(name)));

    // Guards the reflection itself: the records the walks are known to handle are all found.
    [Fact]
    public void Reflection_finds_the_records()
    {
        var expressions = Concrete(typeof(SqlExpression));
        Assert.Contains(typeof(SqlIsNotTrueExpression), expressions);
        Assert.Contains(typeof(SqlCastExpression), expressions);
        Assert.Contains(typeof(SqlConditionalExpression), expressions);
        Assert.True(expressions.Count >= 15, "found " + expressions.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Contains(typeof(SqlAggregateCallExpression), expressions);
        Assert.Contains(typeof(SqlDateTruncExpression), expressions);
        Assert.Contains(typeof(SqlObjectIsNullExpression), expressions);
        Assert.Contains(typeof(SqlInSubqueryExpression), expressions);
        Assert.Contains(typeof(SqlArithmeticExpression), expressions);
        Assert.Equal(
            [
                typeof(SqlAggregateStatement), typeof(SqlDeleteStatement), typeof(SqlScalarAggregateStatement),
                typeof(SqlSelectStatement), typeof(SqlUpdateStatement),
            ],
            Concrete(typeof(SqlStatement)).OrderBy(t => t.Name, StringComparer.Ordinal));
    }

    private static void Walk(SqlStatement statement)
    {
        var select = statement switch
        {
            SqlAggregateStatement aggregate => aggregate.Source,
            SqlScalarAggregateStatement scalar => scalar.Source,
            SqlDeleteStatement or SqlUpdateStatement => null,
            _ => (SqlSelectStatement)statement,
        };
        var maps = new EquatableArray<EntityMapModel>(ImmutableArray.Create(Map));
        var names = new SqlNameScope(Map, "TestApp.TaskEntity", select?.Alias, maps);
        foreach (var join in select?.Joins ?? default)
            names.AddSource(join.Alias, Map, "TestApp.TaskEntity");
        _ = EnumParameterStorage.Decide(statement, names);
        _ = PostgresSqlRenderer.Instance.Render(statement, names);
        _ = SqlParameterReferences.Collect(statement);
        _ = SqlSubqueries.Collect(statement);
        _ = LinqQueryBinder.Bind(statement, maps);
    }

    // The expression in every position a statement has: the projection, a join's ON and its
    // source's conditions, the WHERE, the GROUP BY, the ORDER BY and the paging. The FROM's alias
    // is "x", the placeholder a member's source alias gets.
    private static SqlSelectStatement Select(SqlExpression expression)
        => new(
            From,
            new EquatableArray<SqlExpression>(ImmutableArray.Create(expression)),
            new EquatableArray<SqlExpression>(ImmutableArray.Create(expression)),
            new EquatableArray<SqlOrdering>(ImmutableArray.Create(new SqlOrdering(expression, Descending: false))),
            new SqlPaging(expression, expression),
            new EquatableArray<SqlExpression>(ImmutableArray.Create(expression)),
            Joins(expression),
            Alias: "x");

    private static EquatableArray<SqlJoin> Joins(SqlExpression expression)
        => new(ImmutableArray.Create(new SqlJoin(
            From,
            "t1",
            expression,
            new EquatableArray<SqlExpression>(ImmutableArray.Create(expression)))));

    // The expression as an UPDATE's setter value and its WHERE, the positions an UPDATE adds.
    private static SqlUpdateStatement Update(SqlExpression expression)
        => new(
            From,
            new EquatableArray<SqlSetter>(ImmutableArray.Create(new SqlSetter(Column, expression))),
            new EquatableArray<SqlExpression>(ImmutableArray.Create(expression)));

    // A DELETE's or an UPDATE's table takes no alias, so its members name none: the placeholder "x" goes.
    private static SqlExpression Unaliased(SqlExpression expression) => expression switch
    {
        SqlMemberExpression member => member with { SourceAlias = null },
        SqlObjectIsNullExpression test => test with { SourceAlias = null },
        _ => expression,
    };

    // The expression as an aggregate's argument, the position a scalar aggregate adds.
    private static SqlAggregateCallExpression Call(SqlExpression argument)
        => new(SqlAggregateFunction.Sum, argument, ResultCast: "int", CoalesceToZero: true);

    private static TheoryData<string> Names(Type root)
    {
        var data = new TheoryData<string>();
        foreach (var type in Concrete(root)) data.Add(type.Name);
        return data;
    }

    private static List<Type> Concrete(Type root)
        => root.Assembly.GetTypes()
            .Where(t => !t.IsAbstract && root.IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

    private static Type TypeNamed(string name)
        => typeof(SqlExpression).Assembly.GetTypes().Single(t => string.Equals(t.Name, name, StringComparison.Ordinal)
            && string.Equals(t.Namespace, typeof(SqlExpression).Namespace, StringComparison.Ordinal));

    // An instance of a record through its widest constructor, every argument a placeholder.
    private static object Create(Type type)
    {
        var constructor = type.GetConstructors()
            .Where(c => c.GetParameters().All(p => p.ParameterType != type))
            .OrderByDescending(c => c.GetParameters().Length)
            .First();
        return constructor.Invoke(constructor.GetParameters().Select(Argument).ToArray());
    }

    private static object? Argument(System.Reflection.ParameterInfo parameter)
    {
        var type = parameter.ParameterType;
        if (type == typeof(string)) return "x";
        if (type == typeof(bool)) return false;
        if (type.IsEnum) return Enum.GetValues(type).GetValue(0);
        if (type == typeof(LocationInfo)) return null;
        if (type == typeof(SqlWholeObject)) return null;
        if (type == typeof(SqlExpression)) return Column;
        if (type == typeof(EquatableArray<SqlExpression>)) return new EquatableArray<SqlExpression>(ImmutableArray.Create<SqlExpression>(Column));
        if (type == typeof(EquatableArray<SqlOrdering>)) return new EquatableArray<SqlOrdering>(ImmutableArray.Create(new SqlOrdering(Column, Descending: false)));
        if (type == typeof(SqlTableSource)) return From;
        if (type == typeof(EquatableArray<SqlJoin>)) return Joins(Column);
        if (type == typeof(SqlPaging)) return SqlPaging.None;
        if (type == typeof(SqlSelectStatement)) return Select(Column);
        if (type == typeof(SqlAggregateCallExpression)) return Call(Column);
        if (type == typeof(SqlMemberExpression)) return Column;
        if (type == typeof(EquatableArray<SqlSetter>)) return new EquatableArray<SqlSetter>(ImmutableArray.Create(new SqlSetter(Column, Column)));
        throw new InvalidOperationException(
            "No placeholder for " + type.Name + " in " + parameter.Member.DeclaringType?.Name + "; add one here.");
    }
}
