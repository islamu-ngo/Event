namespace Explore.Domain.Enums;

[Flags]
public enum EventLocationDisclosureFields
{
    None = 0,
    VenueName = 1 << 0,
    City = 1 << 1,
    Country = 1 << 2,
    RoomName = 1 << 3,
    StreetAddress = 1 << 4,
    Postcode = 1 << 5,
    Coordinates = 1 << 6,
    All = VenueName | City | Country | RoomName | StreetAddress | Postcode | Coordinates
}
