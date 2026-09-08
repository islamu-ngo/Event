using Explore.Domain.Interfaces;

namespace Explore.Domain.Views;

public sealed class EventWithSessionsView : ITenantEntity
{
    public Guid EventId { get; set; }
    public Guid TenantId { get; set; }
    public required string Title { get; set; }
    public required string Slug { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset? EndAt { get; set; }
    public required string Status { get; set; }
    public required string Visibility { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? IslamicTheme { get; set; }
    public string? Madhab { get; set; }
    public bool? IsRamadan { get; set; }
    public bool? PrayerAware { get; set; }
    public string? TechStack { get; set; }
    public string? DifficultyLevel { get; set; }
    public string? TargetAudience { get; set; }
    public int SessionCount { get; set; }
    public DateTimeOffset? FirstSessionStartAt { get; set; }
    public DateTimeOffset? LastSessionEndAt { get; set; }
    public bool HasInPersonSessions { get; set; }
    public bool HasVirtualSessions { get; set; }
    public string? AggregatedSessionIslamicThemes { get; set; }
    public required string EventCustomPropertyFacets { get; set; }
    public required string EventSessionCustomPropertyFacets { get; set; }
}
