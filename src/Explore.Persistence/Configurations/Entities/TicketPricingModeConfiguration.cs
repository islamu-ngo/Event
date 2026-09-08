using Explore.Domain;

namespace Explore.Persistence.Configurations.Entities;

public sealed class TicketPricingModeConfiguration : LookupConfiguration<TicketPricingMode>
{
    protected override string TableName => "ticket_pricing_modes";
}
