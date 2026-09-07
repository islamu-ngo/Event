using CommunityLexicon.Calendar;
using CommunityLexicon.Location;
using Explore.Application.Services.Federation;
using Explore.Domain.Federation;

namespace Explore.Infrastructure.Services.Federation;

internal static class AtprotoCalendarEventProjectionMapper
{
    public static AtprotoEventProjection Map(
        Event record,
        Guid atprotoRecordId,
        long sourceVersion,
        DateTime materializedAt)
    {
        return new AtprotoEventProjection
        {
            AtprotoRecordId = atprotoRecordId,
            Name = Bound(record.Name.Trim(), 240),
            Description = BoundNullable(record.Description, 4000),
            CreatedAt = record.CreatedAt.ToUniversalTime(),
            StartsAt = record.StartsAt?.ToUniversalTime(),
            EndsAt = record.EndsAt?.ToUniversalTime(),
            Mode = NormalizeToken(record.Mode),
            Status = NormalizeToken(record.Status),
            RsvpExpected = record.RsvpExpected,
            LocationSummary = BuildLocationSummary(record.Locations),
            SourceUrl = record.Uris?
                .Select(value => AtprotoExternalUriPolicy.Normalize(value.Uri))
                .FirstOrDefault(value => value is not null),
            SourceVersion = sourceVersion,
            MaterializedAt = materializedAt
        };
    }

    private static string? NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        int separator = value.LastIndexOf('#');
        return Bound((separator >= 0 ? value[(separator + 1)..] : value).Trim(), 80);
    }

    private static string? BuildLocationSummary(IReadOnlyCollection<IEventLocations>? locations)
    {
        string[] values = (locations ?? [])
            .Select(location => location switch
            {
                Address address => address.Name ?? address.Locality ?? address.Country,
                Geo geo => geo.Name,
                EventUri uri => uri.Name,
                _ => null
            })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return BoundNullable(string.Join(" · ", values), 500);
    }

    private static string Bound(string value, int maximumLength)
    {
        if (value.Length <= maximumLength)
        {
            return value;
        }

        int length = maximumLength;
        if (char.IsHighSurrogate(value[length - 1]) && char.IsLowSurrogate(value[length]))
        {
            length--;
        }

        return value[..length].TrimEnd();
    }

    private static string? BoundNullable(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Bound(value.Trim(), maximumLength);
}
