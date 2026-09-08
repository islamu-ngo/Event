using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class WebhookConsumerProviderBindingRepository(ExploreDbContext dbContext)
    : IWebhookConsumerProviderBindingRepository
{
    public async Task<WebhookConsumerProviderBinding> CreateAsync(
        WebhookConsumerProviderBinding binding,
        CancellationToken cancellationToken)
    {
        await dbContext.WebhookConsumerProviderBindings.AddAsync(binding, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return binding;
    }

    public async Task<WebhookConsumerProviderBinding?> GetByConsumerAsync(
        Guid? tenantId,
        Guid webhookConsumerId,
        WebhookProviderKind providerKind,
        string providerEnvironment,
        CancellationToken cancellationToken)
    {
        var normalizedEnvironment = WebhookConsumerProviderBinding.NormalizeIdentity(
            providerEnvironment,
            nameof(providerEnvironment));

        return await BindingQuery()
            .SingleOrDefaultAsync(binding =>
                binding.TenantId == tenantId &&
                binding.WebhookConsumerId == webhookConsumerId &&
                binding.ProviderKindId == (int)providerKind &&
                binding.NormalizedEnvironment == normalizedEnvironment,
                cancellationToken);
    }

    public Task<WebhookConsumerProviderBinding?> GetByTenantAndIdForUpdateAsync(
        Guid? tenantId,
        Guid bindingId,
        CancellationToken cancellationToken) =>
        MutableBindingQuery()
            .SingleOrDefaultAsync(binding =>
                binding.TenantId == tenantId &&
                binding.Id == bindingId,
                cancellationToken);

    public async Task<WebhookConsumerProviderBinding?> GetVerifiedByConsumerAsync(
        Guid? tenantId,
        Guid webhookConsumerId,
        WebhookProviderKind providerKind,
        string providerEnvironment,
        CancellationToken cancellationToken)
    {
        var normalizedEnvironment = WebhookConsumerProviderBinding.NormalizeIdentity(
            providerEnvironment,
            nameof(providerEnvironment));

        return await BindingQuery()
            .SingleOrDefaultAsync(binding =>
                binding.TenantId == tenantId &&
                binding.WebhookConsumerId == webhookConsumerId &&
                binding.ProviderKindId == (int)providerKind &&
                binding.NormalizedEnvironment == normalizedEnvironment &&
                binding.VerificationStateId == (int)WebhookProviderBindingVerificationState.Verified &&
                binding.VerifiedTenantId == tenantId &&
                binding.VerifiedWebhookConsumerId == webhookConsumerId &&
                binding.IsEnabled,
                cancellationToken);
    }

    public async Task<WebhookConsumerProviderBinding?> GetVerifiedByProviderIdentityAsync(
        Guid tenantId,
        WebhookProviderKind providerKind,
        string providerEnvironment,
        string externalApplicationId,
        string applicationUid,
        CancellationToken cancellationToken)
    {
        var normalizedEnvironment = WebhookConsumerProviderBinding.NormalizeIdentity(
            providerEnvironment,
            nameof(providerEnvironment));
        var normalizedExternalApplicationId = WebhookConsumerProviderBinding.NormalizeIdentity(
            externalApplicationId,
            nameof(externalApplicationId));
        var normalizedApplicationUid = WebhookConsumerProviderBinding.NormalizeIdentity(
            applicationUid,
            nameof(applicationUid));

        return await BindingQuery()
            .SingleOrDefaultAsync(binding =>
                binding.TenantId == tenantId &&
                binding.ProviderKindId == (int)providerKind &&
                binding.NormalizedEnvironment == normalizedEnvironment &&
                binding.NormalizedExternalApplicationId == normalizedExternalApplicationId &&
                binding.NormalizedApplicationUid == normalizedApplicationUid &&
                binding.VerificationStateId == (int)WebhookProviderBindingVerificationState.Verified &&
                binding.VerifiedTenantId == tenantId &&
                binding.IsEnabled,
                cancellationToken);
    }

    public async Task<WebhookConsumerProviderBinding?> ResolveVerifiedProviderIdentityAsync(
        WebhookProviderKind providerKind,
        string providerEnvironment,
        string externalApplicationId,
        string applicationUid,
        CancellationToken cancellationToken)
    {
        var normalizedEnvironment = WebhookConsumerProviderBinding.NormalizeIdentity(
            providerEnvironment,
            nameof(providerEnvironment));
        var normalizedExternalApplicationId = WebhookConsumerProviderBinding.NormalizeIdentity(
            externalApplicationId,
            nameof(externalApplicationId));
        var normalizedApplicationUid = WebhookConsumerProviderBinding.NormalizeIdentity(
            applicationUid,
            nameof(applicationUid));

        return await dbContext.WebhookConsumerProviderBindings
            .IgnoreTenantFilter(TenantFilterBypassReasons.IncomingWebhookProviderAuthorityResolution)
            .AsNoTracking()
            .SingleOrDefaultAsync(binding =>
                binding.ProviderKindId == (int)providerKind &&
                binding.NormalizedEnvironment == normalizedEnvironment &&
                binding.NormalizedExternalApplicationId == normalizedExternalApplicationId &&
                binding.NormalizedApplicationUid == normalizedApplicationUid &&
                binding.VerificationStateId == (int)WebhookProviderBindingVerificationState.Verified &&
                binding.VerifiedTenantId == binding.TenantId &&
                binding.VerifiedWebhookConsumerId == binding.WebhookConsumerId &&
                binding.IsEnabled,
                cancellationToken);
    }

    public async Task<IReadOnlyList<WebhookConsumerProviderBinding>> GetVerifiedByConsumersAsync(
        Guid? tenantId,
        IReadOnlyCollection<Guid> webhookConsumerIds,
        WebhookProviderKind providerKind,
        string providerEnvironment,
        CancellationToken cancellationToken)
    {
        if (webhookConsumerIds.Count == 0)
        {
            return [];
        }

        var normalizedEnvironment = WebhookConsumerProviderBinding.NormalizeIdentity(
            providerEnvironment,
            nameof(providerEnvironment));
        var distinctConsumerIds = webhookConsumerIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        if (distinctConsumerIds.Length == 0)
        {
            return [];
        }

        return await BindingQuery()
            .Where(binding =>
                binding.TenantId == tenantId &&
                distinctConsumerIds.Contains(binding.WebhookConsumerId) &&
                binding.ProviderKindId == (int)providerKind &&
                binding.NormalizedEnvironment == normalizedEnvironment &&
                binding.VerificationStateId == (int)WebhookProviderBindingVerificationState.Verified &&
                binding.VerifiedTenantId == tenantId &&
                binding.VerifiedWebhookConsumerId == binding.WebhookConsumerId &&
                binding.IsEnabled)
            .ToListAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);

    private IQueryable<WebhookConsumerProviderBinding> BindingQuery() =>
        MutableBindingQuery().AsNoTracking();

    private IQueryable<WebhookConsumerProviderBinding> MutableBindingQuery() =>
        dbContext.WebhookConsumerProviderBindings
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation);
}
