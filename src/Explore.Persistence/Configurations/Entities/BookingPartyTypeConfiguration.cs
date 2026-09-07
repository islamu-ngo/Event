using Explore.Domain;

namespace Explore.Persistence.Configurations.Entities;

public sealed class BookingPartyTypeConfiguration : LookupConfiguration<BookingPartyType>
{
    protected override string TableName => "booking_party_types";
}
