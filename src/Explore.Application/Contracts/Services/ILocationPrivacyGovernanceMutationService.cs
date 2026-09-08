using Explore.Domain.Settings;

namespace Explore.Application.Contracts.Services;

public sealed record LocationPrivacyProjectionIdentity(
    Guid TenantId,
    Guid EventId,
    Guid EventLocationId);

public sealed record LocationPrivacyGovernanceMutationResult(
    bool Accepted,
    string? Error,
    string? PreviousStoredValue,
    IReadOnlyList<LocationPrivacyProjectionIdentity> CorrectedProjections)
{
    public static LocationPrivacyGovernanceMutationResult Rejected(string error) =>
        new(false, error, null, []);
}

public interface ILocationPrivacyGovernanceMutationService
{
    bool Handles(string key);

    Task<string?> ValidateTenantValueAsync(
        string key,
        string proposedStoredValue,
        CancellationToken cancellationToken = default);

    Task<LocationPrivacyGovernanceMutationResult> ExecuteAsync(
        string key,
        string proposedStoredValue,
        SettingScope scope,
        Guid? tenantId,
        Guid actorUserId,
        Func<CancellationToken, Task<string?>> persist,
        CancellationToken cancellationToken = default);

    ValueTask InvalidateScopeAsync(
        SettingScope scope,
        Guid? tenantId,
        CancellationToken cancellationToken = default);

    Task InvalidateMutationAsync(
        SettingScope scope,
        Guid? tenantId,
        IReadOnlyList<LocationPrivacyProjectionIdentity> corrected,
        CancellationToken cancellationToken = default);
}
