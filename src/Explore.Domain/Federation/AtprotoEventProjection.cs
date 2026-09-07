namespace Explore.Domain.Federation;

public sealed class AtprotoEventProjection
{
    public Guid AtprotoRecordId { get; set; }
    public required string Name { get; set; }
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
