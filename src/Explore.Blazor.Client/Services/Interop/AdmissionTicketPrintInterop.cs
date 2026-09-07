using Explore.Blazor.Client.Contracts.Interop;
using Microsoft.JSInterop;

namespace Explore.Blazor.Client.Services.Interop;

public sealed class AdmissionTicketPrintInterop(IJSRuntime jsRuntime) :
    IAdmissionTicketPrintInterop
{
    private const string ModulePath = "/js/admission-ticket-print.js";
    private IJSObjectReference? module;

    public async ValueTask PrintAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            module ??= await jsRuntime.InvokeAsync<IJSObjectReference>(
                "import",
                cancellationToken,
                ModulePath);
            await module.InvokeVoidAsync("printTicket", cancellationToken);
        }
        catch (Exception exception)
            when (exception is JSException
                or JSDisconnectedException
                or InvalidOperationException
                or OperationCanceledException)
        {
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
