using System.Linq;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Model;

public class SqlPlaceholderScannerTests
{
    [Fact]
    public void Scan_FindsTableAndColumn()
    {
        const string sql = "SELECT {col:Id} FROM {table}";

        var scan = SqlPlaceholderScanner.Scan(sql);

        Assert.Equal(2, scan.Spans.Length);
        Assert.Equal(SqlPlaceholderKind.Column, scan.Spans[0].Kind);
        Assert.Equal("Id", scan.Spans[0].Name);
        Assert.Equal("{col:Id}", sql.Substring(scan.Spans[0].Start, scan.Spans[0].Length));
        Assert.Equal(SqlPlaceholderKind.Member, scan.Spans[1].Kind);
        Assert.Equal("table", scan.Spans[1].Name);
        Assert.Equal("{table}", sql.Substring(scan.Spans[1].Start, scan.Spans[1].Length));
    }

    [Fact]
    public void Scan_MaskedSql_HasSameLengthAndNoBraces()
    {
        const string sql = "SELECT {col:Id}, {col:X} FROM {table} WHERE {col:Id} = @id";

        var scan = SqlPlaceholderScanner.Scan(sql);

        Assert.Equal(sql.Length, scan.MaskedSql.Length);
        Assert.DoesNotContain('{', scan.MaskedSql);
        foreach (var span in scan.Spans)
        {
            var masked = scan.MaskedSql.Substring(span.Start, span.Length);
            Assert.True(char.IsLetter(masked[0]) || masked[0] == '_');
            Assert.All(masked, c => Assert.True(char.IsLetterOrDigit(c) || c == '_'));
        }

        for (var i = 0; i < sql.Length; i++)
        {
            if (!scan.Spans.Any(sp => i >= sp.Start && i < sp.Start + sp.Length))
            {
                Assert.Equal(sql[i], scan.MaskedSql[i]);
            }
        }
    }

    [Fact]
    public void Scan_MaskedSql_ShortestSpanStaysAnIdentifier()
    {
        var scan = SqlPlaceholderScanner.Scan("{a}");

        Assert.Single(scan.Spans);
        Assert.Equal(3, scan.MaskedSql.Length);
        Assert.Equal('_', scan.MaskedSql[0]);
    }

    [Theory]
    [InlineData("'{table}'")]
    [InlineData("'it''s {table}'")]
    [InlineData("\"{table}\"")]
    [InlineData("[{table}]")]
    [InlineData("`{table}`")]
    [InlineData("$$ {table} $$")]
    [InlineData("$tag$ {table} $tag$")]
    [InlineData("-- {table}\n")]
    [InlineData("/* {table} /* {table} */ {table} */")]
    public void Scan_IgnoresQuotedAndCommented(string sql)
    {
        var scan = SqlPlaceholderScanner.Scan(sql);

        Assert.Empty(scan.Spans);
        Assert.Equal(sql, scan.MaskedSql);
    }

    [Theory]
    [InlineData("'{1,2}'")]
    [InlineData("{d '2020-01-01'}")]
    [InlineData("{}")]
    [InlineData("{ table }")]
    [InlineData("{col:}")]
    [InlineData("{1abc}")]
    public void Scan_LeavesNonPlaceholderBraces(string sql)
    {
        var scan = SqlPlaceholderScanner.Scan(sql);

        Assert.Empty(scan.Spans);
    }

    [Fact]
    public void Scan_FindsPlaceholderAfterSkippedRegions()
    {
        var scan = SqlPlaceholderScanner.Scan("SELECT '{table}' /* {table} */ FROM {table} -- {table}");

        Assert.Single(scan.Spans);
        Assert.Equal("table", scan.Spans.Single().Name);
        Assert.Equal("SELECT '{table}' /* {table} */ FROM ".Length, scan.Spans[0].Start);
    }

    [Theory]
    [InlineData("'abc {table}")]
    [InlineData("/* {table}")]
    [InlineData("$$ {table}")]
    [InlineData("{table")]
    [InlineData("{col:")]
    public void Scan_UnterminatedRegions_GiveNoSpans(string sql)
    {
        var scan = SqlPlaceholderScanner.Scan(sql);

        Assert.Empty(scan.Spans);
        Assert.Equal(sql, scan.MaskedSql);
    }

    [Fact]
    public void Scan_EmptyString_GivesNoSpans()
    {
        var scan = SqlPlaceholderScanner.Scan("");

        Assert.Empty(scan.Spans);
        Assert.Equal("", scan.MaskedSql);
    }

    [Theory]
    [InlineData("$1 {table}")]
    [InlineData("a$b$ {table}")]
    public void Scan_DollarThatIsNotAQuote_StillFindsPlaceholder(string sql)
    {
        var scan = SqlPlaceholderScanner.Scan(sql);

        Assert.Single(scan.Spans);
        Assert.Equal("table", scan.Spans[0].Name);
    }
}
