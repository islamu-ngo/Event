namespace Explore.Domain.Keycloak;

public enum KeycloakMapperSemantic
{
    Subject = 0,
    Audience = 1
}

public enum KeycloakMapperOrigin
{
    Native = 0,
    Direct = 1,
    Inherited = 2
}

public sealed record KeycloakEffectiveMapperSnapshot(
    string ProviderId,
    KeycloakMapperSemantic Semantic,
    KeycloakMapperOrigin Origin,
    string? Audience,
    bool AddsToAccessToken,
    bool AddsToIdToken,
    bool IsEffective,
    bool IsConflicting = false,
    string? Name = null,
    string? Protocol = null,
    string? MapperType = null,
    string? ClaimName = null);

public sealed record KeycloakClientShape(
    bool Enabled,
    bool BearerOnly,
    bool PublicClient,
    bool StandardFlowEnabled,
    bool DirectAccessGrantsEnabled,
    bool ServiceAccountsEnabled);

/// <summary>A value-free exact-name lookup result. Client secrets are never inspected.</summary>
public sealed record KeycloakClientInspectionSnapshot(
    string ClientId,
    int ExactMatchCount,
    string? ProviderId,
    KeycloakClientShape? Shape)
{
    public bool IsProvenAbsent => ExactMatchCount == 0;
    public bool IsUnambiguous => ExactMatchCount == 1 && !string.IsNullOrWhiteSpace(ProviderId);
}

public sealed record KeycloakInspectionSnapshot
{
    private readonly KeycloakEffectiveMapperSnapshot[] _effectiveMappers;

    public KeycloakInspectionSnapshot(
        string realm,
        string blazorClientId,
        string? apiClientId,
        bool realmExists,
        IEnumerable<KeycloakEffectiveMapperSnapshot>? effectiveMappers = null,
        KeycloakClientInspectionSnapshot? blazorClient = null,
        KeycloakClientInspectionSnapshot? apiClient = null)
    {
        Realm = realm;
        BlazorClientId = blazorClientId;
        ApiClientId = apiClientId;
        RealmExists = realmExists;
        BlazorClient = blazorClient ?? new(
            blazorClientId,
            realmExists ? 1 : 0,
            realmExists ? blazorClientId : null,
            realmExists ? new(true, false, false, true, false, false) : null);
        ApiClient = string.IsNullOrWhiteSpace(apiClientId)
            ? null
            : apiClient ?? new(
                apiClientId,
                realmExists ? 1 : 0,
                realmExists ? apiClientId : null,
                realmExists ? new(true, true, false, false, false, false) : null);
        _effectiveMappers = effectiveMappers?.ToArray() ?? [];
    }

    public string Realm { get; }
    public string BlazorClientId { get; }
    public string? ApiClientId { get; }
    public bool RealmExists { get; }
    public KeycloakClientInspectionSnapshot BlazorClient { get; }
    public KeycloakClientInspectionSnapshot? ApiClient { get; }
    public IReadOnlyList<KeycloakEffectiveMapperSnapshot> EffectiveMappers => _effectiveMappers;
}
