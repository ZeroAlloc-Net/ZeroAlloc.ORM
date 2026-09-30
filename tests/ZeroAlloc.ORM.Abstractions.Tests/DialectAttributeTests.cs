using AwesomeAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Abstractions.Tests;

// #248
public class DialectAttributeTests
{
    [Fact]
    public void Targets_the_assembly_and_repository_types_once_without_inheritance()
    {
        var usage = typeof(DialectAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        usage.ValidOn.Should().Be(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Struct);
        usage.AllowMultiple.Should().BeFalse();
        usage.Inherited.Should().BeFalse();
    }

    [Theory]
    [InlineData(SqlDialect.SqlServer)]
    [InlineData(SqlDialect.PostgreSql)]
    [InlineData(SqlDialect.Sqlite)]
    [InlineData(SqlDialect.MySql)]
    public void Ctor_stores_the_dialect(SqlDialect dialect)
        => new DialectAttribute(dialect).Dialect.Should().Be(dialect);

    // The generator reads the declared dialect as a number.
    [Fact]
    public void Dialect_values_are_fixed()
    {
        ((int)SqlDialect.SqlServer).Should().Be(0);
        ((int)SqlDialect.PostgreSql).Should().Be(1);
        ((int)SqlDialect.Sqlite).Should().Be(2);
        ((int)SqlDialect.MySql).Should().Be(3);
    }
}
