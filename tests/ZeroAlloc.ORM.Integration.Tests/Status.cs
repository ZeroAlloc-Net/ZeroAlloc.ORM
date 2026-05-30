namespace ZeroAlloc.ORM.Integration.Tests;

// Default-int enum — round-trips as the underlying integer value. Stored as
// INTEGER in the Sqlite column. The type name deliberately drops an "Enum"
// suffix to satisfy CA1711 (TreatWarningsAsErrors).
public enum Status
{
    Pending = 0,
    Cancelled = 1,
}
