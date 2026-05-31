using FluentAssertions;
using Xunit;

namespace ZeroAlloc.TypeConversions.Tests;

public class SqlStatementSplitterTests
{
    [Fact]
    public void Single_statement_counts_one_and_splits_one()
    {
        const string sql = "SELECT 1";

        SqlStatementSplitter.CountStatements(sql).Should().Be(1);
        SqlStatementSplitter.Split(sql).Should().Equal("SELECT 1");
    }

    [Fact]
    public void Two_statements_count_two_and_split_two()
    {
        const string sql = "SELECT 1; SELECT 2";

        SqlStatementSplitter.CountStatements(sql).Should().Be(2);
        SqlStatementSplitter.Split(sql).Should().Equal("SELECT 1", " SELECT 2");
    }

    [Fact]
    public void Trailing_semicolon_does_not_count_an_empty_statement()
    {
        const string sql = "SELECT 1;";

        SqlStatementSplitter.CountStatements(sql).Should().Be(1);
        SqlStatementSplitter.Split(sql).Should().Equal("SELECT 1");
    }

    [Fact]
    public void Trailing_semicolon_with_whitespace_does_not_count_an_empty_statement()
    {
        const string sql = "SELECT 1;   \n  ";

        SqlStatementSplitter.CountStatements(sql).Should().Be(1);
        SqlStatementSplitter.Split(sql).Should().Equal("SELECT 1");
    }

    [Fact]
    public void Semicolon_inside_single_quoted_literal_is_not_a_separator()
    {
        const string sql = "SELECT 'a;b' AS x";

        SqlStatementSplitter.CountStatements(sql).Should().Be(1);
        SqlStatementSplitter.Split(sql).Should().Equal("SELECT 'a;b' AS x");
    }

    [Fact]
    public void Doubled_single_quote_inside_literal_does_not_end_the_literal()
    {
        // 'a''b;c' -- the '' is an escaped single-quote inside the literal, so
        // the ; is still inside the literal and not a statement boundary.
        const string sql = "SELECT 'a''b;c' AS x";

        SqlStatementSplitter.CountStatements(sql).Should().Be(1);
        SqlStatementSplitter.Split(sql).Should().Equal("SELECT 'a''b;c' AS x");
    }

    [Fact]
    public void Semicolon_inside_double_quoted_identifier_is_not_a_separator()
    {
        const string sql = "SELECT \"col;name\" FROM t";

        SqlStatementSplitter.CountStatements(sql).Should().Be(1);
        SqlStatementSplitter.Split(sql).Should().Equal("SELECT \"col;name\" FROM t");
    }

    [Fact]
    public void Doubled_double_quote_does_not_end_the_quoted_identifier()
    {
        const string sql = "SELECT \"a\"\"b;c\" FROM t";

        SqlStatementSplitter.CountStatements(sql).Should().Be(1);
        SqlStatementSplitter.Split(sql).Should().Equal("SELECT \"a\"\"b;c\" FROM t");
    }

    [Fact]
    public void Empty_string_yields_zero_count_and_empty_split()
    {
        SqlStatementSplitter.CountStatements(string.Empty).Should().Be(0);
        SqlStatementSplitter.Split(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void Three_statements_with_mixed_literals_split_correctly()
    {
        const string sql = "INSERT INTO t VALUES ('x;y'); UPDATE t SET v = 1; SELECT 1";

        SqlStatementSplitter.CountStatements(sql).Should().Be(3);
        SqlStatementSplitter.Split(sql).Should().Equal(
            "INSERT INTO t VALUES ('x;y')",
            " UPDATE t SET v = 1",
            " SELECT 1");
    }
}
