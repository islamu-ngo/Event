using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Federation.Atproto.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Federation;
using Microsoft.Extensions.Logging.Abstractions;

namespace Event.Application.UnitTests.Features.Federation;

public sealed class AtprotoModerationCompensationPlannerTests
{
    private static readonly DateTime CreatedAt = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
    private const string PdsHost = "https://pds.example.test/";

    [Test]
    public async Task PlansDistinctLineagesForOneEventAndReusesSuccessorIdsAcrossRetries()
    {
        Guid eventId = Guid.CreateVersion7();
        var first = Candidate(eventId);
        var second = Candidate(eventId);
        second = second with
        {
            Lineage = second.Lineage with { TenantId = first.Lineage.TenantId, Did = "did:plc:othermoderation" },
            Settled = null,
            Unsettled = [Mutation(CreatedAt.AddSeconds(90), null)],
            Account = new(1, "did:plc:othermoderation", PdsHost, true)
        };
        var store = new FactStore([first, second]);
        var planner = Planner(store);
        var ids = new Dictionary<AtprotoPublicationLineage, Guid>();
        var target = Target();

        await planner.PlanDeletesAsync(target, ids, CreatedAt, CancellationToken.None);
        await planner.PlanDeletesAsync(target, ids, CreatedAt, CancellationToken.None);

        await Assert.That(store.Writes.Count).IsEqualTo(4);
        await Assert.That(store.Writes[0].Lineage).IsEqualTo(first.Lineage);
        await Assert.That(store.Writes[1].Lineage).IsEqualTo(second.Lineage);
        await Assert.That(store.Writes[0].SuccessorId).IsNotEqualTo(store.Writes[1].SuccessorId);
        await Assert.That(store.Writes[2].SuccessorId).IsEqualTo(store.Writes[0].SuccessorId);
        await Assert.That(store.Writes[3].SuccessorId).IsEqualTo(store.Writes[1].SuccessorId);
        await Assert.That(store.Writes[0].AtprotoRecordId).IsEqualTo(first.Settled?.RecordId);
        await Assert.That(store.Writes[0].ExpectedCid).IsEqualTo(first.Settled?.Cid);
    }

    [Test]
    [Arguments(20, 0, 65)]
    [Arguments(90, 120, 120)]
    public async Task ProcessingCompensationUsesPlanningWindowAndLongestRetryDelay(
        int leaseSeconds, int retrySeconds, int expectedSeconds)
    {
        var candidate = Candidate(Guid.CreateVersion7()) with
        {
            Unsettled =
            [
                Mutation(CreatedAt.AddSeconds(20), null),
                Mutation(CreatedAt.AddSeconds(leaseSeconds),
                    retrySeconds == 0 ? null : CreatedAt.AddSeconds(retrySeconds))
            ]
        };
        var store = new FactStore([candidate]);

        await Planner(store).PlanDeletesAsync(
            Target(), new Dictionary<AtprotoPublicationLineage, Guid>(), CreatedAt, CancellationToken.None);

        await Assert.That(store.Writes.Count).IsEqualTo(1);
        await Assert.That(store.Writes[0].NotBefore).IsEqualTo(CreatedAt.AddSeconds(expectedSeconds));
        await Assert.That(store.Writes[0].ExpectedCid).IsNull();
        await Assert.That(store.Writes[0].AtprotoRecordId).IsEqualTo(candidate.Settled?.RecordId);
    }

    [Test]
    [Arguments(false, true, 1)]
    [Arguments(true, false, 1)]
    [Arguments(true, true, 2)]
    public async Task MissingCapabilityConsentOrUnambiguousSessionCannotProduceDelete(
        bool enabled, bool consent, int sessionCount)
    {
        var candidate = Candidate(Guid.CreateVersion7()) with
        {
            Policy = Policy(enabled, consent),
            Account = new(sessionCount, "did:plc:moderation", PdsHost, true)
        };
        var store = new FactStore([candidate]);
        var ids = new Dictionary<AtprotoPublicationLineage, Guid>();

        await Planner(store).PlanDeletesAsync(Target(), ids, CreatedAt, CancellationToken.None);

        await Assert.That(store.Writes).IsEmpty();
        await Assert.That(ids).IsEmpty();
    }

    private static AtprotoModerationCompensationPlanner Planner(FactStore store) =>
        new(store, NullLogger<AtprotoModerationCompensationPlanner>.Instance);

    private static AtprotoModerationTarget Target() =>
        new(Guid.CreateVersion7(), null, null, Guid.CreateVersion7());

    private static AtprotoModerationDeleteCandidate Candidate(Guid eventId) =>
        new(new AtprotoPublicationLineage(
                Guid.CreateVersion7(), AtprotoEventPublicationPlanner.EventSourceType, eventId,
                Guid.CreateVersion7(), "did:plc:moderation", AtprotoEventPublicationPlanner.EventCollection,
                Guid.CreateVersion7().ToString("N")),
            new(Guid.CreateVersion7(), Guid.CreateVersion7(), null),
            new(Guid.CreateVersion7(), "bafy-owned-record"),
            ImmutableArray<AtprotoModerationMutation>.Empty,
            Policy(true, true),
            new(1, "did:plc:moderation", PdsHost, true));

    private static AtprotoModerationMutation Mutation(DateTime leaseEnd, DateTime? retryAt) =>
        new(Guid.CreateVersion7(), PdsSyncOperation.Create, PdsSyncStatus.Processing,
            CreatedAt.AddHours(-1), retryAt, leaseEnd, 1, PdsHost, null);

    private static AtprotoModerationPolicyFacts Policy(bool enabled, bool consent)
    {
        var enabledFacts = Facts(enabled ? "true" : "false", SettingValueType.Boolean);
        var profileFacts = Facts("\"platform\"", SettingValueType.String);
        var consentFacts = Facts("false", SettingValueType.Boolean) with { User = consent ? "true" : "false" };
        return new(enabledFacts, profileFacts, consentFacts);
    }

    private static SettingMergeFacts Facts(string value, SettingValueType valueType) =>
        new(new SystemSettingFacts(value, valueType, false, null, null, null),
            null, null, null, null, true, null);

    private sealed class FactStore(ImmutableArray<AtprotoModerationDeleteCandidate> candidates)
        : IAtprotoModerationCompensationStore
    {
        internal List<AtprotoModerationDeleteWrite> Writes { get; } = [];

        public async IAsyncEnumerable<AtprotoModerationDeleteCandidate> ReadDeleteCandidatesAsync(
            AtprotoModerationTarget target, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return candidate;
            }
            await Task.CompletedTask;
        }

        public Task WriteDeleteAsync(
            AtprotoModerationTarget target, AtprotoModerationDeleteWrite write, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Writes.Add(write);
            return Task.CompletedTask;
        }
    }
}
