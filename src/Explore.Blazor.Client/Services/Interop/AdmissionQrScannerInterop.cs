using System.Text.Json;
using ISLAMU.Wire.Contracts.Admissions;
using Explore.Blazor.Client.Contracts.Interop;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Explore.Blazor.Client.Services.Interop;

public sealed class AdmissionQrScannerInterop(
    IJSRuntime jsRuntime,
    ILogger<AdmissionQrScannerInterop> logger) : IAdmissionQrScanner
{
    private const string ModulePath = "/js/admission-qr-scanner.js";
    private IJSObjectReference? module;

    public async Task<AdmissionQrScannerCapability> GetCapabilityAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            AdmissionQrNativeResult? result = await (await GetModuleAsync(cancellationToken))
                .InvokeAsync<AdmissionQrNativeResult?>("getCapability", cancellationToken);
            return new AdmissionQrScannerCapability(result?.Status == AdmissionQrNativeStatus.Supported);
        }
        catch (Exception exception) when (IsExpectedInteropFailure(exception))
        {
            logger.LogDebug("Native admission QR capability detection was unavailable.");
            return new AdmissionQrScannerCapability(false);
        }
    }

    public async Task<AdmissionQrScanResult> DetectAsync(
        ElementReference imageSource,
        CancellationToken cancellationToken = default)
    {
        try
        {
            AdmissionQrNativeResult? result = await (await GetModuleAsync(cancellationToken))
                .InvokeAsync<AdmissionQrNativeResult?>("detect", cancellationToken, imageSource);
            if (result is null)
            {
                return new AdmissionQrScanResult(AdmissionQrScanOutcome.Failure);
            }

            return result.Status switch
            {
                AdmissionQrNativeStatus.Unsupported => new AdmissionQrScanResult(AdmissionQrScanOutcome.Unsupported),
                AdmissionQrNativeStatus.NoCode => new AdmissionQrScanResult(AdmissionQrScanOutcome.NoCode),
                AdmissionQrNativeStatus.Multiple => new AdmissionQrScanResult(AdmissionQrScanOutcome.MultipleAmbiguous),
                AdmissionQrNativeStatus.Single => ParseSingle(result.Value),
                _ => new AdmissionQrScanResult(AdmissionQrScanOutcome.Failure)
            };
        }
        catch (Exception exception) when (IsExpectedInteropFailure(exception))
        {
            logger.LogDebug("Native admission QR detection failed closed.");
            return new AdmissionQrScanResult(AdmissionQrScanOutcome.Failure);
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
        catch (Exception exception) when (exception is JSDisconnectedException or OperationCanceledException)
        {
        }
    }

    private static AdmissionQrScanResult ParseSingle(string? value)
    {
        if (!AdmissionQrPayloadCodec.TryDecode(value, out AdmissionQrPayload? payload))
        {
            return new AdmissionQrScanResult(AdmissionQrScanOutcome.Invalid);
        }

        return new AdmissionQrScanResult(AdmissionQrScanOutcome.SingleValid, payload!.Bearer);
    }

    private async Task<IJSObjectReference> GetModuleAsync(CancellationToken cancellationToken)
    {
        module ??= await jsRuntime.InvokeAsync<IJSObjectReference>("import", cancellationToken, ModulePath);
        return module;
    }

    private static bool IsExpectedInteropFailure(Exception exception) =>
        exception is JSException or JSDisconnectedException or JsonException or
            InvalidOperationException or OperationCanceledException;
}
