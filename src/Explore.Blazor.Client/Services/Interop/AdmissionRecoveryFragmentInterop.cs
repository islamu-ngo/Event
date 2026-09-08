using Explore.Blazor.Client.Contracts.Interop;
using Microsoft.JSInterop;

namespace Explore.Blazor.Client.Services.Interop;

public sealed class AdmissionRecoveryFragmentInterop(
    IJSRuntime jsRuntime,
    ILogger<AdmissionRecoveryFragmentInterop> logger) :
    IAdmissionRecoveryFragmentInterop
{
    private const string ModulePath = "/js/admission-recovery.js";
    private IJSObjectReference? module;

    public async ValueTask<string?> TakeCapabilityAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            module ??= await jsRuntime.InvokeAsync<IJSObjectReference>(
                "import",
                cancellationToken,
                ModulePath);
            return await module.InvokeAsync<string?>(
                "takeCapability",
                cancellationToken);
        }
        catch (Exception exception)
            when (exception is JSException
                or JSDisconnectedException
                or InvalidOperationException
                or OperationCanceledException)
        {
            logger.LogDebug("Admission recovery fragment interop was unavailable.");
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (module is null)
        {
            return;
        }

        try
        {
            await module.DisposeAsync();
        }
        catch (Exception exception)
            when (exception is JSDisconnectedException or OperationCanceledException)
        {
        }
    }
}
