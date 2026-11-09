using DigitalWallet.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DigitalWallet.Infrastructure.Persistence.Configurations;

/// <summary>Shared mapping for <see cref="Money"/>: decimal with 2 places (BRL) plus currency code.</summary>
internal static class MoneyConfiguration
{
    public static void Configure(ComplexPropertyBuilder<Money> builder)
    {
        builder.Property(money => money.Amount).HasPrecision(18, 2);
        builder.Property(money => money.Currency).HasMaxLength(3).IsRequired();
    }
}
