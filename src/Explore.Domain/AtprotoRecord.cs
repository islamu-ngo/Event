namespace Explore.Domain;

public class AtprotoRecord
{
    public Guid Id { get; set; }
    public required string Did { get; set; }
    public required string Collection { get; set; }
    public required string RecordKey { get; set; }
    public string? Cid { get; set; }
    public string? Uri { get; set; }
    public AtprotoRecordDirection Direction { get; set; }
    public AtprotoRecordProvenance Provenance { get; set; }
    public long SourceVersion { get; set; }
    public long? SourceCursor { get; set; }
    public string? RecordJson { get; set; }
    public string? RecordHash { get; set; }
    public string? SubjectUri { get; set; }
    public string? SubjectCid { get; set; }
    public DateTime? IndexedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? TombstonedAt { get; set; }
}

public enum AtprotoRecordDirection
{
    Inbound = 1,
    Outbound = 2,
    Reconciled = 3
}

public enum AtprotoRecordProvenance
{
    Jetstream = 1,
    LocalLifecycle = 2,
    JetstreamEcho = 3
}
