using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ISLAMU.Wire.Contracts.Admissions;
using Microsoft.AspNetCore.Components;

namespace Explore.Blazor.Client.Contracts.Interop;

public interface IAdmissionQrScanner : IAsyncDisposable
{
    Task<AdmissionQrScannerCapability> GetCapabilityAsync(CancellationToken cancellationToken = default);
    Task<AdmissionQrScanResult> DetectAsync(ElementReference imageSource, CancellationToken cancellationToken = default);
}

public sealed record AdmissionQrScannerCapability(
    bool NativeQrAvailable,
    bool HidInputAvailable = true,
    bool ManualInputAvailable = true);

[JsonConverter(typeof(AdmissionQrNativeStatusJsonConverter))]
public enum AdmissionQrNativeStatus
{
    Unknown,
    Supported,
    Unsupported,
    NoCode,
    Single,
    Multiple,
    Failure
}

public sealed class AdmissionQrNativeStatusJsonConverter : JsonConverter<AdmissionQrNativeStatus>
{
    public override AdmissionQrNativeStatus Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Admission QR native status must be a string.");
        }

        return reader.GetString() switch
        {
            "supported" => AdmissionQrNativeStatus.Supported,
            "unsupported" => AdmissionQrNativeStatus.Unsupported,
            "noCode" => AdmissionQrNativeStatus.NoCode,
            "single" => AdmissionQrNativeStatus.Single,
            "multiple" => AdmissionQrNativeStatus.Multiple,
            "failure" => AdmissionQrNativeStatus.Failure,
            _ => throw new JsonException("Admission QR native status is unknown.")
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        AdmissionQrNativeStatus value,
        JsonSerializerOptions options)
    {
        string serialized = value switch
        {
            AdmissionQrNativeStatus.Supported => "supported",
            AdmissionQrNativeStatus.Unsupported => "unsupported",
            AdmissionQrNativeStatus.NoCode => "noCode",
            AdmissionQrNativeStatus.Single => "single",
            AdmissionQrNativeStatus.Multiple => "multiple",
            AdmissionQrNativeStatus.Failure => "failure",
            _ => throw new JsonException("Admission QR native status is unknown.")
        };
        writer.WriteStringValue(serialized);
    }
}

[DebuggerDisplay("AdmissionQrNativeResult(status={Status}, <redacted>)")]
public sealed record AdmissionQrNativeResult(
    [property: JsonPropertyName("status")] AdmissionQrNativeStatus Status,
    [property: JsonPropertyName("value")] string? Value = null)
{
    public override string ToString() => $"AdmissionQrNativeResult(status={Status}, <redacted>)";
}

public enum AdmissionQrScanOutcome
{
    Unsupported,
    NoCode,
    SingleValid,
    MultipleAmbiguous,
    Invalid,
    Failure
}

[DebuggerDisplay("AdmissionQrScanResult(outcome={Outcome}, <redacted>)")]
public sealed record AdmissionQrScanResult(
    AdmissionQrScanOutcome Outcome,
    AdmissionCredentialBearer? Credential = null)
{
    public override string ToString() => $"AdmissionQrScanResult(outcome={Outcome}, <redacted>)";
}
