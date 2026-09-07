namespace Event.Domain.UnitTests.Entities;

public sealed class PlatformMonetizationTests
{
    [Test]
    public async Task CreateDefault_UsesDisabledZeroFeeAndVersionOne()
    {
        PlatformFeePolicy policy = PlatformFeePolicy.CreateDefault();

        await Assert.That(policy.VersionNumber).IsEqualTo(1);
        await Assert.That(policy.IsActive).IsTrue();
        await Assert.That(policy.IsEnabled).IsFalse();
        await Assert.That(policy.FeeBasisPoints).IsEqualTo(0);
        await Assert.That(policy.FixedCharges).IsEmpty();
        await Assert.That(policy.CalculateFeeMinor("USD", 100)).IsEqualTo(0);
    }

    [Test]
    public async Task CreateRevision_PreservesPriorPolicyAndIncrementsVersion()
    {
        PlatformFeePolicy original = PlatformFeePolicy.CreateDefault();
        PlatformFeePolicy revision = original.CreateRevision(
            true,
            250,
            [
                PlatformFeeFixedCharge.Create("USD", 25),
                PlatformFeeFixedCharge.Create("EUR", 20)
            ]);

        await Assert.That(revision.Id).IsNotEqualTo(original.Id);
        await Assert.That(revision.VersionNumber).IsEqualTo(2);
        await Assert.That(original.IsActive).IsFalse();
        await Assert.That(revision.IsActive).IsTrue();
        await Assert.That(revision.IsEnabled).IsTrue();
        await Assert.That(revision.FeeBasisPoints).IsEqualTo(250);
        await Assert.That(revision.GetFixedChargeMinor("USD")).IsEqualTo(25);
        await Assert.That(revision.GetFixedChargeMinor("EUR")).IsEqualTo(20);
        await Assert.That(revision.GetFixedChargeMinor("JPY")).IsEqualTo(0);
        await Assert.That(revision.CalculateFeeMinor("USD", 1_000)).IsEqualTo(50);
        await Assert.That(original.FeeBasisPoints).IsEqualTo(0);
    }

    [Test]
    public async Task ContributionInitialVersion_UsesCallerSuppliedStoredOptions()
    {
        PlatformContributionSetting setting = PlatformContributionSetting.CreateInitial(false, string.Empty, string.Empty, CreateOptions());
        int[] basisPoints = setting.Options.OrderBy(option => option.SortOrder).Select(option => option.ContributionBasisPoints).ToArray();

        await Assert.That(setting.VersionNumber).IsEqualTo(1);
        await Assert.That(setting.IsActive).IsTrue();
        await Assert.That(setting.IsEnabled).IsFalse();
        await Assert.That(setting.Heading).IsEmpty();
        await Assert.That(setting.Body).IsEmpty();
        await Assert.That(basisPoints.SequenceEqual([0, 500, 1_000, 1_500, 2_000])).IsTrue();
        await Assert.That(setting.Options.Single(option => option.IsDefault).ContributionBasisPoints).IsEqualTo(0);
        await Assert.That(setting.Options.Single(option => option.ContributionBasisPoints == 500).CalculateAmountMinor(10_000)).IsEqualTo(500);
    }

    [Test]
    public async Task ContributionRevision_ClonesOptionsAndRejectsNonZeroDefault()
    {
        PlatformContributionSetting original = PlatformContributionSetting.CreateInitial(false, string.Empty, string.Empty, CreateOptions());
        PlatformContributionSetting revision = original.CreateRevision(
            true,
            "Support the platform",
            "Optional contribution",
            original.Options);

        await Assert.That(revision.Id).IsNotEqualTo(original.Id);
        await Assert.That(revision.VersionNumber).IsEqualTo(2);
        await Assert.That(original.IsActive).IsFalse();
        await Assert.That(revision.IsActive).IsTrue();
        await Assert.That(revision.Options.First().Id).IsNotEqualTo(original.Options.First().Id);
        await Assert.That(() => revision.CreateRevision(
                true,
                "Heading",
                "Body",
                [PlatformContributionOption.Create(500, 0, true)]))
            .Throws<ArgumentException>();
        await Assert.That(() => revision.CreateRevision(true, string.Empty, string.Empty, CreateOptions()))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task FeePolicy_RejectsDuplicateCurrencyQualifiedFixedAmountsAndRetiredRevisions()
    {
        PlatformFeePolicy policy = PlatformFeePolicy.CreateDefault();

        await Assert.That(() => policy.CreateRevision(
                true,
                100,
                [PlatformFeeFixedCharge.Create("USD", 10), PlatformFeeFixedCharge.Create("USD", 20)]))
            .Throws<ArgumentException>();

        policy.Retire();

        await Assert.That(policy.IsActive).IsFalse();
        await Assert.That(() => policy.CreateRevision(false, 0, [])).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task FeePolicy_DisabledOrExcessiveFeesDoNotReduceOrganizerSubtotalBelowZero()
    {
        PlatformFeePolicy disabledPolicy = PlatformFeePolicy.CreateDefault().CreateRevision(
            false,
            2_500,
            [PlatformFeeFixedCharge.Create("USD", 25)]);
        PlatformFeePolicy cappedPolicy = PlatformFeePolicy.CreateDefault().CreateRevision(
            true,
            2_500,
            [PlatformFeeFixedCharge.Create("USD", 25)]);

        await Assert.That(disabledPolicy.CalculateFeeMinor("USD", 10)).IsEqualTo(0);
        await Assert.That(cappedPolicy.CalculateFeeMinor("USD", 10)).IsEqualTo(10);
        await Assert.That(cappedPolicy.CalculateFeeMinor("USD", -10)).IsEqualTo(0);
    }

    private static PlatformContributionOption[] CreateOptions() =>
    [
        PlatformContributionOption.Create(0, 0, true),
        PlatformContributionOption.Create(500, 1, false),
        PlatformContributionOption.Create(1_000, 2, false),
        PlatformContributionOption.Create(1_500, 3, false),
        PlatformContributionOption.Create(2_000, 4, false)
    ];
}
