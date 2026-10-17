
using Explore.Application.Contracts.Services;
using Explore.Application.Models;
using Explore.Domain;
using Microsoft.Extensions.Configuration;

namespace Explore.Application.Services;

public sealed class VisitorAccessProviderReader(IConfiguration configuration)
    : IVisitorAccessProviderReader
{
    public IReadOnlyList<VisitorAccessProviderState> ReadProviders(IReadOnlyList<SystemSetting> instanceSettings) =>
        AuthProviderConfigurationService.ProjectVisitorProviders(
            instanceSettings.ToDictionary(setting => setting.SettingKey, StringComparer.Ordinal), configuration);
}
