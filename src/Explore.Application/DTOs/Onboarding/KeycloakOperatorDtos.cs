using System.Text.Json.Serialization;

namespace Explore.Application.DTOs.Onboarding;

/// <summary>Public, credential-free effective Keycloak binding.</summary>
public sealed record KeycloakConnectionDto(
    string Status,
    string? Authority,
    string? Realm,
    string? ClientId,
    bool Available,
    string CredentialOwnership,
    string CredentialStatus,
    bool RequiresCoordinatedRestart,
    string OperatorGuidance);

/// <summary>Public projection of a provider inspection. Raw provider payloads are never exposed.</summary>
public sealed record KeycloakInspectionDto(
    string Status,
    string? ReasonCode,
    string? Authority,
    string? Realm,
    string? ClientId,
    IReadOnlyList<string> Findings);

public sealed record KeycloakStepOutcomeDto(
    string StepId,
    string Outcome,
    string? ProviderResourceId,
    string? ObservedFingerprint);

/// <summary>Private, server-authored operation receipt projection.</summary>
public sealed record KeycloakOperationDto(
    Guid Id,
    string State,
    string Digest,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? SettledAtUtc,
    bool IsCancellationRequested,
    IReadOnlyList<string> Steps,
    IReadOnlyList<KeycloakStepOutcomeDto> Outcomes);

public enum KeycloakOperationIntent
{
    RepairClient,
    CreateClients,
    CreateRealm
}

/// <summary>Write-only inspection credentials. This type must never be returned or logged.</summary>
public class KeycloakInspectionCredentials
{
    [JsonPropertyName("administratorUsername")]
    public string? AdministratorUsername { get; init; }

    [JsonPropertyName("administratorPassword")]
    public string? AdministratorPassword { get; init; }

    public override string ToString() => GetType().Name;
}

/// <summary>Closed reviewed intent plus fresh inspection credentials.</summary>
public sealed class KeycloakOperationPlanInput : KeycloakInspectionCredentials
{
    [JsonPropertyName("intent")]
    public KeycloakOperationIntent? Intent { get; init; }
}

/// <summary>Write-only credentials used to apply or reconcile a persisted receipt.</summary>
public sealed class KeycloakOperationCredentials : KeycloakInspectionCredentials;
