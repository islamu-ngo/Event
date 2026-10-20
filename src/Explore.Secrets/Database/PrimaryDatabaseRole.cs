namespace Explore.Secrets.Database;

public enum PrimaryDatabaseRole
{
    Runtime = 1,
    Migrator = 2,
    Global = 3,
    InstanceKeyRead = 4,
    InstanceKeyWrite = 5,
    InstanceKeyIssuance = 6,
    PlatformKeyReport = 7,
    EmailPolicyInstanceMutation = 8,
    EmailPolicyTenantMutation = 9,
    LocalIdentityWriter = 10,
}
