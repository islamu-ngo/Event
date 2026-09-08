using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Events;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.Client.Services;

public sealed class PrivateHomeOwnershipService(
    ILocationClient client,
    ILogger<PrivateHomeOwnershipService> logger) : IPrivateHomeOwnershipService
{
    public Task<BaseCommandResponseOfGuid> ClassifyAsPrivateHomeAsync(
        Guid locationId,
        Guid expectedConcurrencyStamp,
        PrivateHomeOwnershipConsentDto consent,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            "classify as private home",
            locationId,
            expectedConcurrencyStamp,
            consent,
            (stamp, body, token) => client.ClassifyLocationAsPrivateHomeAsync(
                locationId, body, stamp, cancellationToken: token),
            cancellationToken);

    public Task<BaseCommandResponseOfGuid> AcceptOwnershipAsync(
        Guid locationId,
        Guid expectedConcurrencyStamp,
        PrivateHomeOwnershipConsentDto consent,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            "accept private home ownership",
            locationId,
            expectedConcurrencyStamp,
            consent,
            (stamp, body, token) => client.AcceptPrivateHomeOwnershipAsync(
                locationId, body, stamp, cancellationToken: token),
            cancellationToken);

    private async Task<BaseCommandResponseOfGuid> SendAsync(
        string operation,
        Guid locationId,
        Guid expectedConcurrencyStamp,
        PrivateHomeOwnershipConsentDto consent,
        Func<string, PrivateHomeOwnershipConsentDto, CancellationToken, Task<BaseCommandResponseOfGuid>> send,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(consent);
        if (locationId == Guid.Empty || expectedConcurrencyStamp == Guid.Empty)
        {
            return new BaseCommandResponseOfGuid
            {
                Success = false,
                Message = "Location id and concurrency stamp are required."
            };
        }

        try
        {
            return await send($"\"{expectedConcurrencyStamp:D}\"", consent, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ApiException exception)
        {
            logger.LogError(
                exception,
                "[PRIVATE HOME SERVICE] API error trying to {Operation} for location {LocationId}: {StatusCode}",
                operation,
                locationId,
                exception.StatusCode);
            return new BaseCommandResponseOfGuid
            {
                Success = false,
                Message = $"API error: {exception.Message}",
                Errors = [exception.Response ?? exception.Message]
            };
        }
    }
}
