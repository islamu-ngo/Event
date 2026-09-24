namespace Explore.Domain.Keycloak;

public enum KeycloakChangeKind
{
    CreateRealm = 0,
    CreateClient = 1,
    CreateMapper = 2,
    UpdateMapper = 3,
    UpdateRealm = 4,
    UpdateExistingClient = 5,
    ConvertClientKind = 6,
    RotateClientSecret = 7,
    UpdateUser = 8,
    UpdateRealmRole = 9,
    UpdateSharedClientScope = 10,
    GrantOfflineAccess = 11
}

public sealed class KeycloakOperationPolicy
{
    public bool IsAllowedOnExistingRealm(KeycloakChangeKind changeKind) =>
        changeKind is KeycloakChangeKind.CreateClient
            or KeycloakChangeKind.CreateMapper
            or KeycloakChangeKind.UpdateMapper;

    public bool HasConflictingClientIds(KeycloakInspectionSnapshot snapshot) =>
        !string.IsNullOrWhiteSpace(snapshot.ApiClientId)
        && string.Equals(
            snapshot.BlazorClientId.Trim(),
            snapshot.ApiClientId.Trim(),
            StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<KeycloakMapperSemantic> GetRequiredMapperRepairs(
        KeycloakInspectionSnapshot snapshot)
    {
        var repairs = new List<KeycloakMapperSemantic>(capacity: 2);
        bool hasSubject = snapshot.EffectiveMappers.Any(mapper =>
            mapper.IsEffective
            && mapper.Semantic == KeycloakMapperSemantic.Subject);
        if (!hasSubject)
        {
            repairs.Add(KeycloakMapperSemantic.Subject);
        }

        if (!string.IsNullOrWhiteSpace(snapshot.ApiClientId))
        {
            bool hasAudience = snapshot.EffectiveMappers.Any(mapper =>
                mapper.IsEffective
                && mapper.Semantic == KeycloakMapperSemantic.Audience
                && mapper.AddsToAccessToken
                && string.Equals(
                    mapper.Audience,
                    snapshot.ApiClientId,
                    StringComparison.Ordinal));
            if (!hasAudience)
            {
                repairs.Add(KeycloakMapperSemantic.Audience);
            }
        }

        return repairs;
    }

    public bool HasConflictingMapper(
        KeycloakInspectionSnapshot snapshot,
        KeycloakMapperSemantic semantic) =>
        snapshot.EffectiveMappers.Any(mapper =>
            mapper.Semantic == semantic && mapper.IsConflicting);
}
