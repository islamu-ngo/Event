using System.Text.Json.Serialization;
using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Secrets;

/// <summary>
/// Used only by an explicitly authorized credential operation, never ordinary settings updates.
/// Eligibility does not certify remote permission. A confirmed source write does not certify
/// activation, and an uncertain completion must not be retried or rolled back automatically.
/// </summary>
public interface IAuthenticationRuntimeSecretWriter
{
    Task<AuthenticationRuntimeWriteCapability> GetWriteCapabilityAsync(
        AuthenticationRuntimeSecretTarget target, CancellationToken cancellationToken = default);

    Task<AuthenticationRuntimeSecretWriteResult> WriteAsync(
        AuthenticationRuntimeSecretWriteRequest request, CancellationToken cancellationToken = default);
}

public enum AuthenticationRuntimeSecretTarget { GoogleClientSecret, KeycloakBffClientSecret }

public enum AuthenticationRuntimeWriteOutcome
{
    ReadyToAttempt,
    Written,
    OperatorInjectionRequired,
    Missing,
    Invalid,
    Unauthorized,
    Unavailable
}

public enum AuthenticationRuntimeSourceMutation { NotAttempted, ConfirmedWritten, Unknown }

/// <summary>Internal expected-selection guard; not an authorization token or distributed lease.</summary>
public sealed record AuthenticationRuntimeWriteSelection(
    AuthenticationRuntimeSecretTarget Target, Guid BindingId, SecretSourceType Source,
    string Environment, string Path, string Key, string AuthorityUrl, string ProjectId);

public sealed record AuthenticationRuntimeWriteCapability(
    AuthenticationRuntimeWriteOutcome Outcome,
    AuthenticationRuntimeWriteSelection? Selection = null,
    string? OperatorVariable = null);

/// <summary>Transient write-only input. The caller owns and clears the credential buffer.</summary>
public sealed class AuthenticationRuntimeSecretWriteRequest(
    AuthenticationRuntimeWriteSelection selection, ReadOnlyMemory<byte> credential)
{
    public AuthenticationRuntimeWriteSelection Selection { get; } = selection;

    [JsonIgnore]
    public ReadOnlyMemory<byte> Credential { get; } = credential;

    public override string ToString() => "authentication_runtime_secret_write";
}

public sealed record AuthenticationRuntimeSecretWriteResult(
    AuthenticationRuntimeWriteOutcome Outcome, AuthenticationRuntimeSourceMutation Mutation,
    string? OperatorVariable = null);
