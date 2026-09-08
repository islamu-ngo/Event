using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Models;
using Explore.Domain.Settings;

namespace Explore.Infrastructure.Mail;

public sealed class SmtpConfigResolver(
    EmailDeliveryCapabilityResolver resolver,
    ITenantContext tenantContext,
    IHierarchicalSettingsResolver settings) : ISmtpConfigResolver
{
    public Task<SmtpConfiguration?> ResolveAsync(CancellationToken cancellationToken = default) =>
        ResolveAsync(tenantContext.TenantId == Guid.Empty ? null : tenantContext.TenantId, cancellationToken);

    public async Task<SmtpConfiguration?> ResolveAsync(Guid? tenantId, CancellationToken cancellationToken = default) =>
        (await resolver.ResolveTransportAsync(tenantId, cancellationToken)).Configuration;

    public void InvalidateCache(Guid? tenantId = null) =>
        settings.InvalidateCache(tenantId.HasValue ? SettingScope.Tenant : null, tenantId);
}
