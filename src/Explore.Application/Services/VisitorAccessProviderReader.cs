
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Models;
using Microsoft.Extensions.Configuration;

namespace Explore.Application.Services;

public sealed class VisitorAccessProviderReader(ISystemSettingRepository settings, IConfiguration configuration)
    : IVisitorAccessProviderReader
{
    public async Task<IReadOnlyList<VisitorAccessProviderState>> ReadProvidersAsync(
        Guid tenantId, CancellationToken cancellationToken = default) =>
        AuthProviderConfigurationService.ProjectVisitorProviders(
            (await settings.GetAllSettings(cancellationToken: cancellationToken))
                .ToDictionary(setting => setting.SettingKey, StringComparer.Ordinal), configuration);
}
