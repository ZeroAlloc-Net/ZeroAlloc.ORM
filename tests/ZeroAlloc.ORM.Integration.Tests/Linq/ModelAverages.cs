namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// One model's averages and extremes, each read with the reader of its CLR type.
internal sealed record ModelAverages(
    string? ModelId,
    double Input,
    decimal InputDecimal,
    double? Sequence,
    int MinInput,
    long MaxInput,
    DateTime LastExecuted);
