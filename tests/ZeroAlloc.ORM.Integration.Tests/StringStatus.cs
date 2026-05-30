using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

// [StoreAsString] forces round-trip via the enum member name. Stored as TEXT
// in the column; parameter goes down as the literal "Cancelled" and reads
// parse back via Enum.Parse. The type name deliberately drops an "Enum"
// suffix to satisfy CA1711 (TreatWarningsAsErrors).
[StoreAsString]
public enum StringStatus
{
    Pending,
    Cancelled,
}
