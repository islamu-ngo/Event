using Explore.Domain;

namespace Explore.Persistence.Configurations.Entities;

public sealed class RegistrationOrderStatusConfiguration : LookupConfiguration<RegistrationOrderStatus>
{
    protected override string TableName => "registration_order_statuses";
}
