using System.Globalization;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

// v0.5 Phase D.3 — canonical Sqlite decimal-as-text adopter recipe. The
// `[Materialize(Factory = "FromStorage")]` annotation tells the generator to
// dispatch through the static `FromStorage` method instead of the underlying
// `Money(decimal, string)` ctor; the factory takes `string` for the amount and
// parses it under `CultureInfo.InvariantCulture` so Sqlite's TEXT-encoded
// decimal round-trips losslessly regardless of the test host's current culture.
//
// Mirrors the design doc's Sqlite quirk (Section 3, line 366): "The existing
// MoneyConverter.FromStorage pattern in za-clean migrates to a [Materialize(
// Factory = "FromStorage")] annotation on Money."
[Materialize(Factory = "FromStorage")]
public readonly record struct MoneyWithFactory(decimal Amount, string Currency)
{
    public static MoneyWithFactory FromStorage(string amountText, string currency)
        => new MoneyWithFactory(
            decimal.Parse(amountText, NumberStyles.Number, CultureInfo.InvariantCulture),
            currency);
}
