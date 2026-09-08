namespace ISLAMU.Wire.Contracts.SetupLive;

public static class SetupLiveContractMetadata
{
    public const string CapabilityHeader = "X-Setup-Enrollment-Capability";
    public const string IdempotencyHeader = "Idempotency-Key";
    public const string CreateRequestMediaType = "application/json";
    public const string SecretWriteRequestMediaType = "application/octet-stream";
    public const string SuccessMediaType = "application/hal+json";
    public const string ErrorMediaType = "application/problem+json";
    public const string EnrollmentWriteRatePolicy = "SetupEnrollmentWrite";
    public const string SecretWriteRatePolicy = "SetupSecretBindingWrite";
    public const string EnrollmentTimeoutPolicy = "SetupEnrollment";
    public const string SecretWriteTimeoutPolicy = "SetupSecretBinding";
}

public static class SetupLiveContentLimits
{
    public const int MaximumCreateRequestBytes = 16_384;
    public const int MaximumSecretWriteBytes = 65_536;
}

public static class SetupLiveHalRelations
{
    public const string CreateSetupEnrollment = "create-setup-enrollment";
    public const string Self = "self";
    public const string Revoke = "revoke";
    public const string RotateCapability = "rotate-capability";
    public const string SecretBindingReadiness = "secret-binding-readiness";
    public const string WriteSecretBinding = "write-secret-binding";
    public const string SecretBindingOperation = "secret-binding-operation";
}

public static class SetupLiveProblemContracts
{
    public const int UnavailableStatus = 404;
    public const string UnavailableType =
        "/problems/setup-enrollment-unavailable";
    public const string UnavailableTitle = "Setup enrollment unavailable";
    public const string UnavailableCode = "setup_enrollment_unavailable";
    public const string UnavailableDetail =
        "The requested setup enrollment is unavailable.";

    public const int IdempotencyConflictStatus = 409;
    public const string IdempotencyConflictType =
        "/problems/setup-enrollment-idempotency-conflict";
    public const string IdempotencyConflictTitle =
        "Setup enrollment request conflicts with an existing operation";
    public const string IdempotencyConflictCode =
        "setup_enrollment_idempotency_conflict";
    public const string IdempotencyConflictDetail =
        "The idempotency key is already bound to different setup enrollment input.";
}
