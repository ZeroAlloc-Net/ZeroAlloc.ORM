using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ZeroAlloc.ORM.Generator.Model;

// Extracts the placeholder list from a [Command(Kind = BulkInsert)] SQL
// template's VALUES tuple. Exactly one tuple required — multiple tuples
// means the user already wrote multi-row SQL (which doesn't compose with
// BulkInsert's auto-multiplication) and we reject with ZAO071. Zero
// VALUES clauses also rejected (probably an INSERT...SELECT or similar).
internal static class BulkInsertValuesParser
{
    // Matches `VALUES (@p1, @p2, …)` — case-insensitive on VALUES.
    // Only the @-placeholder shape produces a successful parse, since the
    // codegen needs named placeholders to bind row properties.
    private static readonly Regex ValuesTuple = new(
        @"\bVALUES\s*\(\s*(?<inner>@\w+(?:\s*,\s*@\w+)*)\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Matches the opening `VALUES (` token. Used to detect "there is a
    // VALUES clause but our @-placeholder shape didn't match it" (e.g. the
    // tuple contains literals like `VALUES (1, 2)`) vs "no VALUES at all".
    private static readonly Regex AnyValuesClause = new(
        @"\bVALUES\s*\(",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Matches `), (` between row-tuples in a multi-row VALUES clause.
    // We count these to report a correct TupleCount (>=2) when the user
    // wrote multi-row SQL like `VALUES (1, 2), (3, 4)` — even though the
    // @-placeholder regex matches zero of them, we still want to tell the
    // user "you supplied N tuples" via ZAO071.
    //
    // Limitations:
    //   This parser is regex-based, not a full SQL tokeniser. Nested-paren
    //   patterns within a single VALUES tuple — e.g. `VALUES (@A, COALESCE(@B, 0))`
    //   or `VALUES (@A, (SELECT 1))` — can match the `)\s*,\s*\(` separator
    //   spuriously and be misclassified as a multi-tuple insert. In that
    //   case the user sees ZAO071 ("looks like multi-row VALUES") rather
    //   than a more precise diagnostic. A proper tokeniser is deferred to
    //   a future iteration; the classifier (Task 5) should word ZAO071
    //   defensively ("looks like multi-row VALUES", not "is multi-row VALUES").
    private static readonly Regex RowTupleSeparator = new(
        @"\)\s*,\s*\(",
        RegexOptions.Compiled);

    internal sealed record Result(
        bool Success,
        IReadOnlyList<string> Placeholders,
        int TupleCount,
        int TupleStart,
        int TupleLength,
        int ValuesClauseStart);   // NEW — for diagnostic Location anchoring

    public static Result TryParse(string sql)
    {
        var anyValuesMatch = AnyValuesClause.Match(sql);
        if (!anyValuesMatch.Success)
        {
            // No VALUES clause at all (e.g. INSERT…SELECT).
            return new Result(
                Success: false,
                Placeholders: Array.Empty<string>(),
                TupleCount: 0,
                TupleStart: 0,
                TupleLength: 0,
                ValuesClauseStart: -1);
        }

        // Count comma-separated row tuples after the VALUES keyword. The
        // first tuple is implicit (the `(` consumed by AnyValuesClause),
        // then each `), (` adds another. We only scan the substring after
        // the VALUES keyword so a separator in some other clause cannot
        // mis-inflate the count.
        var afterValues = anyValuesMatch.Index + anyValuesMatch.Length - 1; // index of '('
        var tail = sql.Substring(afterValues);
        var rowSeparatorCount = RowTupleSeparator.Matches(tail).Count;
        var rowTupleCount = 1 + rowSeparatorCount;

        if (rowTupleCount > 1)
        {
            // Multi-row VALUES — incompatible with BulkInsert auto-multiplication.
            return new Result(
                Success: false,
                Placeholders: Array.Empty<string>(),
                TupleCount: rowTupleCount,
                TupleStart: 0,
                TupleLength: 0,
                ValuesClauseStart: anyValuesMatch.Index);
        }

        // Exactly one row tuple — try to extract @-placeholders from it.
        var placeholderMatch = ValuesTuple.Match(sql);
        if (!placeholderMatch.Success)
        {
            // VALUES clause present but doesn't match the `@name, @name`
            // shape (e.g. literal values, or a malformed list). Surface
            // TupleCount=1 so diagnostics can say "saw 1 VALUES tuple but
            // couldn't extract placeholders".
            return new Result(
                Success: false,
                Placeholders: Array.Empty<string>(),
                TupleCount: 1,
                TupleStart: 0,
                TupleLength: 0,
                ValuesClauseStart: anyValuesMatch.Index);
        }

        var inner = placeholderMatch.Groups["inner"].Value;
        var rawPlaceholders = inner.Split(',');
        var placeholders = new string[rawPlaceholders.Length];
        for (var i = 0; i < rawPlaceholders.Length; i++)
        {
            placeholders[i] = rawPlaceholders[i].Trim().TrimStart('@');
        }

        // TupleStart/TupleLength bracket the literal "(...)" portion — the
        // runtime SQL builder uses these to splice chunk-multiplied tuples
        // back into the original SQL.
        var openParen = sql.IndexOf('(', placeholderMatch.Index);
        var tupleEnd = placeholderMatch.Index + placeholderMatch.Length;
        return new Result(
            Success: true,
            Placeholders: placeholders,
            TupleCount: 1,
            TupleStart: openParen,
            TupleLength: tupleEnd - openParen,
            ValuesClauseStart: anyValuesMatch.Index);
    }
}
