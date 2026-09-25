using System.Globalization;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

// #264 — a factory whose parameters run in the opposite order to the ctor's. The
// factory's parameter list drives the inner columns, so the SELECT lists Currency
// before Amount and the generated call must pass them in that order.
[Materialize(Factory = "FromStorage")]
public readonly record struct MoneyWithReversedFactory(decimal Amount, string Currency)
{
    public static MoneyWithReversedFactory FromStorage(string currency, string amountText)
        => new MoneyWithReversedFactory(
            decimal.Parse(amountText, NumberStyles.Number, CultureInfo.InvariantCulture),
            currency);
}
