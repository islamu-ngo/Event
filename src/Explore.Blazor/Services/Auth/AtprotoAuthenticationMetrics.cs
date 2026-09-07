using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Explore.Blazor.Services.Auth;

public sealed class AtprotoAuthenticationMetrics
{
    private static readonly Meter Meter = new("Explore.Business", "1.0.0");
    private static readonly Counter<long> Operations = Meter.CreateCounter<long>(
        "atproto.authentication.operations",
        unit: "{operation}",
        description: "AT Protocol authentication operation outcomes");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
        "atproto.authentication.duration",
        unit: "s",
        description: "AT Protocol authentication operation duration");

    public void Record(
        AtprotoAuthenticationOperation operation,
        AtprotoAuthenticationOutcome outcome,
        TimeSpan duration)
    {
        var tags = new TagList
        {
            { "operation", OperationLabel(operation) },
            { "outcome", OutcomeLabel(outcome) }
        };
        Operations.Add(1, tags);
        Duration.Record(Math.Max(0, duration.TotalSeconds), tags);
    }

    public void RecordReadiness(bool isReady, string? failureCode, TimeSpan duration)
    {
        Record(
            AtprotoAuthenticationOperation.Readiness,
            isReady ? AtprotoAuthenticationOutcome.Success : OutcomeFromFailureCode(failureCode),
            duration);
    }

    internal static string NormalizeFailureCode(string? failureCode) => failureCode switch
    {
        null => "none",
        "invalid_public_url_or_callback" => failureCode,
        "key_ring_unavailable" => failureCode,
        "state_store_unavailable" => failureCode,
        "session_store_unavailable" => failureCode,
        "provider_not_configured" => failureCode,
        _ => "other"
    };

    private static AtprotoAuthenticationOutcome OutcomeFromFailureCode(string? failureCode) => failureCode switch
    {
        "invalid_public_url_or_callback" => AtprotoAuthenticationOutcome.ValidationFailed,
        "key_ring_unavailable" => AtprotoAuthenticationOutcome.KeyMissing,
        "state_store_unavailable" or "session_store_unavailable" or "provider_not_configured" =>
            AtprotoAuthenticationOutcome.ProviderUnavailable,
        _ => AtprotoAuthenticationOutcome.InternalFailure
    };

    private static string OperationLabel(AtprotoAuthenticationOperation operation) => operation switch
    {
        AtprotoAuthenticationOperation.Readiness => "readiness",
        AtprotoAuthenticationOperation.Challenge => "challenge",
        AtprotoAuthenticationOperation.Callback => "callback",
        AtprotoAuthenticationOperation.BridgeVerification => "bridge_verification",
        AtprotoAuthenticationOperation.Refresh => "refresh",
        AtprotoAuthenticationOperation.Revoke => "revoke",
        _ => "unknown"
    };

    private static string OutcomeLabel(AtprotoAuthenticationOutcome outcome) => outcome switch
    {
        AtprotoAuthenticationOutcome.Success => "success",
        AtprotoAuthenticationOutcome.ValidationFailed => "validation_failed",
        AtprotoAuthenticationOutcome.StateReplay => "state_replay",
        AtprotoAuthenticationOutcome.DidMismatch => "did_mismatch",
        AtprotoAuthenticationOutcome.PdsUnavailable => "pds_unavailable",
        AtprotoAuthenticationOutcome.TokenInvalid => "token_invalid",
        AtprotoAuthenticationOutcome.KeyMissing => "key_missing",
        AtprotoAuthenticationOutcome.ReauthenticationRequired => "reauth_required",
        AtprotoAuthenticationOutcome.ProviderUnavailable => "provider_unavailable",
        AtprotoAuthenticationOutcome.Cancelled => "cancelled",
        _ => "internal_failure"
    };
}

public enum AtprotoAuthenticationOperation
{
    Readiness,
    Challenge,
    Callback,
    BridgeVerification,
    Refresh,
    Revoke
}

public enum AtprotoAuthenticationOutcome
{
    Success,
    ValidationFailed,
    StateReplay,
    DidMismatch,
    PdsUnavailable,
    TokenInvalid,
    KeyMissing,
    ReauthenticationRequired,
    ProviderUnavailable,
    Cancelled,
    InternalFailure
}
