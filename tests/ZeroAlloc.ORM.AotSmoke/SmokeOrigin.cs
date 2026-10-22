namespace ZeroAlloc.ORM.AotSmoke;

// Owned by SmokeTask and optional: a task without an origin has both columns NULL.
public sealed class SmokeOrigin
{
    public string Url { get; private set; } = "";

    public string? Branch { get; private set; }
}
