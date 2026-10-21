using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

/// <summary>Returns detached management entities without fetching credential hashes or usage IP addresses.</summary>
internal static class ExternalApiKeyMetadataProjection
{
    internal static IQueryable<ExternalApiKey> Apply(IQueryable<ExternalApiKey> keys) => keys.AsNoTracking()
        .Select(key => new ExternalApiKey
        {
            Id = key.Id,
            TenantId = key.TenantId,
            Name = key.Name,
            Description = key.Description,
            KeyId = key.KeyId,
            SecretHash = string.Empty,
            Scopes = key.Scopes,
            ExternalApiKeyOwnerTypeId = key.ExternalApiKeyOwnerTypeId,
            OwnerId = key.OwnerId,
            ExternalApiKeyStatusId = key.ExternalApiKeyStatusId,
            ExternalApiKeyStatus = null!,
            ExternalApiKeyCreditPeriodId = key.ExternalApiKeyCreditPeriodId,
            ExternalApiKeyCreditPeriod = null!,
            CreditLimit = key.CreditLimit,
            MaxRolloverCredits = key.MaxRolloverCredits,
            ExpiresAt = key.ExpiresAt,
            LastUsedAt = key.LastUsedAt,
            CreatedAt = key.CreatedAt,
            CreatedBy = key.CreatedBy,
            UpdatedAt = key.UpdatedAt,
            UpdatedBy = key.UpdatedBy
        });
}
