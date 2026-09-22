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
    bool IsConflicting = false);

public sealed record KeycloakInspectionSnapshot
{
    private readonly KeycloakEffectiveMapperSnapshot[] _effectiveMappers;

    public KeycloakInspectionSnapshot(
        string realm,
        string blazorClientId,
        string? apiClientId,
        bool realmExists,
        IEnumerable<KeycloakEffectiveMapperSnapshot>? effectiveMappers = null)
    {
        Realm = realm;
        BlazorClientId = blazorClientId;
        ApiClientId = apiClientId;
        RealmExists = realmExists;
        _effectiveMappers = effectiveMappers?.ToArray() ?? [];
    }

    public string Realm { get; }

    public string BlazorClientId { get; }

    public string? ApiClientId { get; }

    public bool RealmExists { get; }

    public IReadOnlyList<KeycloakEffectiveMapperSnapshot> EffectiveMappers => _effectiveMappers;
}
