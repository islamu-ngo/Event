using Explore.Application.Models.Storage;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Explore.Application.Features.TenantStorageSettings.Requests.Commands;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Closed original actions, not a provider name, setup flag or transferable write permit.</summary>
public interface IStorageProbeOriginalAction
{
    Task<FileStorageProviderStatus> ExecuteAsync(TestInstanceStorageProviderQuery query, CancellationToken token);
    Task<FileStorageProviderStatus> ExecuteAsync(TestInstanceStorageProviderDuringSetupQuery query, CancellationToken token);
    Task<FileStorageProviderStatus> ExecuteAsync(TestTenantStorageProviderCommand command, CancellationToken token);
}

/// <summary>
/// Pins the selected ordinary source owner. Source transactions finish before any external
/// provider work. This port grants no storage, configuration or accounting mutation.
/// </summary>
public interface IStorageProbeSourceOwner
{
    Task CaptureAsync(Guid? tenantId, CancellationToken token);
    void RequireIdle();
    Task CheckAsync(Func<CancellationToken, Task> sources, Action time, CancellationToken token);
}
