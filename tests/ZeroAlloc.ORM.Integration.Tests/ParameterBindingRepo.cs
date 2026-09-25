using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

// v2.0, #219 — the generator emits every DbParameter.ParameterName without a
// sigil, and the provider matches the bare name to the placeholder the SQL
// carries. This repo exercises every way a parameter gets its name, against
// every provider the suite runs:
//
//   * customerid        — plain scalar, named after the C# parameter.
//   * price_Amount /    — composite, one DbParameter per ctor argument, named
//     price_Currency      `{parameter}_{ctorArgument}`.
//   * excluded          — [Param(Name)] override written with the v1-style `@`,
//                         which the generator now drops.
//
// Lowercase identifiers keep the SQL portable across Postgres's case folding.
// Consumed by ParameterBindingTests (SQLite), PostgresParameterBindingTests
// and SqlServerParameterBindingTests.
public sealed partial class ParameterBindingRepo(IAsyncDbConnection connection)
{
    [Query("""
        SELECT id FROM orders
        WHERE customerid = @customerid
          AND total >= @price_Amount
          AND currency = @price_Currency
          AND id <> @excluded
        ORDER BY id
        """)]
    public partial Task<int?> FirstMatchAsync(
        int customerid,
        Money price,
        [Param(Name = "@excluded")] int excludedId,
        CancellationToken ct);

    // The same bind with Oracle's `:` sigil in the SQL. SQL Server has no such
    // placeholder, but Microsoft.Data.Sqlite and Npgsql both accept it, so on
    // those two it proves the unprefixed ParameterName is what makes the
    // generated code independent of the placeholder sigil. With the v1 `@id`
    // ParameterName, Microsoft.Data.Sqlite does not bind `:id` at all.
    [Query("SELECT id FROM orders WHERE customerid = :customerid AND id <> :excluded ORDER BY id")]
    public partial Task<int?> FirstForCustomerColonAsync(
        int customerid,
        [Param(Name = ":excluded")] int excludedId,
        CancellationToken ct);

    public const string OrdersDdl = """
        CREATE TABLE orders (id INTEGER PRIMARY KEY, customerid INTEGER NOT NULL, total NUMERIC(10, 2) NOT NULL, currency VARCHAR(3) NOT NULL);
        INSERT INTO orders (id, customerid, total, currency) VALUES (1, 7, 80.00, 'EUR');
        INSERT INTO orders (id, customerid, total, currency) VALUES (2, 7, 90.00, 'EUR');
        INSERT INTO orders (id, customerid, total, currency) VALUES (3, 7, 95.00, 'USD');
        INSERT INTO orders (id, customerid, total, currency) VALUES (4, 8, 99.00, 'EUR');
        INSERT INTO orders (id, customerid, total, currency) VALUES (5, 7, 10.00, 'EUR');
        """;

    // With the seed above, customer 7 paying at least 50 EUR matches ids 1 and 2.
    // Excluding id 1 must leave 2; every other row is filtered out by exactly
    // one of the parameters, so a parameter that failed to bind shows up as a
    // wrong answer or a provider error rather than passing by accident.
    public const int ExpectedMatch = 2;
}
