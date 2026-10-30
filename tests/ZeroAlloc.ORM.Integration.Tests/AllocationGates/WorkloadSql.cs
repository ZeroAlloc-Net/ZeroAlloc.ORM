namespace ZeroAlloc.ORM.Integration.Tests.AllocationGates;

// #351 — the SQL of the gated workloads, shared by the repositories and the hand-written
// baselines so both run the same statements.
internal static class WorkloadSql
{
    public const string SingleRowRead = "SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id";

    public const string Insert = "INSERT INTO Orders (Id, CustomerId, Total) VALUES (@id, @cust, @total)";

    public const string HeadStatement = "SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id";

    public const string LinesStatement = "SELECT Id, OrderId, Sku, Qty FROM OrderLines WHERE OrderId = @id";

    public const string MultiResult = HeadStatement + "; " + LinesStatement + ";";

    // The head row is Id = -1 with ten lines, so an insert with a positive Id never collides.
    public const string Seed = """
        CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER, Total NUMERIC(18,4));
        CREATE TABLE OrderLines (Id INTEGER PRIMARY KEY, OrderId INTEGER, Sku TEXT, Qty INTEGER);
        INSERT INTO Orders (Id, CustomerId, Total) VALUES (-1, 100, 99.95);
        INSERT INTO OrderLines (Id, OrderId, Sku, Qty) VALUES
            (1, -1, 'SKU-1', 1), (2, -1, 'SKU-2', 2), (3, -1, 'SKU-3', 3), (4, -1, 'SKU-4', 4),
            (5, -1, 'SKU-5', 5), (6, -1, 'SKU-6', 6), (7, -1, 'SKU-7', 7), (8, -1, 'SKU-8', 8),
            (9, -1, 'SKU-9', 9), (10, -1, 'SKU-10', 10);
        """;

    public const int HeadId = -1;

    public const int LineCount = 10;
}
