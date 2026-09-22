using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Constants;
using Explore.Application.Configuration;
using Microsoft.Extensions.Configuration;

namespace Explore.Application.Features.InstanceOnboarding.Services;

public sealed class InstanceOnboardingGenerationReader(
    ISystemSettingRepository settings,
    IDeploymentModeProvider deploymentMode,
    IInstanceBootstrapStateRepository bootstrapRepository,
    IConfiguration configuration) : IInstanceOnboardingGenerationReader
{
    public async Task<string> ReadCurrentAsync(CancellationToken cancellationToken) =>
        await ReadAsync(await bootstrapRepository.GetCurrent(cancellationToken), cancellationToken);

    public async Task<InstanceOnboardingDurableSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken)
    {
        var bootstrap = await bootstrapRepository.GetCurrent(cancellationToken);
        var mode = bootstrap?.DeploymentMode
            ?? await deploymentMode.GetConfiguredOnboardingModeAsync(cancellationToken);
        var values = await settings.GetAllSettings(cancellationToken: cancellationToken);
        var configuredUrl = PublicAddressResolver.ReadOverride(configuration);
        var generation = ComputeGeneration(bootstrap, mode, values, configuredUrl);
        var byKey = values.ToDictionary(setting => setting.SettingKey, StringComparer.Ordinal);
        return new(generation, new()
        {
            SiteName = ReadValue(byKey, GovernanceSettingKeys.Branding.DisplayName) ?? string.Empty,
            SupportEmail = ReadValue(byKey, GovernanceSettingKeys.Branding.SupportEmail),
            CanonicalUrl = PublicAddressResolver.IsValid(configuredUrl) ? configuredUrl
                : ReadValue(byKey, GovernanceSettingKeys.Domains.PublicBaseUrl),
            Locale = ReadValue(byKey, GovernanceSettingKeys.Localization.DefaultLanguage) ?? "en"
        });
    }

    public async Task<string> ReadAsync(InstanceBootstrapState? bootstrap, CancellationToken cancellationToken)
    {
        var mode = bootstrap?.DeploymentMode
            ?? await deploymentMode.GetConfiguredOnboardingModeAsync(cancellationToken);
        var values = await settings.GetAllSettings(cancellationToken: cancellationToken);
        return ComputeGeneration(bootstrap, mode, values,
            PublicAddressResolver.ReadOverride(configuration));
    }

    private static string? ReadValue(IReadOnlyDictionary<string, SystemSetting> settings, string key)
    {
        var value = settings.GetValueOrDefault(key)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : JsonSerializer.Deserialize<string>(value);
    }

    private static string ComputeGeneration(InstanceBootstrapState? bootstrap, DeploymentMode mode,
        IEnumerable<SystemSetting> values, string? configuredPublicUrl)
    {
        // Reserving a Local operation must not invalidate the generation admitted before reservation.
        // Row identity, timestamps and request-principal state do not change setup authority.
        var snapshot = new
        {
            PublicUrl = configuredPublicUrl,
            Status = bootstrap?.Status ?? InstanceBootstrapStatus.Pending,
            Mode = bootstrap?.Mode ?? InstanceBootstrapMode.Interactive,
            Provider = bootstrap?.ProviderKind,
            DeploymentMode = mode,
            Generation = bootstrap?.Generation ?? 1,
            bootstrap?.ConfigurationFingerprint,
            bootstrap?.SelectorFingerprint,
            Settings = values.OrderBy(setting => setting.SettingKey, StringComparer.Ordinal)
                .Select(setting => new { setting.SettingKey, setting.Value, setting.IsLocked }).ToArray()
        };
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(snapshot)));
    }
}
