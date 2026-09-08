namespace Explore.Domain.Federation;

public sealed class AtprotoJetstreamConsumerState
{
    public Guid Id { get; set; }
    public required string Service { get; set; }

    /// <summary>
    /// Jetstream v2 <c>seq</c> resume token — not a timestamp. The ordering key used to reconcile records
    /// against PDS snapshots is <see cref="Explore.Domain.AtprotoRecord.SourceVersion"/>, which holds
    /// unix microseconds; the two live in different number spaces and must not be compared.
    /// </summary>
    public long Cursor { get; set; }
    public DateTime? LastEventAt { get; set; }
    public string? LeaseOwner { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTime? LeaseExpiresAt { get; set; }
    public long LeaseFence { get; set; }
    public DateTime UpdatedAt { get; set; }
}
