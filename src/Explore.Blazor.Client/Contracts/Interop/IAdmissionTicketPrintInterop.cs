namespace Explore.Blazor.Client.Contracts.Interop;

public interface IAdmissionTicketPrintInterop : IAsyncDisposable
{
    ValueTask PrintAsync(CancellationToken cancellationToken = default);
}
