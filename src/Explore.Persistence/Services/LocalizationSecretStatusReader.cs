using Explore.Application.Contracts.Persistence;
using Explore.Domain.Secrets;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Services;

/// <summary>Reads only binding existence on the original selected database owner.</summary>
public sealed class LocalizationSecretStatusReader(
    ExploreDbContext context, IServiceProvider services) : ILocalizationSecretStatusReader
{
    public async Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default)
    {
        context.ValidateTenantLease();
        if (context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner)
            throw new UnauthorizedAccessException("Localization status requires the ordinary selected scope.");
        if (context.Database.IsNpgsql())
            return await PostgresLocalizationSecretStatusContract.ReadAsync(context,
                services.GetRequiredService<PostgresNativeQueueEnrollment>(), cancellationToken);

        var contextId = context.ContextId;
        var tenant = context.TenantFilterTenantId;
        bool configured = await context.SecretBindings.AsNoTracking().AnyAsync(binding =>
            binding.SettingKey == SecretDefinitionRegistry.Keys.Localization.TmsApiKey
            && binding.Qualifier == string.Empty
            && (binding.SettingScopeId == 1 && binding.ScopeId == null
                || tenant != null && binding.SettingScopeId == 2 && binding.ScopeId == tenant),
            cancellationToken);
        context.ValidateTenantLease();
        if (context.ContextId != contextId || context.TenantFilterTenantId != tenant
            || context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner)
            throw new UnauthorizedAccessException("Localization status lost its original selected scope.");
        return configured;
    }
}
