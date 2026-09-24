using Explore.Domain.Keycloak;

namespace Explore.Application.Contracts.Services;

public enum KeycloakInspectionStatus
{
    Inspected = 0,
    PublicOnly = 1,
    Unauthorized = 2,
    Unavailable = 3,
    InvalidTarget = 4,
    InvalidResponse = 5
}

public sealed class KeycloakAdminInspectionRequest
{
    private readonly string[] _requestedScopes;

    public KeycloakAdminInspectionRequest(
        Uri authority,
        string realm,
        string blazorClientId,
        string? apiClientId,
        string? administratorUsername = null,
        string? administratorPassword = null,
        IEnumerable<string>? requestedScopes = null)
    {
        Authority = authority;
        Realm = realm;
        BlazorClientId = blazorClientId;
        ApiClientId = apiClientId;
        AdministratorUsername = administratorUsername;
        AdministratorPassword = administratorPassword;
        _requestedScopes = requestedScopes?
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Select(scope => scope.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray()
            ?? ["openid", "profile", "email"];
    }

    public Uri Authority { get; }

    public string Realm { get; }

    public string BlazorClientId { get; }

    public string? ApiClientId { get; }

    public string? AdministratorUsername { get; }

    public string? AdministratorPassword { get; }

    public IReadOnlyList<string> RequestedScopes => _requestedScopes;

    public bool HasAdministratorCredentials =>
        !string.IsNullOrWhiteSpace(AdministratorUsername)
        && !string.IsNullOrWhiteSpace(AdministratorPassword);

    public override string ToString() =>
        $"{nameof(KeycloakAdminInspectionRequest)} {{ Authority = {Authority.GetLeftPart(UriPartial.Authority)}, HasAdministratorCredentials = {HasAdministratorCredentials} }}";
}

public sealed class KeycloakAdminInspectionResult
{
    private KeycloakAdminInspectionResult(
        KeycloakInspectionStatus status,
        KeycloakInspectionSnapshot? snapshot,
        string reasonCode)
    {
        Status = status;
        Snapshot = snapshot;
        ReasonCode = reasonCode;
    }

    public KeycloakInspectionStatus Status { get; }

    public KeycloakInspectionSnapshot? Snapshot { get; }

    public string ReasonCode { get; }

    public static KeycloakAdminInspectionResult Success(
        KeycloakInspectionStatus status,
        KeycloakInspectionSnapshot snapshot) =>
        status is KeycloakInspectionStatus.Inspected or KeycloakInspectionStatus.PublicOnly
            ? new(status, snapshot, string.Empty)
            : throw new ArgumentOutOfRangeException(nameof(status));

    public static KeycloakAdminInspectionResult Failure(
        KeycloakInspectionStatus status,
        string reasonCode) =>
        status is KeycloakInspectionStatus.Inspected or KeycloakInspectionStatus.PublicOnly
            ? throw new ArgumentOutOfRangeException(nameof(status))
            : new(status, null, reasonCode);
}

public interface IKeycloakAdminClient
{
    Task<KeycloakAdminInspectionResult> InspectAsync(
        KeycloakAdminInspectionRequest request,
        CancellationToken cancellationToken);

    Task<KeycloakMapperOperationResult> ApplyApprovedMapperAsync(
        KeycloakMapperOperationRequest request,
        CancellationToken cancellationToken);

    Task<KeycloakMapperOperationResult> InspectApprovedMapperAsync(
        KeycloakMapperOperationRequest request,
        CancellationToken cancellationToken);

    Task<KeycloakProvisioningOperationResult>
        ApplyApprovedProvisioningAsync(
            KeycloakProvisioningOperationRequest request,
            CancellationToken cancellationToken);

    Task<KeycloakProvisioningOperationResult>
        InspectApprovedProvisioningAsync(
            KeycloakProvisioningOperationRequest request,
            CancellationToken cancellationToken);
}
