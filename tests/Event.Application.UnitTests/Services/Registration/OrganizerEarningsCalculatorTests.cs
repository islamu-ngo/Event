using Explore.Application.Contracts.Services;
using Explore.Application.Services.Registration;
using Explore.Domain;

namespace Event.Application.UnitTests.Services.Registration;

public sealed class OrganizerEarningsCalculatorTests
{
    [Test]
    public async Task Calculate_UsesExactMinorUnitBasisPointsAndFixedCharges()
    {
        PlatformFeePolicy policy = PlatformFeePolicy.CreateDefault().CreateRevision(
            true,
            250,
            [PlatformFeeFixedCharge.Create("USD", 25)]);
        IOrganizerEarningsCalculator calculator = new OrganizerEarningsCalculator();

        OrganizerEarnings earnings = calculator.Calculate("USD", 10_001, policy);

        await Assert.That(earnings.OrganizerDirectedTotalMinor).IsEqualTo(10_001);
        await Assert.That(earnings.PlatformFeeMinor).IsEqualTo(275);
        await Assert.That(earnings.OrganizerEarningsMinor).IsEqualTo(9_726);
        await Assert.That(earnings.PlatformFeePolicyVersionSnapshot).IsEqualTo(2);
    }

    [Test]
    public async Task Calculate_DisabledPolicyDoesNotReduceOrganizerEarnings()
    {
        IOrganizerEarningsCalculator calculator = new OrganizerEarningsCalculator();

        OrganizerEarnings earnings = calculator.Calculate("EUR", 3, PlatformFeePolicy.CreateDefault());

        await Assert.That(earnings.PlatformFeeMinor).IsEqualTo(0);
        await Assert.That(earnings.OrganizerEarningsMinor).IsEqualTo(3);
        await Assert.That(earnings.PlatformFeePolicyVersionSnapshot).IsNull();
    }

    [Test]
    public async Task Calculate_WhenNoCurrencyHasPositiveTotal_RejectsMonetaryEarnings()
    {
        IOrganizerEarningsCalculator calculator = new OrganizerEarningsCalculator();

        await Assert.That(() => calculator.Calculate("XXX", 1, null))
            .Throws<ArgumentException>();
    }
}
