// ABOUTME: Adapts the effective email capability to the active tenant's SMTP transport.
// ABOUTME: Reuses hierarchical cache invalidation and never caches plaintext transport credentials.

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Models;
using Explore.Domain.Settings;

namespace Explore.Infrastructure.Mail;

public sealed class SmtpConfigResolver(
    EmailDeliveryCapabilityResolver resolver,
    ITenantContext tenantContext,
    IHierarchicalSettingsResolver settings) : ISmtpConfigResolver
{
    public async Task<SmtpConfiguration?> ResolveAsync(CancellationToken cancellationToken = default) =>
        (await resolver.ResolveTransportAsync(
            tenantContext.TenantId == Guid.Empty ? null : tenantContext.TenantId, cancellationToken)).Configuration;

    public void InvalidateCache(Guid? tenantId = null) =>
        settings.InvalidateCache(tenantId.HasValue ? SettingScope.Tenant : null, tenantId);
}
