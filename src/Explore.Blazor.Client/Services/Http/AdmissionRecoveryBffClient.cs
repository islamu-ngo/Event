using System.Net.Http.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Admissions;

namespace Explore.Blazor.Client.Services.Http;

public sealed class AdmissionRecoveryBffClient(
    HttpClient httpClient,
    IApiClientExecutor executor) : IAdmissionRecoveryBffClient
{
    private const string RecoveryBridgeRoute = "/bff/admission-recovery/consume";

    public Task<ApiResult<AdmissionTicketRecoveryDeliveryDto>> ConsumeAsync(
        string capability,
        CancellationToken cancellationToken = default) =>
        executor.ReadJsonAsync<AdmissionTicketRecoveryDeliveryDto>(
            token => SendAsync(capability, token),
            "admission recovery BFF",
            cancellationToken);

    private async Task<HttpResponseMessage> SendAsync(
        string capability,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, RecoveryBridgeRoute)
        {
            Content = JsonContent.Create(new RecoveryBridgeRequest(capability))
        };
        return await httpClient.SendAsync(request, cancellationToken);
    }

    private sealed record RecoveryBridgeRequest(string Capability)
    {
        public override string ToString() => "RecoveryBridgeRequest(<redacted>)";
    }
}
