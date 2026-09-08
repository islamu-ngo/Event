namespace Explore.Blazor.Client.Pages.Events.Workflows;

public sealed class TimezoneWorkflow
{
    private static readonly IReadOnlyList<TimeZoneInfo> AllTimezones = TimeZoneInfo.GetSystemTimeZones();

    public TimeZoneInfo SelectedTimezone { get; private set; } = TimeZoneInfo.Local;

    public string SelectedTimezoneDisplay => FormatTimezoneShort(SelectedTimezone);

    public Task<IEnumerable<TimeZoneInfo>> SearchAsync(string? searchText, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(searchText))
        {
            return Task.FromResult(AllTimezones.AsEnumerable());
        }

        var results = AllTimezones
            .Where(timezone => timezone.DisplayName.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                           || timezone.Id.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                           || timezone.StandardName.Contains(searchText, StringComparison.OrdinalIgnoreCase))
            .AsEnumerable();

        return Task.FromResult(results);
    }

    public void Select(TimeZoneInfo? timezone)
    {
        if (timezone is not null)
        {
            SelectedTimezone = timezone;
        }
    }

    public void InitializeFromId(string? timezoneId)
    {
        if (string.IsNullOrWhiteSpace(timezoneId))
        {
            SelectedTimezone = TimeZoneInfo.Local;
            return;
        }

        try
        {
            SelectedTimezone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            SelectedTimezone = TimeZoneInfo.Local;
        }
        catch (InvalidTimeZoneException)
        {
            SelectedTimezone = TimeZoneInfo.Local;
        }
    }

    internal static string FormatTimezoneShort(TimeZoneInfo timezone)
    {
        var offset = timezone.BaseUtcOffset;
        var sign = offset >= TimeSpan.Zero ? "+" : "-";
        var absoluteOffset = offset.Duration();
        return $"GMT{sign}{absoluteOffset.Hours}:{absoluteOffset.Minutes:D2} {timezone.StandardName}";
    }
}
