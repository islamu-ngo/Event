using Explore.Domain;

namespace Explore.Persistence.Configurations.Entities;

public sealed class RegistrationInventoryHoldStatusConfiguration : LookupConfiguration<RegistrationInventoryHoldStatus>
{
    protected override string TableName => "registration_inventory_hold_statuses";
}
