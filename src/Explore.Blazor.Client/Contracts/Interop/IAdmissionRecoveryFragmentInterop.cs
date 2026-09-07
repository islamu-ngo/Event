namespace Explore.Blazor.Client.Contracts.Interop;

public interface IAdmissionRecoveryFragmentInterop : IAsyncDisposable
{
    ValueTask<string?> TakeCapabilityAsync(CancellationToken cancellationToken = default);
}
