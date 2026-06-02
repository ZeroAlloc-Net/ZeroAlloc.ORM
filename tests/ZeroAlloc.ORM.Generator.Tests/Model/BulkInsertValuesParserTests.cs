using Xunit;
using ZeroAlloc.ORM.Generator.Model;

namespace ZeroAlloc.ORM.Generator.Tests.Model;

public class BulkInsertValuesParserTests
{
    private static readonly string[] ExpectedCustomerIdTotal = ["CustomerId", "Total"];
    private static readonly string[] ExpectedCustIdTotal = ["CustId", "Total"];

    [Fact]
    public void Parses_simple_two_column_VALUES_tuple()
    {
        var result = BulkInsertValuesParser.TryParse(
            "INSERT INTO Orders (CustomerId, Total) VALUES (@CustomerId, @Total)");

        Assert.True(result.Success);
        Assert.Equal(ExpectedCustomerIdTotal, result.Placeholders);
    }

    [Fact]
    public void Parses_VALUES_with_RETURNING_suffix()
    {
        var result = BulkInsertValuesParser.TryParse(
            "INSERT INTO Orders (CustomerId, Total) VALUES (@CustomerId, @Total) RETURNING Id");

        Assert.True(result.Success);
        Assert.Equal(ExpectedCustomerIdTotal, result.Placeholders);
    }

    [Fact]
    public void Parses_VALUES_case_insensitively()
    {
        var result = BulkInsertValuesParser.TryParse(
            "insert into orders (customer_id, total) values (@CustId, @Total)");

        Assert.True(result.Success);
        Assert.Equal(ExpectedCustIdTotal, result.Placeholders);
    }

    [Fact]
    public void Rejects_zero_VALUES_tuples()
    {
        var result = BulkInsertValuesParser.TryParse(
            "INSERT INTO Orders (CustomerId, Total) SELECT 1, 2");

        Assert.False(result.Success);
        Assert.Equal(0, result.TupleCount);
        Assert.Equal(BulkInsertParseFailReason.NoValuesClause, result.FailReason);
    }

    [Fact]
    public void Rejects_multiple_VALUES_tuples()
    {
        var result = BulkInsertValuesParser.TryParse(
            "INSERT INTO Orders (CustomerId, Total) VALUES (1, 2), (3, 4)");

        Assert.False(result.Success);
        Assert.Equal(2, result.TupleCount);
        Assert.Equal(BulkInsertParseFailReason.MultipleRowTuples, result.FailReason);
    }

    [Fact]
    public void Returns_full_VALUES_clause_range_for_emit_rewriting()
    {
        var sql = "INSERT INTO Orders (CustomerId, Total) VALUES (@CustomerId, @Total)";
        var result = BulkInsertValuesParser.TryParse(sql);

        Assert.True(result.Success);
        Assert.Equal("(@CustomerId, @Total)", sql.Substring(result.TupleStart, result.TupleLength));
    }

    [Fact]
    public void Rejects_VALUES_with_literal_tuple_no_placeholders()
    {
        // VALUES is present and well-formed, but contains literal values
        // instead of @-placeholders. Classifier should fire ZAO072
        // (no placeholders to match against TRow properties), so the parser
        // returns Success=false with TupleCount=1 to signal "VALUES seen but
        // shape is wrong".
        var result = BulkInsertValuesParser.TryParse(
            "INSERT INTO Orders (CustomerId, Total) VALUES (1, 2)");

        Assert.False(result.Success);
        Assert.Equal(1, result.TupleCount);
        Assert.Equal(BulkInsertParseFailReason.MalformedTuple, result.FailReason);
    }
}
