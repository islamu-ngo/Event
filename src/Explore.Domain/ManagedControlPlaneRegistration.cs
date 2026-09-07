using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public enum ManagedControlPlaneRegistrationStatus
{
    Pending,
    Registered,
    Revoked
}

public sealed class ManagedControlPlaneRegistration : IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid ManagedInstanceId { get; set; }
    public Guid EventInstanceId { get; set; }
    public required string ControlPlaneEndpoint { get; set; }
    public required string ManagementApiVersion { get; set; }
    public required string EventVersion { get; set; }
    public DeploymentMode DeploymentMode { get; set; }
    public required string RequestHash { get; set; }
    public required string EventToControlPlaneKeyId { get; set; }
    public required string EventToControlPlaneSecretHash { get; set; }
    public required string ControlPlaneToEventKeyId { get; set; }
    public required string ControlPlaneToEventSecretHash { get; set; }
    public Guid CredentialSecretBindingId { get; set; }
    public DateTime EventToControlPlaneCredentialExpiresAt { get; set; }
    public DateTime ControlPlaneToEventCredentialExpiresAt { get; set; }
    public ManagedControlPlaneRegistrationStatus Status { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public string? LastFailureCode { get; set; }
    public DateTime? RegisteredAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public uint RowVersion { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public void RecordAttempt(DateTime attemptedAt, string? failureCode)
    {
        if (Status != ManagedControlPlaneRegistrationStatus.Pending)
        {
            throw new InvalidOperationException("Only a pending managed registration can record an attempt.");
        }

        LastAttemptAt = EnsureUtc(attemptedAt, nameof(attemptedAt));
        LastFailureCode = NormalizeFailureCode(failureCode);
        UpdatedAt = LastAttemptAt;
    }

    public void MarkRegistered(DateTime registeredAt)
    {
        if (Status == ManagedControlPlaneRegistrationStatus.Registered)
        {
            return;
        }

        if (Status != ManagedControlPlaneRegistrationStatus.Pending)
        {
            throw new InvalidOperationException("A revoked managed registration cannot be registered.");
        }

        RegisteredAt = EnsureUtc(registeredAt, nameof(registeredAt));
        LastAttemptAt = RegisteredAt;
        LastFailureCode = null;
        Status = ManagedControlPlaneRegistrationStatus.Registered;
        UpdatedAt = RegisteredAt;
    }

    public void RotateControlPlaneCredential(string keyId, string secretHash, DateTime expiresAt, DateTime rotatedAt)
    {
        EnsureRegistered();
        ControlPlaneToEventKeyId = Require(keyId, 64, nameof(keyId));
        ControlPlaneToEventSecretHash = Require(secretHash, 500, nameof(secretHash));
        ControlPlaneToEventCredentialExpiresAt = EnsureFuture(expiresAt, rotatedAt, nameof(expiresAt));
        UpdatedAt = EnsureUtc(rotatedAt, nameof(rotatedAt));
    }

    public void Revoke(DateTime revokedAt)
    {
        EnsureRegistered();
        RevokedAt = EnsureUtc(revokedAt, nameof(revokedAt));
        Status = ManagedControlPlaneRegistrationStatus.Revoked;
        UpdatedAt = RevokedAt;
    }

    private void EnsureRegistered()
    {
        if (Status != ManagedControlPlaneRegistrationStatus.Registered)
        {
            throw new InvalidOperationException("An active managed registration is required.");
        }
    }

    private static string Require(string value, int maximumLength, string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > maximumLength)
        {
            throw new ArgumentException("A bounded non-empty value is required.", parameterName);
        }

        return normalized;
    }

    private static string? NormalizeFailureCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Require(value, 100, nameof(value));
    }

    private static DateTime EnsureFuture(DateTime value, DateTime comparedTo, string parameterName)
    {
        var normalized = EnsureUtc(value, parameterName);
        if (normalized <= EnsureUtc(comparedTo, nameof(comparedTo)))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return normalized;
    }

    private static DateTime EnsureUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("A UTC timestamp is required.", parameterName);
        }

        return value;
    }
}
