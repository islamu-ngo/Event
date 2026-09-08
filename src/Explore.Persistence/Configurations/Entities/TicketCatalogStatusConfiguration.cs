using Explore.Domain;

namespace Explore.Persistence.Configurations.Entities;

public sealed class TicketCatalogStatusConfiguration : LookupConfiguration<TicketCatalogStatus>
{
    protected override string TableName => "ticket_catalog_statuses";
}
