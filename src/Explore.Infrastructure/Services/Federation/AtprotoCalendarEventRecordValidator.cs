using System.Collections.Immutable;
using System.Globalization;
using CommunityLexicon.Calendar;
using CommunityLexicon.Location;

namespace Explore.Infrastructure.Services.Federation;

public sealed record AtprotoCalendarRecordValidationResult(
    ImmutableArray<string> Errors,
    AtprotoEncodedSizeValidationResult? EncodedSize)
{
    public bool IsValid => Errors.IsEmpty && EncodedSize?.IsValid == true;
}

public static class AtprotoCalendarEventRecordValidator
{
    private static readonly HashSet<string> Modes =
    [
        "community.lexicon.calendar.event#hybrid",
        "community.lexicon.calendar.event#inperson",
        "community.lexicon.calendar.event#virtual"
    ];

    private static readonly HashSet<string> Statuses =
    [
        "community.lexicon.calendar.event#cancelled",
        "community.lexicon.calendar.event#planned",
        "community.lexicon.calendar.event#postponed",
        "community.lexicon.calendar.event#rescheduled",
        "community.lexicon.calendar.event#scheduled"
    ];

    public static AtprotoCalendarRecordValidationResult Validate(Event record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var errors = ImmutableArray.CreateBuilder<string>();
        if (string.IsNullOrWhiteSpace(record.Name))
        {
            errors.Add("The community event name is required.");
        }

        if (record.CreatedAt == default)
        {
            errors.Add("The community event createdAt value is required.");
        }

        if (record.StartsAt.HasValue && record.EndsAt.HasValue && record.EndsAt <= record.StartsAt)
        {
            errors.Add("The community event end must be after its start.");
        }

        if (record.Mode is not null && !Modes.Contains(record.Mode))
        {
            errors.Add("The community event mode is unsupported.");
        }

        if (record.Status is not null && !Statuses.Contains(record.Status))
        {
            errors.Add("The community event status is unsupported.");
        }

        foreach (EventUri uri in record.Uris ?? [])
        {
            ValidateUri(uri.Uri, "event URI", errors);
        }

        foreach (IEventLocations location in record.Locations ?? [])
        {
            switch (location)
            {
                case Address address when address.Country.Length is < 2 or > 10:
                    errors.Add("A community address country must contain 2 to 10 characters.");
                    break;
                case Geo geo:
                    ValidateCoordinate(geo.Latitude, -90, 90, "latitude", errors);
                    ValidateCoordinate(geo.Longitude, -180, 180, "longitude", errors);
                    break;
                case EventUri uri:
                    ValidateUri(uri.Uri, "location URI", errors);
                    break;
                case not Address and not Fsq and not Hthree:
                    errors.Add("The community event location type is unsupported.");
                    break;
            }
        }

        AtprotoEncodedSizeValidationResult? size = null;
        if (errors.Count == 0)
        {
            size = AtprotoRecordSizeValidator.Validate(record);
            errors.AddRange(size.Errors);
        }

        return new(errors.ToImmutable(), size);
    }

    private static void ValidateUri(
        string value,
        string label,
        ImmutableArray<string>.Builder errors)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
                && !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal))
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            errors.Add($"The {label} must be an absolute HTTP(S) URI without credentials or a fragment.");
        }
    }

    private static void ValidateCoordinate(
        string value,
        double minimum,
        double maximum,
        string label,
        ImmutableArray<string>.Builder errors)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            || !double.IsFinite(parsed)
            || parsed < minimum
            || parsed > maximum)
        {
            errors.Add($"The community location {label} is invalid.");
        }
    }
}
