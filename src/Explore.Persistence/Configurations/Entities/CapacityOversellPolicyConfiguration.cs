using Explore.Domain;

namespace Explore.Persistence.Configurations.Entities;

public sealed class CapacityOversellPolicyConfiguration : LookupConfiguration<CapacityOversellPolicy>
{
    protected override string TableName => "capacity_oversell_policies";
}
