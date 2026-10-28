using Explore.Application.Contracts.Moderation;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using ISLAMU.Moderation.Domain;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed class ModerationMeasuredOperationsProofTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 0, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments("exact")]
    [Arguments("missing-erasure")]
    [Arguments("wrong-store")]
    [Arguments("old-generation")]
    [Arguments("replay-gap")]
    [Arguments("unresolved")]
    [Arguments("foreign-purpose")]
    [Arguments("wrong-mode")]
    [Arguments("future-proof")]
    [Arguments("expired")]
    [Arguments("changed-current")]
    public async Task Recovery_requires_actual_restore_replay_and_erasure_reconciliation_for_exact_stores(string change)
    {
        var scope = Scope();
        var canonical = new ModerationMeasuredOperationsStore(ModerationMeasuredOperationsStoreKind.Canonical,
            Guid.CreateVersion7(), 2, 7, 23);
        var erasure = new ModerationMeasuredOperationsStore(ModerationMeasuredOperationsStoreKind.Erasure,
            Guid.CreateVersion7(), 3, 8, 29);
        var current = new ModerationMeasuredOperationsCurrent(scope,
            ModerationMeasuredOperationsProofKind.Recovery, Guid.CreateVersion7(), 1, 4,
            [canonical, erasure], [], [], 0, 0, Now, Now.AddMinutes(2));
        var reference = new ModerationMeasuredOperationsProofReference(Guid.CreateVersion7(), 1, 5, Now, Now.AddMinutes(1));
        ModerationMeasuredOperationsStoreRecovery Recovered(ModerationMeasuredOperationsStore store) =>
            new(store, Guid.CreateVersion7(), store.RequiredPosition - 1, store.RequiredPosition, 0,
                Now.AddMinutes(-3), Now.AddMinutes(-2), Now.AddMinutes(-1));
        var measured = new ModerationMeasuredOperationsMeasurement(scope, current.Kind, reference,
            current.AuthorityId, current.Revision, current.WriterGeneration,
            [Recovered(canonical), Recovered(erasure)], [], 0, 0, 0, Now);
        measured = change switch
        {
            "missing-erasure" => measured with { Stores = [Recovered(canonical)] },
            "wrong-store" => measured with { Stores = [Recovered(canonical), Recovered(erasure with { StoreId = Guid.CreateVersion7() })] },
            "old-generation" => measured with { Stores = [Recovered(canonical with { Generation = 6 }), Recovered(erasure)] },
            "replay-gap" => measured with { Stores = [Recovered(canonical) with { ReplayedThrough = 22 }, Recovered(erasure)] },
            "unresolved" => measured with { Stores = [Recovered(canonical), Recovered(erasure) with { PendingReconciliations = 1 }] },
            "foreign-purpose" => measured with { Scope = scope with { RecognitionPurpose = 1 } },
            "wrong-mode" => measured with { Scope = scope with { Binding = scope.Binding with { Mode = ModerationNativeMode.RemoteStrict } } },
            "future-proof" => measured with { Reference = reference with { MeasuredAt = Now.AddTicks(1) } },
            "expired" => measured with { Reference = reference with { ValidUntil = Now } },
            _ => measured
        };
        if (change == "changed-current") current = current with { Revision = 2 };
        await Assert.That(ModerationMeasuredOperationsProofJoin.Match(current, measured, scope, current.Kind, Now) is not null)
            .IsEqualTo(change == "exact");
    }

    [Test]
    public async Task Remote_transport_needs_current_epoch_contiguous_head_and_fresh_heartbeat()
    {
        var scope = Scope() with { Binding = Scope().Binding with { Mode = ModerationNativeMode.RemoteStrict } };
        var replica = Guid.CreateVersion7();
        var current = new ModerationMeasuredOperationsCurrent(scope,
            ModerationMeasuredOperationsProofKind.Transport, Guid.CreateVersion7(), 1, 4,
            [], [], [replica], 9, 31, Now, Now.AddMinutes(2));
        var measured = new ModerationMeasuredOperationsMeasurement(scope, current.Kind,
            new(Guid.CreateVersion7(), 1, 5, Now, Now.AddMinutes(1)),
            current.AuthorityId, current.Revision, current.WriterGeneration,
            [], [], 9, 31, 0, Now, replica);
        await Assert.That(ModerationMeasuredOperationsProofJoin.Match(current, measured, scope, current.Kind, Now)).IsNotNull();
        foreach (var invalid in new[]
        {
            measured with { TransportEpoch = 8 }, measured with { AcknowledgedPosition = 30 },
            measured with { MissingSequenceCount = 1 }, measured with { HeartbeatAt = Now.AddMinutes(-2) },
            measured with { TransportReplicaId = Guid.CreateVersion7() }
        })
            await Assert.That(ModerationMeasuredOperationsProofJoin.Match(current, invalid, scope, current.Kind, Now)).IsNull();
    }

    private static ModerationMeasuredOperationsProofScope Scope()
    {
        var tenant = AuthorityScope.Tenant(AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()),
            Guid.CreateVersion7()), Guid.CreateVersion7());
        var binding = new ModerationActivationBinding(Guid.CreateVersion7(), 1, Guid.CreateVersion7(), tenant,
            ModerationRuleReference.Create(tenant.Id, "profile.operations", 1),
            ModerationCapability.ReadResource, ModerationNativeMode.Embedded);
        return new(Guid.CreateVersion7(), tenant.Id, Guid.CreateVersion7(), binding,
            new string('a', 64), "Sqlite", "provider-version", null, null, 0, 0, TimeSpan.Zero);
    }

    [Test]
    [Arguments("exact")]
    [Arguments("missing-replica")]
    [Arguments("wrong-key")]
    [Arguments("wrong-generation")]
    [Arguments("wrong-bytes")]
    [Arguments("short-retention")]
    [Arguments("wrong-purpose")]
    [Arguments("wrong-privacy-source")]
    public async Task Recognition_keys_require_every_current_replica_exact_material_and_original_purpose(string change)
    {
        var scope = Scope() with
        {
            RecognitionSourceId = Guid.CreateVersion7(),
            RecognitionAcceptanceId = Guid.CreateVersion7(),
            RecognitionSourceGeneration = 7,
            RecognitionPurpose = 3,
            RecognitionBackupHorizon = TimeSpan.FromDays(3)
        };
        var key = new ModerationMeasuredOperationsKey("recognition-current", 4, new string('b', 64), Now.AddDays(4));
        var replicas = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };
        var current = new ModerationMeasuredOperationsCurrent(scope,
            ModerationMeasuredOperationsProofKind.KeyConvergence, Guid.CreateVersion7(), 1, 8,
            [], [key], [.. replicas], 0, 0, Now, Now.AddMinutes(2));
        var measured = new ModerationMeasuredOperationsMeasurement(scope, current.Kind,
            new(Guid.CreateVersion7(), 1, 9, Now, Now.AddMinutes(1)),
            current.AuthorityId, current.Revision, current.WriterGeneration, [],
            [new(replicas[0], key, Now), new(replicas[1], key, Now)], 0, 0, 0, Now);
        measured = change switch
        {
            "missing-replica" => measured with { Keys = [measured.Keys[0]] },
            "wrong-key" => measured with { Keys = [measured.Keys[0], new(replicas[1], key with { KeyId = "other" }, Now)] },
            "wrong-generation" => measured with { Keys = [measured.Keys[0], new(replicas[1], key with { Generation = 3 }, Now)] },
            "wrong-bytes" => measured with { Keys = [measured.Keys[0], new(replicas[1], key with { VerificationTag = new string('c', 64) }, Now)] },
            "wrong-purpose" => measured with { Scope = scope with { RecognitionPurpose = 1 } },
            "wrong-privacy-source" => measured with { Scope = scope with { RecognitionSourceId = Guid.CreateVersion7() } },
            _ => measured
        };
        if (change == "short-retention")
        {
            key = key with { RetainUntil = Now.AddDays(1) };
            current = current with { Keys = [key] };
            measured = measured with { Keys = [new(replicas[0], key, Now), new(replicas[1], key, Now)] };
        }
        await Assert.That(ModerationMeasuredOperationsProofJoin.Match(current, measured, scope, current.Kind, Now) is not null)
            .IsEqualTo(change == "exact");
    }
}
