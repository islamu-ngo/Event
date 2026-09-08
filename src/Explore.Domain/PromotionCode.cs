using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class PromotionCode : ITenantEntity, IAuditableEntity
{
    private PromotionCode()
    {
    }

    private PromotionCode(PromotionDefinition definition, string maskedSuffix, PromotionScopeMetadata scopeMetadata)
    {
        Id = Guid.CreateVersion7();
        TenantId = definition.TenantId;
        PromotionDefinitionVersionId = definition.Id;
        ScopeMetadata = scopeMetadata;
        DisplayLabel = $"****{NormalizeSuffix(maskedSuffix)}";
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; set; }

    public Guid PromotionDefinitionVersionId { get; private set; }

    public PromotionScopeMetadata ScopeMetadata { get; private set; } = null!;

    public string DisplayLabel { get; private set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public static PromotionCode Create(PromotionDefinition definition, string maskedSuffix, PromotionScopeMetadata scopeMetadata)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(scopeMetadata);
        if (definition.PromotionDefinitionStatusId != (int)PromotionDefinitionStatusEnum.Published || definition.TenantId != scopeMetadata.TenantId || definition.ScopeMetadata != scopeMetadata)
        {
            throw new ArgumentException("Promotion code scope must match a published promotion definition.", nameof(scopeMetadata));
        }

        return new PromotionCode(definition, maskedSuffix, scopeMetadata);
    }

    private static string NormalizeSuffix(string maskedSuffix)
    {
        if (string.IsNullOrWhiteSpace(maskedSuffix))
        {
            throw new ArgumentException("Promotion code display suffix is required.", nameof(maskedSuffix));
        }

        string normalized = maskedSuffix.Trim().ToUpperInvariant();
        return normalized.Length <= 8 ? normalized : throw new ArgumentException("Promotion code display suffix is too long.", nameof(maskedSuffix));
    }
}
