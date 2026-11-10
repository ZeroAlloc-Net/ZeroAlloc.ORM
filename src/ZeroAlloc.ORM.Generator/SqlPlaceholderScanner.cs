using System;
using System.Collections.Immutable;

namespace ZeroAlloc.ORM.Generator;

internal enum SqlPlaceholderKind
{
    // {table}: a member of the declaring type, resolved by name.
    Member,
    // {col:Name}: a column of the entity, resolved by property name.
    Column,
}

internal sealed record SqlPlaceholderSpan(int Start, int Length, SqlPlaceholderKind Kind, string Name);

// MaskedSql has the same length as the input, with every placeholder replaced by an identifier
// of equal length, so downstream tokenising and splitting still sees valid SQL.
internal sealed record SqlPlaceholderScan(string MaskedSql, ImmutableArray<SqlPlaceholderSpan> Spans);

// Finds {table} and {col:Name} placeholders in a single left-to-right pass. Text inside string
// literals, quoted identifiers, dollar quotes and comments is never a placeholder.
internal static class SqlPlaceholderScanner
{
    private const string ColumnPrefix = "col:";

    internal static SqlPlaceholderScan Scan(string sql)
    {
        var spans = ImmutableArray.CreateBuilder<SqlPlaceholderSpan>();
        char[]? masked = null;
        var i = 0;
        while (i < sql.Length)
        {
            var c = sql[i];
            // Dialect choices: backslash-escaped quotes (MySQL, Postgres E strings) are not honoured,
            // and '[' opens a bracket identifier in every dialect, so a placeholder inside an array
            // subscript is not found.
            switch (c)
            {
                case '\'':
                    i = SkipQuoted(sql, i, '\'', doubling: true);
                    break;
                case '"':
                    i = SkipQuoted(sql, i, '"', doubling: true);
                    break;
                case '`':
                    i = SkipQuoted(sql, i, '`', doubling: true);
                    break;
                case '[':
                    i = SkipQuoted(sql, i, ']', doubling: false);
                    break;
                case '$':
                    // Not an opener in the middle of an identifier such as a$b$.
                    i = i > 0 && (IsIdentifierChar(sql[i - 1]) || sql[i - 1] == '$')
                        ? i + 1
                        : SkipDollarQuote(sql, i);
                    break;
                case '-' when i + 1 < sql.Length && sql[i + 1] == '-':
                    i = SkipLineComment(sql, i);
                    break;
                case '/' when i + 1 < sql.Length && sql[i + 1] == '*':
                    i = SkipBlockComment(sql, i);
                    break;
                case '{':
                    i = TryReadPlaceholder(sql, i, spans, ref masked);
                    break;
                default:
                    i++;
                    break;
            }
        }

        return new SqlPlaceholderScan(masked is null ? sql : new string(masked), spans.ToImmutable());
    }

    private static int TryReadPlaceholder(
        string sql, int start, ImmutableArray<SqlPlaceholderSpan>.Builder spans, ref char[]? masked)
    {
        // Bounded scan: the name is at most an optional "col:" prefix plus identifier characters,
        // so an unclosed brace never searches to the end of the SQL.
        var nameStart = start + 1;
        var kind = SqlPlaceholderKind.Member;
        if (string.CompareOrdinal(sql, nameStart, ColumnPrefix, 0, ColumnPrefix.Length) == 0)
        {
            kind = SqlPlaceholderKind.Column;
            nameStart += ColumnPrefix.Length;
        }

        var close = nameStart;
        while (close < sql.Length && IsIdentifierChar(sql[close]))
        {
            close++;
        }

        if (close >= sql.Length || sql[close] != '}' || close == nameStart || !IsAsciiLetterOrUnderscore(sql[nameStart]))
        {
            return start + 1;
        }

        var inner = sql.AsSpan(nameStart, close - nameStart);

        var length = close - start + 1;
        spans.Add(new SqlPlaceholderSpan(start, length, kind, inner.ToString()));
        masked ??= sql.ToCharArray();
        masked[start] = '_';
        for (var k = start + 1; k < close + 1; k++)
        {
            masked[k] = 'z';
        }

        return close + 1;
    }

    // The spans that sit where a value belongs, for ZAO116. A narrow heuristic on the masked SQL:
    // a span right after (ignoring whitespace) =, <, >, <=, >=, <> or !=, the keyword LIKE, or
    // "IN (", or at nesting depth one of a VALUES (...) tuple. Identifier positions (FROM, JOIN,
    // UPDATE, INTO, TABLE, after a dot, a column list, SELECT, ORDER BY) are never flagged.
    internal static ImmutableArray<SqlPlaceholderSpan> FindValuePositionSpans(SqlPlaceholderScan scan)
    {
        var sql = BlankComments(scan.MaskedSql);
        var inTuple = TupleDepthOneStarts(sql, scan.Spans);
        var result = ImmutableArray.CreateBuilder<SqlPlaceholderSpan>();
        foreach (var span in scan.Spans)
        {
            if (inTuple.Contains(span.Start) || FollowsValueOperator(sql, span.Start))
                result.Add(span);
        }

        return result.ToImmutable();
    }

    // The SQL with every comment replaced by spaces, same length, so a comment between two tokens
    // neither hides nor fakes an operator.
    private static string BlankComments(string sql)
    {
        char[]? blanked = null;
        var i = 0;
        while (i < sql.Length)
        {
            var next = SkipOpaque(sql, i);
            var c = sql[i];
            if ((c == '-' || c == '/') && next > i + 1)
            {
                blanked ??= sql.ToCharArray();
                for (var k = i; k < next; k++) blanked[k] = ' ';
            }

            i = next;
        }

        return blanked is null ? sql : new string(blanked);
    }

    private static bool FollowsValueOperator(string sql, int start)
    {
        var p = start - 1;
        while (p >= 0 && char.IsWhiteSpace(sql[p])) p--;
        if (p < 0) return false;
        var c = sql[p];
        if (c == '=' || c == '<' || c == '>') return true;
        if (c == '(')
        {
            p--;
            while (p >= 0 && char.IsWhiteSpace(sql[p])) p--;
            return WordEndsAt(sql, p, "IN");
        }

        return WordEndsAt(sql, p, "LIKE");
    }

    // True when `word` ends at index `end` as a whole word, ignoring case.
    private static bool WordEndsAt(string sql, int end, string word)
    {
        var begin = end - word.Length + 1;
        if (begin < 0) return false;
        if (string.Compare(sql, begin, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) != 0) return false;
        return begin == 0 || !IsIdentifierChar(sql[begin - 1]);
    }

    // The starts of the spans at depth one of a VALUES (...) tuple, also in the later tuples of
    // a multi-row VALUES (...), (...).
    private static System.Collections.Generic.HashSet<int> TupleDepthOneStarts(
        string sql, ImmutableArray<SqlPlaceholderSpan> spans)
    {
        var hits = new System.Collections.Generic.HashSet<int>();
        var starts = new System.Collections.Generic.HashSet<int>();
        foreach (var span in spans) starts.Add(span.Start);
        var i = 0;
        while (i < sql.Length)
        {
            var c = sql[i];
            if (IsAsciiLetterOrUnderscore(c))
            {
                var wordStart = i;
                while (i < sql.Length && IsIdentifierChar(sql[i])) i++;
                if (i - wordStart == 6
                    && string.Compare(sql, wordStart, "VALUES", 0, 6, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    i = ReadValuesTuples(sql, i, starts, hits);
                }

                continue;
            }

            i = SkipOpaque(sql, i);
        }

        return hits;
    }

    private static int ReadValuesTuples(
        string sql, int i, System.Collections.Generic.HashSet<int> starts, System.Collections.Generic.HashSet<int> hits)
    {
        while (true)
        {
            while (i < sql.Length && char.IsWhiteSpace(sql[i])) i++;
            if (i >= sql.Length || sql[i] != '(') return i;
            var depth = 0;
            while (i < sql.Length)
            {
                var c = sql[i];
                if (c == '(')
                {
                    depth++;
                    i++;
                }
                else if (c == ')')
                {
                    depth--;
                    i++;
                    if (depth == 0) break;
                }
                else
                {
                    if (depth == 1 && starts.Contains(i)) hits.Add(i);
                    i = IsAsciiLetterOrUnderscore(c) ? SkipWord(sql, i) : SkipOpaque(sql, i);
                }
            }

            while (i < sql.Length && char.IsWhiteSpace(sql[i])) i++;
            if (i >= sql.Length || sql[i] != ',') return i;
            i++;
        }
    }

    private static int SkipWord(string sql, int i)
    {
        while (i < sql.Length && IsIdentifierChar(sql[i])) i++;
        return i;
    }

    // Steps over one character, or over a whole quoted identifier, string, dollar quote or comment.
    private static int SkipOpaque(string sql, int i)
    {
        var c = sql[i];
        switch (c)
        {
            case '\'': return SkipQuoted(sql, i, '\'', doubling: true);
            case '"': return SkipQuoted(sql, i, '"', doubling: true);
            case '`': return SkipQuoted(sql, i, '`', doubling: true);
            case '[': return SkipQuoted(sql, i, ']', doubling: false);
            case '$':
                return i > 0 && (IsIdentifierChar(sql[i - 1]) || sql[i - 1] == '$') ? i + 1 : SkipDollarQuote(sql, i);
            case '-' when i + 1 < sql.Length && sql[i + 1] == '-': return SkipLineComment(sql, i);
            case '/' when i + 1 < sql.Length && sql[i + 1] == '*': return SkipBlockComment(sql, i);
            default: return i + 1;
        }
    }

    private static bool IsIdentifierChar(char c) =>
        IsAsciiLetterOrUnderscore(c) || (c >= '0' && c <= '9');

    private static bool IsAsciiLetterOrUnderscore(char c) =>
        c == '_' || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

    private static int SkipQuoted(string sql, int start, char close, bool doubling)
    {
        var i = start + 1;
        while (i < sql.Length)
        {
            if (sql[i] == close)
            {
                if (doubling && i + 1 < sql.Length && sql[i + 1] == close)
                {
                    i += 2;
                    continue;
                }

                return i + 1;
            }

            i++;
        }

        return sql.Length;
    }

    private static int SkipDollarQuote(string sql, int start)
    {
        // $tag$ ... $tag$ with an optional identifier tag. "$1" is a parameter, not a quote.
        var i = start + 1;
        if (i < sql.Length && IsAsciiLetterOrUnderscore(sql[i]))
        {
            while (i < sql.Length && (IsAsciiLetterOrUnderscore(sql[i]) || (sql[i] >= '0' && sql[i] <= '9')))
            {
                i++;
            }
        }

        if (i >= sql.Length || sql[i] != '$')
        {
            return start + 1;
        }

        var tag = sql.Substring(start, i - start + 1);
        var end = sql.IndexOf(tag, i + 1, StringComparison.Ordinal);
        return end < 0 ? sql.Length : end + tag.Length;
    }

    private static int SkipLineComment(string sql, int start)
    {
        var end = sql.IndexOf('\n', start);
        return end < 0 ? sql.Length : end + 1;
    }

    private static int SkipBlockComment(string sql, int start)
    {
        var depth = 1;
        var i = start + 2;
        while (i < sql.Length)
        {
            if (sql[i] == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                depth++;
                i += 2;
            }
            else if (sql[i] == '*' && i + 1 < sql.Length && sql[i + 1] == '/')
            {
                depth--;
                i += 2;
                if (depth == 0)
                {
                    return i;
                }
            }
            else
            {
                i++;
            }
        }

        return sql.Length;
    }
}
