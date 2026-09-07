namespace ISLAMU.Wire.Contracts.SetupLive;

using System.Text.Json.Serialization;

[JsonConverter(typeof(SetupEnrollmentScopeJsonConverter))]
public enum SetupEnrollmentScope
{
    [JsonStringEnumMemberName("target.read")]
    TargetRead,

    [JsonStringEnumMemberName("secret_binding.readiness")]
    SecretBindingReadiness,

    [JsonStringEnumMemberName("secret_binding.write")]
    SecretBindingWrite
}

[JsonConverter(typeof(SetupEnrollmentStateJsonConverter))]
public enum SetupEnrollmentState
{
    [JsonStringEnumMemberName("active")]
    Active,

    [JsonStringEnumMemberName("revoked")]
    Revoked,

    [JsonStringEnumMemberName("expired")]
    Expired
}

[JsonConverter(typeof(SetupEnrollmentIssuanceJsonConverter))]
public enum SetupEnrollmentIssuance
{
    [JsonStringEnumMemberName("issued")]
    Issued,

    [JsonStringEnumMemberName("already_issued")]
    AlreadyIssued
}

[JsonConverter(typeof(SetupSecretBindingReadinessStateJsonConverter))]
public enum SetupSecretBindingReadinessState
{
    [JsonStringEnumMemberName("unconfigured")]
    Unconfigured,

    [JsonStringEnumMemberName("ready")]
    Ready,

    [JsonStringEnumMemberName("unavailable")]
    Unavailable,

    [JsonStringEnumMemberName("unauthorized")]
    Unauthorized,

    [JsonStringEnumMemberName("invalid")]
    Invalid
}

[JsonConverter(typeof(SetupSecretBindingOperationStateJsonConverter))]
public enum SetupSecretBindingOperationState
{
    [JsonStringEnumMemberName("accepted")]
    Accepted,

    [JsonStringEnumMemberName("succeeded")]
    Succeeded,

    [JsonStringEnumMemberName("failed")]
    Failed,

    [JsonStringEnumMemberName("cancelled")]
    Cancelled
}

[JsonConverter(typeof(SetupSecretBindingOperationOutcomeJsonConverter))]
public enum SetupSecretBindingOperationOutcome
{
    [JsonStringEnumMemberName("accepted")]
    Accepted,

    [JsonStringEnumMemberName("ready")]
    Ready,

    [JsonStringEnumMemberName("unavailable")]
    Unavailable,

    [JsonStringEnumMemberName("unauthorized")]
    Unauthorized,

    [JsonStringEnumMemberName("invalid")]
    Invalid,

    [JsonStringEnumMemberName("cancelled")]
    Cancelled,

    [JsonStringEnumMemberName("unavailable_enrollment")]
    UnavailableEnrollment
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateSetupTargetEnrollmentRequest
{
    private IReadOnlyList<SetupEnrollmentScope> _requestedScopes =
        Array.Empty<SetupEnrollmentScope>();
    private SetupClientChallenge? _clientChallenge;

    public required SetupClientChallenge ClientChallenge
    {
        get => _clientChallenge
            ?? throw new InvalidOperationException(
                "Setup client challenge has not been initialized.");
        init => _clientChallenge = value
            ?? throw new ArgumentNullException(nameof(value));
    }

    [JsonConverter(typeof(SetupEnrollmentScopeListJsonConverter))]
    public required IReadOnlyList<SetupEnrollmentScope> RequestedScopes
    {
        get => _requestedScopes;
        init => _requestedScopes = SetupLiveSnapshot.ScopeList(value);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SetupTargetEnrollmentData
{
    private IReadOnlyList<SetupEnrollmentScope> _scopes =
        Array.Empty<SetupEnrollmentScope>();

    public required Guid EnrollmentId { get; init; }

    public required SetupEnrollmentState State { get; init; }

    public required long Generation { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    [JsonConverter(typeof(SetupEnrollmentScopeListJsonConverter))]
    public required IReadOnlyList<SetupEnrollmentScope> Scopes
    {
        get => _scopes;
        init => _scopes = SetupLiveSnapshot.ScopeList(value);
    }

    public required SetupEnrollmentIssuance Issuance { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SetupSecretBindingReadinessItem
{
    public required string BindingKey { get; init; }

    public required SetupSecretBindingReadinessState State { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SetupSecretBindingOperationData
{
    public required Guid OperationId { get; init; }

    public required SetupSecretBindingOperationState State { get; init; }

    public required SetupSecretBindingOperationOutcome Outcome { get; init; }

    public required long EnrollmentGeneration { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required DateTimeOffset? SettledAt { get; init; }
}

internal static class SetupLiveSnapshot
{
    internal static IReadOnlyList<SetupEnrollmentScope> ScopeList(
        IEnumerable<SetupEnrollmentScope>? source)
    {
        ArgumentNullException.ThrowIfNull(source);
        SetupEnrollmentScope[] snapshot = source.ToArray();
        if (snapshot.Length is < 1 or > 3
            || snapshot.Distinct().Count() != snapshot.Length
            || snapshot.Any(scope => !Enum.IsDefined(scope)))
        {
            throw new ArgumentException(
                "Setup enrollment scopes must be a non-empty unique closed set.",
                nameof(source));
        }

        return Array.AsReadOnly(snapshot);
    }
}
