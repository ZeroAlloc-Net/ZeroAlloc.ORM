#if NETSTANDARD2_0

// Polyfill: the C# compiler requires System.Runtime.CompilerServices.IsExternalInit
// for `init`-only properties (and record positional members); the type does not exist
// on netstandard2.0 (it was added in net5.0+). This empty internal type satisfies the
// compiler's lookup without shipping a new public API. Once we drop the netstandard2.0
// target, this file can be removed.

namespace System.Runtime.CompilerServices;

internal static class IsExternalInit
{
}

#endif
