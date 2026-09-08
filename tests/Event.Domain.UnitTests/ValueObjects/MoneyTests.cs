using System.Reflection;
using Explore.Domain.ValueObjects;

namespace Event.Domain.UnitTests.ValueObjects;

public sealed class MoneyTests
{
    [Test]
    public async Task CreateNormalizesCurrencyAndPreservesMinorUnits()
    {
        Money money = Money.Create(12_345, " eur ");

        await Assert.That(money.MinorUnits).IsEqualTo(12_345);
        await Assert.That(money.CurrencyCode).IsEqualTo("EUR");
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments(" ")]
    [Arguments("EU")]
    [Arguments("EU1")]
    [Arguments("ZZZ")]
    public async Task CreateRejectsBlankMalformedOrUnsupportedCurrency(string? currencyCode)
    {
        await Assert.That(() => Money.Create(1, currencyCode!)).Throws<ArgumentException>();
    }

    [Test]
    public async Task CreateRejectsNegativeMinorUnits()
    {
        await Assert.That(() => Money.Create(-1, "EUR")).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task NoCurrencySentinelRequiresZeroMinorUnits()
    {
        Money free = Money.Create(0, "XXX");

        await Assert.That(free.CurrencyCode).IsEqualTo("XXX");
        await Assert.That(() => Money.Create(1, "XXX")).Throws<ArgumentException>();
    }

    [Test]
    public async Task EqualityUsesNormalizedCurrencyAndMinorUnits()
    {
        Money left = Money.Create(long.MaxValue, "kwd");
        Money equal = Money.Create(long.MaxValue, " KWD ");
        Money differentAmount = Money.Create(long.MaxValue - 1, "KWD");
        Money differentCurrency = Money.Create(long.MaxValue, "EUR");

        await Assert.That(left).IsEqualTo(equal);
        await Assert.That(left.GetHashCode()).IsEqualTo(equal.GetHashCode());
        await Assert.That(left).IsNotEqualTo(differentAmount);
        await Assert.That(left).IsNotEqualTo(differentCurrency);
    }

    [Test]
    public async Task FormattingIsBoundedAndContainsOnlyAmountAndCurrency()
    {
        string formatted = Money.Create(12_345, "EUR").ToString();

        await Assert.That(formatted).IsEqualTo("EUR 12345");
    }

    [Test]
    public async Task SurfaceHasNoPublicConstructionConversionsOrArithmeticOperators()
    {
        MethodInfo[] forbiddenOperators = typeof(Money)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name is
                "op_Implicit" or
                "op_Explicit" or
                "op_Addition" or
                "op_Subtraction" or
                "op_Multiply" or
                "op_Division")
            .ToArray();

        await Assert.That(typeof(Money).IsClass).IsTrue();
        await Assert.That(typeof(Money).IsSealed).IsTrue();
        await Assert.That(typeof(Money).GetConstructors()).IsEmpty();
        await Assert.That(forbiddenOperators).IsEmpty();
    }
}
