using System.Collections.Immutable;

namespace ZeroAlloc.TypeConversions;

// SQL statement-boundary scanner shared by the ORM generator (ZAO008 multi-
// statement diagnostic, BatchEmitStrategy resolution) and any future tooling
// (e.g. Mapping.Generator stored-proc body analysis).
//
// The scan is intentionally minimal: it understands single- and double-quoted
// string literals (`'...'` / `"..."`) with the SQL doubling-escape convention
// (`''` / `""`) so a `;` inside a literal isn't treated as a statement
// terminator. A trailing `;` with only whitespace after it is NOT counted as
// opening a new empty statement.
//
// What it does NOT yet handle (TODO(v0.4+) — proper SQL tokenizer):
//   * line comments (`-- ...`)
//   * block comments (`/* ... */`)
//   * PostgreSQL dollar-quoted strings (`$tag$...$tag$`)
//
// Hoisted from OrmGenerator in v0.3 Phase A so the same scan powers both the
// ZAO008 diagnostic and the upcoming batch-emit strategy resolution without
// the generator owning a public scanning surface.
public static class SqlStatementSplitter
{
    /// <summary>
    /// Counts SQL statements separated by ';'. String-literal-aware (handles
    /// '...' and "..." quoted segments + escaped quotes via doubling).
    /// Trailing ';' with only whitespace after is ignored.
    /// Does NOT yet understand SQL comments (-- or /* */) or PostgreSQL
    /// dollar-quoted strings — TODO(v0.4+): proper SQL tokenizer.
    /// </summary>
    public static int CountStatements(string sql)
    {
        if (string.IsNullOrEmpty(sql)) return 0;
        var count = 1;
        var inSingleQuote = false;
        var inDoubleQuote = false;
        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            if (inSingleQuote)
            {
                // SQL string escape: '' inside '...' is a literal '.
                if (c == '\'')
                {
                    if (i + 1 < sql.Length && sql[i + 1] == '\'')
                    {
                        i++; // skip the escaped quote
                        continue;
                    }
                    inSingleQuote = false;
                }
                continue;
            }
            if (inDoubleQuote)
            {
                if (c == '"')
                {
                    if (i + 1 < sql.Length && sql[i + 1] == '"')
                    {
                        i++;
                        continue;
                    }
                    inDoubleQuote = false;
                }
                continue;
            }
            switch (c)
            {
                case '\'':
                    inSingleQuote = true;
                    break;
                case '"':
                    inDoubleQuote = true;
                    break;
                case ';':
                    // A trailing `;` (with only whitespace after) doesn't open a new
                    // statement. Walk the remainder inline to avoid the substring
                    // allocation of `sql[(i + 1)..].TrimStart()`.
                    if (IsOnlyWhitespaceAfter(sql, i + 1))
                        return count;
                    count++;
                    break;
            }
        }
        return count;
    }

    /// <summary>
    /// Splits SQL into individual statements. Each returned string has its
    /// own ';' terminator stripped. String-literal-aware (see <see cref="CountStatements"/>).
    /// Returns empty when the input is null/empty. A trailing ';' with only
    /// whitespace after it is dropped (no empty final element).
    /// </summary>
    public static ImmutableArray<string> Split(string sql)
    {
        if (string.IsNullOrEmpty(sql)) return ImmutableArray<string>.Empty;
        var builder = ImmutableArray.CreateBuilder<string>();
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var segmentStart = 0;
        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            if (inSingleQuote)
            {
                if (c == '\'')
                {
                    if (i + 1 < sql.Length && sql[i + 1] == '\'')
                    {
                        i++;
                        continue;
                    }
                    inSingleQuote = false;
                }
                continue;
            }
            if (inDoubleQuote)
            {
                if (c == '"')
                {
                    if (i + 1 < sql.Length && sql[i + 1] == '"')
                    {
                        i++;
                        continue;
                    }
                    inDoubleQuote = false;
                }
                continue;
            }
            switch (c)
            {
                case '\'':
                    inSingleQuote = true;
                    break;
                case '"':
                    inDoubleQuote = true;
                    break;
                case ';':
                    // A trailing `;` (only whitespace after) closes the final
                    // statement without producing an empty trailing element.
                    if (IsOnlyWhitespaceAfter(sql, i + 1))
                    {
                        builder.Add(sql.Substring(segmentStart, i - segmentStart));
                        return builder.ToImmutable();
                    }
                    builder.Add(sql.Substring(segmentStart, i - segmentStart));
                    segmentStart = i + 1;
                    break;
            }
        }
        // Unterminated final segment (no trailing ';') — include it verbatim.
        builder.Add(sql.Substring(segmentStart));
        return builder.ToImmutable();
    }

    private static bool IsOnlyWhitespaceAfter(string sql, int startIndex)
    {
        for (var j = startIndex; j < sql.Length; j++)
        {
            if (!char.IsWhiteSpace(sql[j])) return false;
        }
        return true;
    }
}
