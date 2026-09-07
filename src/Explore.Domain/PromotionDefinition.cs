using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class PromotionDefinition : ITenantEntity, IAuditableEntity
{
    private PromotionDefinition()
    {
    }

    private PromotionDefinition(
        Guid definitionGroupId,
        int versionNumber,
        PromotionScopeMetadata scopeMetadata,
        string displayLabel,
        PromotionEligibility eligibility,
        PromotionDiscountRule discountRule,
        DateTime startsAtUtc,
        DateTime endsAtUtc,
        int? totalRedemptionLimit,
        int? perVerifiedPurchaserLimit)
    {
        Id = Guid.CreateVersion7();
        DefinitionGroupId = definitionGroupId;
        VersionNumber = versionNumber;
        TenantId = scopeMetadata.TenantId;
        ScopeMetadata = scopeMetadata;
        DisplayLabel = NormalizeLabel(displayLabel);
        Eligibility = eligibility;
        DiscountRule = discountRule;
        StartsAtUtc = EnsureUtc(startsAtUtc, nameof(startsAtUtc));
        EndsAtUtc = EnsureUtc(endsAtUtc, nameof(endsAtUtc));
        ValidateWindowAndLimits(totalRedemptionLimit, perVerifiedPurchaserLimit);
        TotalRedemptionLimit = totalRedemptionLimit;
        PerVerifiedPurchaserLimit = perVerifiedPurchaserLimit;
        PromotionDefinitionStatusId = (int)PromotionDefinitionStatusEnum.Draft;
    }

    public Guid Id { get; private set; }

    public Guid DefinitionGroupId { get; private set; }

    public Guid TenantId { get; set; }

    public int VersionNumber { get; private set; }

    public int PromotionDefinitionStatusId { get; private set; }

    public PromotionScopeMetadata ScopeMetadata { get; private set; } = null!;

    public string DisplayLabel { get; private set; } = string.Empty;

    public PromotionEligibility Eligibility { get; private set; } = null!;

    public PromotionDiscountRule DiscountRule { get; private set; } = null!;

    public DateTime StartsAtUtc { get; private set; }

    public DateTime EndsAtUtc { get; private set; }

    public int? TotalRedemptionLimit { get; private set; }

    public int? PerVerifiedPurchaserLimit { get; private set; }

    public DateTime? PublishedAtUtc { get; private set; }

    public DateTime? RevokedAtUtc { get; private set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public static PromotionDefinition CreateDraft(
        PromotionScopeMetadata scopeMetadata,
        string displayLabel,
        PromotionEligibility eligibility,
        PromotionDiscountRule discountRule,
        DateTime startsAtUtc,
        DateTime endsAtUtc,
        int? totalRedemptionLimit,
        int? perVerifiedPurchaserLimit)
    {
        ArgumentNullException.ThrowIfNull(scopeMetadata);
        return new PromotionDefinition(
            Guid.CreateVersion7(),
            1,
            scopeMetadata,
            displayLabel,
            eligibility ?? throw new ArgumentNullException(nameof(eligibility)),
            discountRule ?? throw new ArgumentNullException(nameof(discountRule)),
            startsAtUtc,
            endsAtUtc,
            totalRedemptionLimit,
            perVerifiedPurchaserLimit);
    }

    public void Publish(DateTime publishedAtUtc)
    {
        if ((PromotionDefinitionStatusEnum)PromotionDefinitionStatusId != PromotionDefinitionStatusEnum.Draft)
        {
            throw new InvalidOperationException("Only draft promotion definitions can be published.");
        }

        DateTime normalizedPublishedAt = EnsureUtc(publishedAtUtc, nameof(publishedAtUtc));
        if (!string.Equals(ScopeMetadata.CurrencyCode, DiscountRule.CurrencyCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Promotion discount currency must match its scope currency.");
        }

        PromotionDefinitionStatusId = (int)PromotionDefinitionStatusEnum.Published;
        PublishedAtUtc = normalizedPublishedAt;
    }

    public PromotionDefinition CreateRevision(
        string displayLabel,
        PromotionEligibility eligibility,
        PromotionDiscountRule discountRule,
        DateTime startsAtUtc,
        DateTime endsAtUtc,
        int? totalRedemptionLimit,
        int? perVerifiedPurchaserLimit)
    {
        if ((PromotionDefinitionStatusEnum)PromotionDefinitionStatusId != PromotionDefinitionStatusEnum.Published)
        {
            throw new InvalidOperationException("Only published promotion definitions can be revised.");
        }

        return new PromotionDefinition(
            DefinitionGroupId,
            checked(VersionNumber + 1),
            ScopeMetadata,
            displayLabel,
            eligibility ?? throw new ArgumentNullException(nameof(eligibility)),
            discountRule ?? throw new ArgumentNullException(nameof(discountRule)),
            startsAtUtc,
            endsAtUtc,
            totalRedemptionLimit,
            perVerifiedPurchaserLimit);
    }

    public void Revoke(DateTime decisionAtUtc, DateTime effectiveAtUtc)
    {
        if ((PromotionDefinitionStatusEnum)PromotionDefinitionStatusId != PromotionDefinitionStatusEnum.Published)
        {
            throw new InvalidOperationException("Only published promotion definitions can be revoked.");
        }

        DateTime normalizedDecisionAt = EnsureUtc(decisionAtUtc, nameof(decisionAtUtc));
        DateTime normalizedEffectiveAt = EnsureUtc(effectiveAtUtc, nameof(effectiveAtUtc));
        if (PublishedAtUtc.HasValue && normalizedEffectiveAt <= PublishedAtUtc.Value || normalizedEffectiveAt < normalizedDecisionAt)
        {
            throw new InvalidOperationException("Promotion revocation must be future-only relative to the revocation decision.");
        }

        PromotionDefinitionStatusId = (int)PromotionDefinitionStatusEnum.Revoked;
        RevokedAtUtc = normalizedEffectiveAt;
    }

    public void EnsureRedeemable(DateTime evaluatedAtUtc, int currentTotalRedemptions, int currentPurchaserRedemptions)
    {
        DateTime normalizedEvaluatedAt = EnsureUtc(evaluatedAtUtc, nameof(evaluatedAtUtc));
        if (currentTotalRedemptions < 0 || currentPurchaserRedemptions < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(currentTotalRedemptions));
        }

        if ((PromotionDefinitionStatusEnum)PromotionDefinitionStatusId != PromotionDefinitionStatusEnum.Published || normalizedEvaluatedAt < StartsAtUtc || normalizedEvaluatedAt >= EndsAtUtc ||
            RevokedAtUtc is not null && normalizedEvaluatedAt >= RevokedAtUtc.Value ||
            TotalRedemptionLimit is not null && currentTotalRedemptions >= TotalRedemptionLimit.Value ||
            PerVerifiedPurchaserLimit is not null && currentPurchaserRedemptions >= PerVerifiedPurchaserLimit.Value)
        {
            throw new InvalidOperationException("Promotion is not redeemable for the current order context.");
        }
    }

    private static string NormalizeLabel(string displayLabel)
    {
        if (string.IsNullOrWhiteSpace(displayLabel))
        {
            throw new ArgumentException("Promotion display label is required.", nameof(displayLabel));
        }

        return displayLabel.Trim();
    }

    private void ValidateWindowAndLimits(int? totalRedemptionLimit, int? perVerifiedPurchaserLimit)
    {
        if (EndsAtUtc <= StartsAtUtc || totalRedemptionLimit is <= 0 || perVerifiedPurchaserLimit is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalRedemptionLimit));
        }
    }

    private static DateTime EnsureUtc(DateTime value, string parameterName)
    {
        if (value == default || value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamp must be a non-default UTC value.", parameterName);
        }

        return value;
    }
}
