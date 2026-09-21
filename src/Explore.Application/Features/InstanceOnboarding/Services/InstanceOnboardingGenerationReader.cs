using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Features.InstanceOnboarding.Services;

public sealed class InstanceOnboardingGenerationReader(
    ISystemSettingRepository settings,
    IDeploymentModeProvider deploymentMode) : IInstanceOnboardingGenerationReader
{
    public async Task<string> ReadAsync(InstanceBootstrapState? bootstrap, CancellationToken cancellationToken)
    {
        var mode = bootstrap?.DeploymentMode
            ?? await deploymentMode.GetConfiguredOnboardingModeAsync(cancellationToken);
        var values = await settings.GetAllSettings(cancellationToken: cancellationToken);
        // Reserving a Local operation must not invalidate the generation admitted before reservation.
        // Row identity, timestamps and request-principal state do not change setup authority.
        var snapshot = new
        {
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
