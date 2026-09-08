namespace Explore.Application.Models;

public enum PublicExperiencePrimaryOrganizationState
{
    Available = 0,
    NotConfigured = 1,
    Missing = 2,
    Deleted = 3,
    HiddenOrInactive = 4,
    CrossTenantInvalid = 5,
    ActorUnavailable = 6
}
