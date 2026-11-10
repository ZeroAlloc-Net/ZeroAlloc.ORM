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
