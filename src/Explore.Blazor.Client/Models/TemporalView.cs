namespace Explore.Blazor.Client.Models;

/// <summary>
/// Temporal view filter for event listing queries.
/// </summary>
public enum TemporalView
{
    Upcoming,
    Ongoing,
    Past,
    UpcomingAndOngoing,
    All
}
