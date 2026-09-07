using Explore.Domain.Interfaces;
using Explore.Domain.ValueObjects;

namespace Explore.Domain;

public sealed class RegistrationOrderPlatformContribution : ITenantEntity, IAuditableEntity
{
    private RegistrationOrderPlatformContribution()
    {
    }

    private RegistrationOrderPlatformContribution(
        Guid registrationOrderId,
        Guid tenantId,
        PlatformContributionSetting setting,
        int contributionBasisPointsSnapshot,
        long amountMinor,
        string currencyCode)
    {
        Id = Guid.CreateVersion7();
        RegistrationOrderId = registrationOrderId;
        TenantId = tenantId;
        PlatformContributionSettingIdSnapshot = setting.Id;
        PlatformContributionSettingVersionSnapshot = setting.VersionNumber;
        ContributionBasisPointsSnapshot = contributionBasisPointsSnapshot;
        AmountMinor = amountMinor;
        CurrencyCode = currencyCode;
    }

    public Guid Id { get; private set; }

    public Guid RegistrationOrderId { get; private set; }

    public Guid TenantId { get; set; }

    public Guid PlatformContributionSettingIdSnapshot { get; private set; }

    public int PlatformContributionSettingVersionSnapshot { get; private set; }

    public int ContributionBasisPointsSnapshot { get; private set; }

    public long AmountMinor { get; private set; }

    public string CurrencyCode { get; private set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public static RegistrationOrderPlatformContribution? CreateOrNull(
        Guid registrationOrderId,
        Guid tenantId,
        PlatformContributionSetting setting,
        int contributionBasisPoints,
        long organizerDirectedTotalMinor,
        string currencyCode)
    {
        ArgumentNullException.ThrowIfNull(setting);

        if (registrationOrderId == Guid.Empty || tenantId == Guid.Empty || organizerDirectedTotalMinor < 0)
        {
            throw new ArgumentException("Registration order, tenant, and non-negative total are required.");
        }

        CurrencyMetadata currency = CurrencyMetadata.Get(currencyCode);
        if (!setting.IsEnabled || currency.IsNoCurrency)
        {
            throw new InvalidOperationException("Platform contributions require an enabled monetary setting.");
        }

        PlatformContributionOption option = setting.Options.SingleOrDefault(option => option.ContributionBasisPoints == contributionBasisPoints)
            ?? throw new ArgumentException("Contribution selection is not enabled by the setting.", nameof(contributionBasisPoints));
        long amountMinor = MinorUnitMath.ApplyBasisPoints(organizerDirectedTotalMinor, option.ContributionBasisPoints);
        return contributionBasisPoints == 0
            ? null
            : new RegistrationOrderPlatformContribution(
                registrationOrderId,
                tenantId,
                setting,
                contributionBasisPoints,
                amountMinor,
                currency.Code);
    }

    public void Reprice(long organizerDirectedTotalMinor)
    {
        if (organizerDirectedTotalMinor < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(organizerDirectedTotalMinor));
        }

        AmountMinor = MinorUnitMath.ApplyBasisPoints(organizerDirectedTotalMinor, ContributionBasisPointsSnapshot);
    }
}
