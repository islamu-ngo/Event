using System.Collections.Immutable;
using System.Globalization;
using CommunityLexicon.Calendar;
using CommunityLexicon.Location;
using Explore.Application.Features.Federation.Atproto.Models;
using Explore.Application.Features.Federation.Atproto.Services;

namespace Explore.Infrastructure.Services.Federation;

public static class AtprotoCalendarEventRecordMapper
{
    public static Event Map(AtprotoEventPublicationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new()
        {
            Name = snapshot.Name,
            Description = AtprotoEventDescriptionFormatter.Format(snapshot),
            CreatedAt = snapshot.CreatedAt.ToUniversalTime(),
            StartsAt = snapshot.StartsAt?.ToUniversalTime(),
            EndsAt = snapshot.EndsAt?.ToUniversalTime(),
            Mode = snapshot.Mode,
            Status = snapshot.Status,
            RsvpExpected = snapshot.RsvpExpected,
            Locations = MapLocations(snapshot.Locations),
            Uris = snapshot.Uris
                .Select(value => new EventUri { Uri = value.Uri, Name = value.Name })
                .ToList()
        };
    }

    private static List<IEventLocations>? MapLocations(
        ImmutableArray<AtprotoEventLocationSnapshot> locations)
    {
        var mapped = new List<IEventLocations>();
        foreach (AtprotoEventLocationSnapshot location in locations)
        {
            if (location.Country is { Length: >= 2 } country)
            {
                mapped.Add(new Address
                {
                    Country = country,
                    PostalCode = location.Postcode,
                    Locality = location.City,
                    Street = location.StreetAddress,
                    Name = location.RoomName ?? location.VenueName
                });
            }

            if (location.Latitude.HasValue && location.Longitude.HasValue)
            {
                mapped.Add(new Geo
                {
                    Latitude = location.Latitude.Value.ToString("R", CultureInfo.InvariantCulture),
                    Longitude = location.Longitude.Value.ToString("R", CultureInfo.InvariantCulture),
                    Name = location.RoomName ?? location.VenueName
                });
            }

            if (location.MapUri is not null)
            {
                mapped.Add(new EventUri { Uri = location.MapUri, Name = location.VenueName ?? "Map" });
            }
        }

        return mapped.Count == 0 ? null : mapped;
    }
}
