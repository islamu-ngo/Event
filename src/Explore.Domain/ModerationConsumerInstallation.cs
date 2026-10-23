namespace Explore.Domain;

/// <summary>
/// Finite native consumer artifact installation, not measured conformance or an activation grant.
/// Original artifacts and writer generation remain immutable after accountable withdrawal.
/// </summary>
public sealed class ModerationConsumerInstallation
{
    private ModerationConsumerInstallation() { }
    public Guid Id { get; private set; }
    public Guid HostBindingId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid TenantId { get; private set; }
    public string ConsumerId { get; private set; } = string.Empty;
    public int ModeCode { get; private set; }
    public string RuntimeGeneration { get; private set; } = string.Empty;
    public string InventoryFingerprint { get; private set; } = string.Empty;
    public Guid InstalledByHumanId { get; private set; }
    public long WriterGeneration { get; private set; }
    public long Revision { get; private set; }
    public DateTimeOffset InstalledAt { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }
    public Guid? WithdrawnByHumanId { get; private set; }
    public DateTimeOffset? WithdrawnAt { get; private set; }

    public static ModerationConsumerInstallation Install(
        Guid id, Guid hostBindingId, Guid trustDomainId, Guid tenantId, string consumerId, int modeCode,
        string runtimeGeneration, string inventoryFingerprint, Guid humanId, long writerGeneration,
        DateTimeOffset now, DateTimeOffset validUntil)
    {
        var source = new ModerationConsumerInstallation
        {
            Id = id,
            HostBindingId = hostBindingId,
            TrustDomainId = trustDomainId,
            TenantId = tenantId,
            ConsumerId = consumerId,
            ModeCode = modeCode,
            RuntimeGeneration = runtimeGeneration,
            InventoryFingerprint = inventoryFingerprint,
            InstalledByHumanId = humanId,
            WriterGeneration = writerGeneration,
            Revision = 1,
            InstalledAt = now.ToUniversalTime(),
            ValidUntil = validUntil
        };
        source.ValidatePersistedState();
        return source;
    }

    public void Withdraw(long expectedRevision, Guid humanId, DateTimeOffset now)
    {
        ValidatePersistedState();
        if (expectedRevision != Revision || WithdrawnAt is not null)
            throw new InvalidOperationException("Consumer withdrawal requires the original live installation revision.");
        now = now.ToUniversalTime();
        if (humanId == Guid.Empty || now < InstalledAt || now == DateTimeOffset.MaxValue)
            throw new ArgumentException("Consumer withdrawal requires accountable administration and finite non-regressing time.");
        Revision = checked(Revision + 1);
        WithdrawnByHumanId = humanId;
        WithdrawnAt = now;
    }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, HostBindingId, TrustDomainId, TenantId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        if (!IsConsumerId(ConsumerId) || ModeCode != 1 || !Fingerprint(RuntimeGeneration)
            || !Fingerprint(InventoryFingerprint) || InstalledByHumanId == Guid.Empty || WriterGeneration is < 1 or long.MaxValue)
            throw new ArgumentException("Consumer installation requires a supported native owner and exact observed artifacts.");
        ModerationQualificationPolicySource.RequireValidity(InstalledAt, ValidUntil);
        if (!(Revision == 1 && WithdrawnAt is null && WithdrawnByHumanId is null
            || Revision == 2 && WithdrawnAt is { } withdrawn && WithdrawnByHumanId is { } human && human != Guid.Empty
                && withdrawn >= InstalledAt && withdrawn != DateTimeOffset.MaxValue && withdrawn.Offset == TimeSpan.Zero))
            throw new ArgumentException("Consumer withdrawal state is inconsistent.");
    }

    public static bool IsConsumerId(string? value) => value is { Length: > 0 and <= 64 }
        && value.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-');

    private static bool Fingerprint(string? value) =>
        value is { Length: 64 } && value.All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f');
    public override string ToString() => nameof(ModerationConsumerInstallation);
}
