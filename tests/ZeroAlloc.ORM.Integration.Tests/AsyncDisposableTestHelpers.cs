using System;
using System.Runtime.CompilerServices;

namespace ZeroAlloc.ORM.Integration.Tests;

// Shared test helper: collapse the
// `await using (((System.IAsyncDisposable)reader).ConfigureAwait(false))`
// boilerplate that the data-reader interfaces in System.Data.Async require
// (the interface itself doesn't expose a `ConfigureAwait` instance method —
// the cast routes through IAsyncDisposable). Used by integration tests that
// open a probe DbDataReader to verify what landed in the database after a
// generator-emitted command.
internal static class AsyncDisposableTestHelpers
{
    public static ConfiguredAsyncDisposable ConfigureAwaitAsDisposable(this IAsyncDisposable disposable)
        => disposable.ConfigureAwait(false);
}
