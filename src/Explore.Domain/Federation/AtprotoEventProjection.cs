using Explore.Domain.Services.Discovery;

namespace Explore.Domain.Federation;

public sealed class AtprotoEventProjection
{
    private Guid _atprotoRecordId;
    private string _name = string.Empty;

    public Guid AtprotoRecordId
    {
        get => _atprotoRecordId;
        set
        {
            _atprotoRecordId = value;
            DiscoverySourceSortKey = EventDiscoveryRank.SourceKey(value);
        }
    }
    public required string Name
    {
        get => _name;
        set
        {
            string rank = EventDiscoveryRank.TitleKey(value);
            _name = value;
            DiscoveryTitleSortKey = rank;
        }
    }
    public string DiscoveryTitleSortKey { get; private set; } = string.Empty;
    public string DiscoverySourceSortKey { get; private set; } = EventDiscoveryRank.SourceKey(Guid.Empty);
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public string? Mode { get; set; }
    public string? Status { get; set; }
    public bool? RsvpExpected { get; set; }
    public string? LocationSummary { get; set; }
    public string? SourceUrl { get; set; }
    public long SourceVersion { get; set; }
    public DateTime MaterializedAt { get; set; }

    public AtprotoRecord? AtprotoRecord { get; set; }
}
