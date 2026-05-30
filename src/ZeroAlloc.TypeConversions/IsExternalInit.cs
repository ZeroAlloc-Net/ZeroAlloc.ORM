#if NETSTANDARD2_0
// Polyfill: C# 9 init-only setters and records emit references to
// System.Runtime.CompilerServices.IsExternalInit, which doesn't ship in
// netstandard2.0. Roslyn analyzer projects target netstandard2.0 by convention,
// so we ship the shim ourselves. Internal-only — never exposed on the public API.
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit
{
}
#endif
