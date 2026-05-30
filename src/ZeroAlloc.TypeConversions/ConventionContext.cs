using Microsoft.CodeAnalysis;

namespace ZeroAlloc.TypeConversions;

/// <summary>
/// Carrier for the <see cref="Microsoft.CodeAnalysis.Compilation"/> the discovery layer needs
/// for symbol lookups (well-known attributes, primitives, etc.). Future fields can extend this
/// without breaking the <see cref="ConventionDiscovery.Resolve"/> signature.
/// </summary>
/// <param name="Compilation">The current compilation under analysis.</param>
public sealed record ConventionContext(Compilation Compilation);
