using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Explore.Application.Features.Moderation.Requests.Commands;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Contracts.Scheduling;

public interface IModerationReconciliationQuery
{
    Task<ModerationReconciliationBatch> ReadDueAsync(
        Guid tenantId, DateTimeOffset now, int batchSize, Guid? afterSanctionId, CancellationToken cancellationToken);
    /// <summary>Discovers incomplete effects whose wake-up is absent or completed; never takes over an outbox retry or dead letter.</summary>
    Task<ModerationEffectRecoveryBatch> ReadRecoverableEffectsAsync(
        Guid tenantId, DateTimeOffset now, int batchSize, Guid? afterEffectId, CancellationToken cancellationToken);
}

public sealed record ModerationEffectRecoveryBatch(
    IReadOnlyList<ModerationEffectRecoveryTarget> Targets, Guid? LastScannedEffectId)
{
    private ImmutableArray<ModerationEffectRecoveryTarget> targets = Targets.ToImmutableArray();
    public IReadOnlyList<ModerationEffectRecoveryTarget> Targets
    {
        get => targets;
        init => targets = value.ToImmutableArray();
    }
}

/// <summary>Durable pointer discovery only; the dispatcher must reacquire original cause and current maintenance authority.</summary>
public sealed record ModerationEffectRecoveryTarget(Guid EffectId, DateTimeOffset CreatedAt);

public sealed record ModerationReconciliationBatch(
    IReadOnlyList<ModerationReconciliationTarget> Targets, Guid? LastScannedSanctionId)
{
    private ImmutableArray<ModerationReconciliationTarget> targets = Targets.ToImmutableArray();
    public IReadOnlyList<ModerationReconciliationTarget> Targets
    {
        get => targets;
        init => targets = value.ToImmutableArray();
    }
}

/// <summary>Identity-only discovery snapshot, not a grant. The native store rereads all authority under its fence.</summary>
public sealed record ModerationReconciliationTarget(
    Guid HostBindingId, Guid TenantId, Guid MaintenanceServiceId,
    SubjectReference Subject, AuthorityScope Scope, Guid DecisionId, long DecisionRevision,
    Guid SanctionId, long SanctionRevision, Guid PolicyId, long PolicyRevision, DateTimeOffset Deadline)
{
    public ReconcileModerationSanctionCommand Command()
    {
        var operation = Operation();
        return new()
        {
            OperationId = operation.OperationId, EffectId = operation.EffectId, DecisionId = DecisionId,
            Subject = Subject, Target = Scope, ExpectedRevision = DecisionRevision,
            ExpectedPolicyRevision = PolicyRevision, SanctionId = SanctionId,
            ExpectedSanctionRevision = SanctionRevision
        };
    }

    public ModerationOperation Operation()
    {
        // A repeated discovery of the same transition keeps its identities, including after a lost
        // acknowledgement or process restart. A changed revision or activation/expiry deadline is
        // a different request. Credential rotation does not change the installed service identity.
        var identity = FormattableString.Invariant(
            $"{HostBindingId:N}:{TenantId:N}:{MaintenanceServiceId:N}:{DecisionId:N}:{DecisionRevision}:{SanctionId:N}:{SanctionRevision}:{PolicyId:N}:{PolicyRevision}:{Deadline.UtcTicks}");
        return ModerationOperation.Reconcile(Subject, Scope, StableId("operation:" + identity),
            StableId("effect:" + identity), DecisionId, DecisionRevision, PolicyRevision, SanctionId, SanctionRevision);
    }

    private Guid StableId(string identity)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        var milliseconds = Deadline.ToUnixTimeMilliseconds();
        if (milliseconds < 0 || milliseconds > 0xFFFFFFFFFFFFL)
            throw new ArgumentOutOfRangeException(null, "The reconciliation deadline cannot be encoded as UUIDv7.");
        for (var index = 5; index >= 0; index--)
        {
            bytes[index] = (byte)milliseconds;
            milliseconds >>= 8;
        }
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes.AsSpan(0, 16), bigEndian: true);
    }
}
