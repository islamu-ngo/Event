using System.Net.Http.Json;
using System.Text.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Admissions;
using Explore.Blazor.Client.Services.Http;
using ISLAMU.Wire.Contracts.Admissions;

namespace Explore.Blazor.Client.Services.Admissions;

public sealed class AdmissionCheckInService : IAdmissionCheckInService
{
    private readonly HttpClient _staffHttpClient;
    private readonly AdmissionScannerHttpClient? _scannerHttpClient;
    private readonly AdmissionScannerCapabilityState? _capabilityState;
    private readonly IApiClientExecutor _executor;

    public AdmissionCheckInService(
        HttpClient staffHttpClient,
        AdmissionScannerHttpClient scannerHttpClient,
        AdmissionScannerCapabilityState capabilityState,
        IApiClientExecutor executor)
    {
        _staffHttpClient = staffHttpClient;
        _scannerHttpClient = scannerHttpClient;
        _capabilityState = capabilityState;
        _executor = executor;
    }

    // Retained for focused consumers that exercise only authenticated staff transport.
    public AdmissionCheckInService(HttpClient staffHttpClient, IApiClientExecutor executor)
    {
        _staffHttpClient = staffHttpClient;
        _executor = executor;
    }

    public async Task<AdmissionCheckInUiResult> CheckInAsync(
        Guid eventId,
        Guid targetId,
        AdmissionCredentialBearer credential,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(eventId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(targetId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(credential);

        ApiResult<AdmissionCheckInWireResult> response =
            await _executor.ReadJsonAsync<AdmissionCheckInWireResult>(
                token => SendAsync(eventId, targetId, credential, token),
                "admission check-in",
                cancellationToken);

        if (response.Exception is HttpRequestException transportFailure)
        {
            throw new HttpRequestException("Admission check-in is unavailable.", transportFailure);
        }

        AdmissionCheckInUiStatus status = response.StatusCode switch
        {
            System.Net.HttpStatusCode.ServiceUnavailable => AdmissionCheckInUiStatus.OnlineRequired,
            System.Net.HttpStatusCode.TooManyRequests => AdmissionCheckInUiStatus.Saturated,
            _ => AdmissionCheckInUiStatus.Completed
        };
        return new AdmissionCheckInUiResult
        {
            Status = status,
            Code = status == AdmissionCheckInUiStatus.Completed &&
                response.IsSuccess && response.Value is not null
                    ? PublicCode(response.Value.Outcome)
                    : AdmissionCheckInUiCodes.Rejected
        };
    }

    private Task<HttpResponseMessage> SendAsync(
        Guid eventId,
        Guid targetId,
        AdmissionCredentialBearer credential,
        CancellationToken cancellationToken) =>
        _capabilityState?.IsActive == true
            ? SendScannerAsync(credential, cancellationToken)
            : SendStaffAsync(eventId, targetId, credential, cancellationToken);

    private async Task<HttpResponseMessage> SendStaffAsync(
        Guid eventId,
        Guid targetId,
        AdmissionCredentialBearer credential,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/events/{eventId:D}/admission/check-ins")
        {
            Content = JsonContent.Create(new AdmissionCheckInRequestDto
            {
                TargetId = targetId,
                Credential = credential.Value
            })
        };
        return await _staffHttpClient.SendAsync(request, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendScannerAsync(
        AdmissionCredentialBearer credential,
        CancellationToken cancellationToken)
    {
        if (_scannerHttpClient is null)
        {
            throw new InvalidOperationException("Scanner capability transport is unavailable.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            AdmissionScannerCapabilityMessageHandler.CheckInPath)
        {
            Content = JsonContent.Create(new AdmissionScannerCheckInRequestDto
            {
                Credential = credential.Value
            })
        };
        return await _scannerHttpClient.SendAsync(request, cancellationToken);
    }

    private static string PublicCode(JsonElement outcome)
    {
        if (outcome.ValueKind == JsonValueKind.String)
        {
            return AdmissionCheckInUiCodes.Normalize(outcome.GetString());
        }

        if (outcome.ValueKind == JsonValueKind.Number && outcome.TryGetInt32(out int value))
        {
            return value switch
            {
                1 => AdmissionCheckInUiCodes.CheckedIn,
                2 => AdmissionCheckInUiCodes.AlreadyCheckedIn,
                _ => AdmissionCheckInUiCodes.Rejected
            };
        }

        return AdmissionCheckInUiCodes.Rejected;
    }

    private sealed record AdmissionCheckInWireResult(JsonElement Outcome);
}
