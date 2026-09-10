namespace ISLAMU.Wire.Contracts.SetupLive;

using System.Text.Json;

internal static class SetupLiveEnumWire
{
    internal static bool TryParse(
        string? value,
        out SetupEnrollmentScope result)
    {
        result = value switch
        {
            "target.read" => SetupEnrollmentScope.TargetRead,
            "secret_binding.readiness" =>
                SetupEnrollmentScope.SecretBindingReadiness,
            "secret_binding.write" => SetupEnrollmentScope.SecretBindingWrite,
            _ => default
        };
        return value is "target.read"
            or "secret_binding.readiness"
            or "secret_binding.write";
    }

    internal static bool TryParse(
        string? value,
        out SetupEnrollmentState result)
    {
        result = value switch
        {
            "active" => SetupEnrollmentState.Active,
            "revoked" => SetupEnrollmentState.Revoked,
            "expired" => SetupEnrollmentState.Expired,
            _ => default
        };
        return value is "active" or "revoked" or "expired";
    }

    internal static bool TryParse(
        string? value,
        out SetupEnrollmentIssuance result)
    {
        result = value switch
        {
            "issued" => SetupEnrollmentIssuance.Issued,
            "already_issued" => SetupEnrollmentIssuance.AlreadyIssued,
            _ => default
        };
        return value is "issued" or "already_issued";
    }

    internal static bool TryParse(
        string? value,
        out SetupSecretBindingReadinessState result)
    {
        result = value switch
        {
            "unconfigured" => SetupSecretBindingReadinessState.Unconfigured,
            "ready" => SetupSecretBindingReadinessState.Ready,
            "unavailable" => SetupSecretBindingReadinessState.Unavailable,
            "unauthorized" => SetupSecretBindingReadinessState.Unauthorized,
            "invalid" => SetupSecretBindingReadinessState.Invalid,
            _ => default
        };
        return value is "unconfigured"
            or "ready"
            or "unavailable"
            or "unauthorized"
            or "invalid";
    }

    internal static bool TryParse(
        string? value,
        out SetupSecretBindingOperationState result)
    {
        result = value switch
        {
            "accepted" => SetupSecretBindingOperationState.Accepted,
            "succeeded" => SetupSecretBindingOperationState.Succeeded,
            "failed" => SetupSecretBindingOperationState.Failed,
            "cancelled" => SetupSecretBindingOperationState.Cancelled,
            _ => default
        };
        return value is "accepted" or "succeeded" or "failed" or "cancelled";
    }

    internal static bool TryParse(
        string? value,
        out SetupSecretBindingOperationOutcome result)
    {
        result = value switch
        {
            "accepted" => SetupSecretBindingOperationOutcome.Accepted,
            "ready" => SetupSecretBindingOperationOutcome.Ready,
            "unavailable" => SetupSecretBindingOperationOutcome.Unavailable,
            "unauthorized" => SetupSecretBindingOperationOutcome.Unauthorized,
            "invalid" => SetupSecretBindingOperationOutcome.Invalid,
            "cancelled" => SetupSecretBindingOperationOutcome.Cancelled,
            "unavailable_enrollment" =>
                SetupSecretBindingOperationOutcome.UnavailableEnrollment,
            _ => default
        };
        return value is "accepted"
            or "ready"
            or "unavailable"
            or "unauthorized"
            or "invalid"
            or "cancelled"
            or "unavailable_enrollment";
    }

    internal static string Format(SetupEnrollmentScope value) => value switch
    {
        SetupEnrollmentScope.TargetRead => "target.read",
        SetupEnrollmentScope.SecretBindingReadiness =>
            "secret_binding.readiness",
        SetupEnrollmentScope.SecretBindingWrite => "secret_binding.write",
        _ => throw new JsonException("Unknown Setup enrollment scope.")
    };

    internal static string Format(SetupEnrollmentState value) => value switch
    {
        SetupEnrollmentState.Active => "active",
        SetupEnrollmentState.Revoked => "revoked",
        SetupEnrollmentState.Expired => "expired",
        _ => throw new JsonException("Unknown Setup enrollment state.")
    };

    internal static string Format(SetupEnrollmentIssuance value) => value switch
    {
        SetupEnrollmentIssuance.Issued => "issued",
        SetupEnrollmentIssuance.AlreadyIssued => "already_issued",
        _ => throw new JsonException("Unknown Setup enrollment issuance.")
    };

    internal static string Format(
        SetupSecretBindingReadinessState value) => value switch
        {
            SetupSecretBindingReadinessState.Unconfigured => "unconfigured",
            SetupSecretBindingReadinessState.Ready => "ready",
            SetupSecretBindingReadinessState.Unavailable => "unavailable",
            SetupSecretBindingReadinessState.Unauthorized => "unauthorized",
            SetupSecretBindingReadinessState.Invalid => "invalid",
            _ => throw new JsonException("Unknown Setup binding readiness state.")
        };

    internal static string Format(
        SetupSecretBindingOperationState value) => value switch
        {
            SetupSecretBindingOperationState.Accepted => "accepted",
            SetupSecretBindingOperationState.Succeeded => "succeeded",
            SetupSecretBindingOperationState.Failed => "failed",
            SetupSecretBindingOperationState.Cancelled => "cancelled",
            _ => throw new JsonException("Unknown Setup binding operation state.")
        };

    internal static string Format(
        SetupSecretBindingOperationOutcome value) => value switch
        {
            SetupSecretBindingOperationOutcome.Accepted => "accepted",
            SetupSecretBindingOperationOutcome.Ready => "ready",
            SetupSecretBindingOperationOutcome.Unavailable => "unavailable",
            SetupSecretBindingOperationOutcome.Unauthorized => "unauthorized",
            SetupSecretBindingOperationOutcome.Invalid => "invalid",
            SetupSecretBindingOperationOutcome.Cancelled => "cancelled",
            SetupSecretBindingOperationOutcome.UnavailableEnrollment =>
                "unavailable_enrollment",
            _ => throw new JsonException("Unknown Setup binding operation outcome.")
        };
}
