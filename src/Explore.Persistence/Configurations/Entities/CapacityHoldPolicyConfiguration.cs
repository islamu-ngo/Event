using Explore.Domain;

namespace Explore.Persistence.Configurations.Entities;

public sealed class CapacityHoldPolicyConfiguration : LookupConfiguration<CapacityHoldPolicy>
{
    protected override string TableName => "capacity_hold_policies";
}
