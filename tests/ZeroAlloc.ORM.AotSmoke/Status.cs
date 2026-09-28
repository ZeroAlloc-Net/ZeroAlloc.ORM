namespace ZeroAlloc.ORM.AotSmoke;

// Enum stored as its name through [StoreAsString]; reads go through Enum.Parse<Status>.
[StoreAsString]
public enum Status
{
    Draft = 0,
    Active = 1,
    Archived = 2,
}
