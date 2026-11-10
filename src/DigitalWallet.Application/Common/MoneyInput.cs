using System.Globalization;
using DigitalWallet.Application.Common;
using DigitalWallet.Domain.ValueObjects;

namespace DigitalWallet.Application.Common;

/// <summary>Converts a client amount into money. BRL has two decimal places; extra precision is a malformed request.</summary>
internal static class MoneyInput
{
    public static Money ToBrl(decimal amount)
    {
        if (decimal.Round(amount, 2) != amount)
        {
            throw new RequestValidationException(
                "amount",
                $"The amount must have at most 2 decimal places. Received: {amount.ToString(CultureInfo.InvariantCulture)}.");
        }

        return Money.Brl(amount);
    }
}
