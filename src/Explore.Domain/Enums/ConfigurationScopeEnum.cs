// Used by ConfigurationChangeLog to track where settings were modified in the hierarchy.

namespace Explore.Domain.Enums;

public enum ConfigurationScopeEnum
{
    System = 0,
    Instance = 1,
    Tenant = 2,
    Organization = 3,
    Group = 4,
    User = 5
}
