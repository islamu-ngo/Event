namespace Explore.Application.Notifications;

public enum AccountAuthorityKind
{
    None = 0,
    Keycloak = 1,
    AtprotoPds = 2,
    IslamuOperatedPds = 3,
    LocalIdentity = 4,
    ExternalOidc = 5
}
