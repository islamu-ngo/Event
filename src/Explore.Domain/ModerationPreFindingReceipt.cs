namespace Explore.Domain;

/// <summary>Exact native retry identity, with no plaintext or delivery assertion.</summary>
public sealed class ModerationPreFindingReceipt
{
    private ModerationPreFindingReceipt() { }
    public Guid Id { get; private set; }
    public Guid HostBindingId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CaseId { get; private set; }
    public Guid ActorHumanId { get; private set; }
    public Guid ResultId { get; private set; }
    public long ResultRevision { get; private set; }
    public string RequestDigest { get; private set; } = string.Empty;
    public DateTimeOffset CompletedAt { get; private set; }

    public static ModerationPreFindingReceipt Complete(Guid id, Guid host, Guid trust, Guid tenant,
        Guid caseId, Guid actor, Guid resultId, long revision, string digest, DateTimeOffset now)
    {
        foreach (var value in new[] { id, host, trust, tenant, caseId, actor, resultId })
            ModerationHostBinding.RequireUuidV7(value, nameof(id));
        if (revision < 1 || digest.Length != 64 || !digest.All(char.IsAsciiHexDigitLower)
            || now == default || now == DateTimeOffset.MaxValue || now.Offset != TimeSpan.Zero)
            throw new ArgumentException("Pre-finding receipt requires exact native result coordinates.");
        return new()
        {
            Id = id, HostBindingId = host, TrustDomainId = trust, TenantId = tenant,
            CaseId = caseId, ActorHumanId = actor, ResultId = resultId, ResultRevision = revision,
            RequestDigest = digest, CompletedAt = now
        };
    }
}
