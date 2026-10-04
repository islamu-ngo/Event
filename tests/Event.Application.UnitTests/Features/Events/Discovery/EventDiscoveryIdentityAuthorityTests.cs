using System.Text.Json;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Features.Events.Discovery;
using Explore.Application.Features.Events.Discovery.Commands;
using Explore.Application.Specifications.Events;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Services.Discovery;

namespace Event.Application.UnitTests.Features.Events.Discovery;

using Event = Explore.Domain.Event;

public sealed class EventDiscoveryIdentityAuthorityTests
{
    [Test]
    public async Task Grant_expiring_during_provider_evaluation_cannot_commit_identity_audit_or_delivery()
    {
        var clock = new ReviewClock(new DateTimeOffset(2040, 1, 1, 12, 0, 0, TimeSpan.Zero));
        using var fixture = new Fixture(clock);
        fixture.GrantsExpireAtUtc = clock.Now.AddMinutes(1).UtcDateTime;
        fixture.AfterProviderEvaluation = () => clock.Now = new DateTimeOffset(
            fixture.GrantsExpireAtUtc.Value, TimeSpan.Zero);
        var result = await fixture.Handler.ExecuteAsync(fixture.Command(), CancellationToken.None);
        await Assert.That(result.IsSuccess).IsFalse();
        await fixture.AssertUnchanged();
    }

    [Test]
    public async Task RevokedMembershipCannotUseRetainedEventAssignments()
    {
        using var fixture = new Fixture { ActiveMembership = false };
        var result = await fixture.Handler.ExecuteAsync(fixture.Command(), CancellationToken.None);
        await Assert.That(result.IsSuccess).IsFalse();
        await fixture.AssertUnchanged();
    }

    [Test]
    public async Task ExplicitReviewPermissionIsRequiredOnBothRecords()
    {
        using var fixture = new Fixture();
        fixture.Grants[fixture.Primary.Id] = new(new HashSet<string>(), new HashSet<string> { PermissionCodes.EventUpdate }, false, true);
        var result = await fixture.Handler.ExecuteAsync(fixture.Command(), CancellationToken.None);
        await Assert.That(result.IsSuccess).IsFalse();
        await fixture.AssertUnchanged();
    }

    [Test]
    public async Task ReviewPermissionDoesNotReplaceManagementAuthority()
    {
        using var fixture = new Fixture();
        fixture.Grants[fixture.Primary.Id] = new(new HashSet<string>(), new HashSet<string> { "event:review-discovery-identity" }, false, false);
        var result = await fixture.Handler.ExecuteAsync(fixture.Command(), CancellationToken.None);
        await Assert.That(result.IsSuccess).IsFalse();
        await fixture.AssertUnchanged();
    }

    [Test]
    public async Task CreatorConflictCannotBeOverriddenByProviderAllow()
    {
        using var fixture = new Fixture();
        fixture.Primary.CreatedBy = fixture.UserId;
        var result = await fixture.Handler.ExecuteAsync(fixture.Command(), CancellationToken.None);
        await Assert.That(result.IsSuccess).IsFalse();
        await fixture.AssertUnchanged();
    }

    [Test]
    public async Task OwnerConflictCannotBeOverriddenByExplicitGrant()
    {
        using var fixture = new Fixture();
        fixture.Grants[fixture.Member.Id] = fixture.Grants[fixture.Member.Id] with { IsOwner = true };
        var result = await fixture.Handler.ExecuteAsync(fixture.Command(), CancellationToken.None);
        await Assert.That(result.IsSuccess).IsFalse();
        await fixture.AssertUnchanged();
    }

    [Test]
    public async Task CrossTenantPrimaryCannotBeReviewed()
    {
        using var fixture = new Fixture();
        fixture.Primary.TenantId = Guid.CreateVersion7();
        var result = await fixture.Handler.ExecuteAsync(fixture.Command(), CancellationToken.None);
        await Assert.That(result.IsSuccess).IsFalse();
        await fixture.AssertUnchanged();
    }

    [Test]
    public async Task ProviderUnavailableCannotCreateAnAlias()
    {
        using var fixture = new Fixture { ProviderAvailable = false };
        var result = await fixture.Handler.ExecuteAsync(fixture.Command(), CancellationToken.None);
        await Assert.That(result.IsSuccess).IsFalse();
        await fixture.AssertUnchanged();
    }

    [Test]
    public async Task StaleRevisionLeavesNoIdentityAuditOrOutbox()
    {
        using var fixture = new Fixture();
        var result = await fixture.Handler.ExecuteAsync(fixture.Command(revision: 1), CancellationToken.None);
        await Assert.That(result.IsSuccess).IsFalse();
        await fixture.AssertUnchanged();
    }

    [Test]
    [Arguments("same-offering")]
    [Arguments("different-offering")]
    public async Task CommitRevisionConflictRollsBackAlreadyWrittenDecisionEvidence(string decision)
    {
        using var fixture = new Fixture();
        bool reachedCommit = false;
        fixture.BeforeCommit = async () =>
        {
            reachedCommit = true;
            await Assert.That(fixture.Revision.IdentityEpoch).IsEqualTo(0);
            await Assert.That(fixture.Audit.Items.Count).IsEqualTo(1);
            await Assert.That(fixture.Outbox.Count).IsEqualTo(1);
        };
        var result = await fixture.Handler.ExecuteAsync(
            fixture.Command(decision, revision: 1), CancellationToken.None);
        await Assert.That(reachedCommit).IsTrue();
        await Assert.That(result.IsSuccess).IsFalse();
        await fixture.AssertUnchanged();
    }

    [Test]
    public async Task IdentityRevisionRemainsUnchangedUntilDecisionEvidenceIsWritten()
    {
        using var fixture = new Fixture();
        fixture.BeforeCommit = async () =>
        {
            await Assert.That(fixture.Revision.IdentityEpoch).IsEqualTo(0);
            await Assert.That(fixture.Audit.Items.Count).IsEqualTo(1);
            await Assert.That(fixture.Outbox.Count).IsEqualTo(1);
        };
        var result = await fixture.Handler.ExecuteAsync(fixture.Command(), CancellationToken.None);
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(fixture.Revision.IdentityEpoch).IsEqualTo(1);
    }

    [Test]
    public async Task SelfRelationshipIsRejectedBeforeMutation()
    {
        using var fixture = new Fixture();
        var command = fixture.Command() with
        {
            Review = fixture.Command().Review with { PrimaryEventId = fixture.Member.Id }
        };
        var result = await fixture.Handler.ExecuteAsync(command, CancellationToken.None);
        await Assert.That(result.IsSuccess).IsFalse();
        await fixture.AssertUnchanged();
    }

    [Test]
    public async Task ConfirmedRelationshipCommitsMinimalReceiptAndOutboxTogether()
    {
        using var fixture = new Fixture();
        Guid memberActor = fixture.Member.ActorId;
        var result = await fixture.Handler.ExecuteAsync(fixture.Command(), CancellationToken.None);
        await Assert.That(result.IsSuccess).IsTrue();
        var member = await fixture.FindAsync(fixture.TenantId, EventDiscoverySourceKind.LocalEvent,
            fixture.Member.Id.ToString("D"), CancellationToken.None);
        var primary = await fixture.FindAsync(fixture.TenantId, EventDiscoverySourceKind.LocalEvent,
            fixture.Primary.Id.ToString("D"), CancellationToken.None);
        await Assert.That(member?.Alias?.PrimaryIdentityId).IsEqualTo(primary?.Id);
        await Assert.That(fixture.Revision.IdentityEpoch).IsEqualTo(1);
        await Assert.That(fixture.Audit.Items.Count).IsEqualTo(1);
        await Assert.That(fixture.Outbox.Count).IsEqualTo(1);
        var receipt = JsonSerializer.Deserialize<EventDiscoveryIdentityCorrectionRequested>(fixture.Outbox[0].Payload!);
        await Assert.That(receipt?.ReasonCode).IsEqualTo("same_program");
        await Assert.That(receipt?.EventId).IsEqualTo(fixture.Member.Id);
        await Assert.That(fixture.Member.ActorId).IsEqualTo(memberActor);
        await Assert.That(fixture.ProviderRequests.All(request => request.Facts is not EventAuthorizationFacts)).IsTrue();
    }

    [Test]
    public async Task DifferentOfferingHasDurableOutcomeWithoutChangingIdentity()
    {
        using var fixture = new Fixture();
        var result = await fixture.Handler.ExecuteAsync(
            fixture.Command("different-offering"), CancellationToken.None);
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(fixture.Identities.Count).IsEqualTo(0);
        await Assert.That(fixture.Revision.IdentityEpoch).IsEqualTo(0);
        await Assert.That(fixture.Audit.Items.Count).IsEqualTo(1);
        await Assert.That(fixture.Outbox.Count).IsEqualTo(1);
    }

    [Test]
    public async Task DifferentOfferingRejectsReasonCodeWithTrailingNewline()
    {
        using var fixture = new Fixture();
        var command = fixture.Command("different-offering");
        var result = await fixture.Handler.ExecuteAsync(command with
        {
            Review = command.Review with { ReasonCode = "different_program\n" }
        }, CancellationToken.None);
        await Assert.That(result.IsSuccess).IsFalse();
        await fixture.AssertUnchanged();
    }

    [Test]
    public async Task ReversalRequiresItsOwnExplicitPermission()
    {
        using var fixture = new Fixture();
        await fixture.Handler.ExecuteAsync(fixture.Command(), CancellationToken.None);
        var result = await fixture.Handler.ExecuteAsync(fixture.Command("reverse", 1), CancellationToken.None);
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(fixture.Revision.IdentityEpoch).IsEqualTo(1);
        await Assert.That(fixture.Identities.Count(identity => identity.Alias is not null)).IsEqualTo(1);
    }

    [Test]
    public async Task AuditFailureRollsBackRelationshipAndOutbox()
    {
        using var fixture = new Fixture();
        fixture.Audit.FailCreate = true;
        await Assert.That(async () =>
            await fixture.Handler.ExecuteAsync(fixture.Command(), CancellationToken.None))
            .Throws<InvalidOperationException>();
        await fixture.AssertUnchanged();
    }

    [Test]
    public async Task MembershipRevokedBeforeFenceReleaseIsRechecked()
    {
        using var fixture = new Fixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.BeforeFence = async () =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(5));
        };
        Task<Explore.Application.Responses.BaseCommandResponse<Guid>> operation =
            fixture.Handler.ExecuteAsync(fixture.Command(), CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.ActiveMembership = false;
        release.TrySetResult();
        var result = await operation.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(result.IsSuccess).IsFalse();
        await fixture.AssertUnchanged();
    }

    [Test]
    public async Task PublicCandidateBoundDoesNotCountPrivateOrOtherTenantRows()
    {
        using var fixture = new Fixture();
        for (int index = 0; index < 30; index++)
            fixture.Events.Items.Insert(0, fixture.NewEvent(privateEvent: true));
        for (int index = 0; index < 30; index++)
            fixture.Events.Items.Insert(0, fixture.NewEvent(tenantId: Guid.CreateVersion7()));
        var result = await fixture.QueryHandler.QueryAsync(new(fixture.Member.Id), CancellationToken.None);
        await Assert.That(result.Candidates.Length).IsEqualTo(1);
        await Assert.That(result.Candidates[0].Id).IsEqualTo(fixture.Primary.Id);
        await Assert.That(result.ExpectedRevision).IsEqualTo(0);
    }

    [Test]
    public async Task CandidateLookupDeniesRevokedMembership()
    {
        using var fixture = new Fixture { ActiveMembership = false };
        await Assert.That(async () =>
            await fixture.QueryHandler.QueryAsync(new(fixture.Member.Id), CancellationToken.None))
            .Throws<AuthorizationException>();
    }

    [Test]
    public async Task PrivateManagedSourceStillReportsThePublicCandidateBound()
    {
        using var fixture = new Fixture();
        fixture.Member.VisibilityTypeId = (int)VisibilityTypeEnum.Private;
        for (int index = 0; index < 20; index++)
            fixture.Events.Items.Add(fixture.NewEvent());
        var result = await fixture.QueryHandler.QueryAsync(new(fixture.Member.Id), CancellationToken.None);
        await Assert.That(result.Candidates.Length).IsEqualTo(20);
        await Assert.That(result.IsBounded).IsTrue();
    }

    [Test]
    public async Task CandidateLookupFailsClosedWhenProviderIsUnavailable()
    {
        using var fixture = new Fixture { ProviderAvailable = false };
        await Assert.That(async () =>
            await fixture.QueryHandler.QueryAsync(new(fixture.Member.Id), CancellationToken.None))
            .Throws<AuthorizationProviderUnavailableException>();
    }

    [Test]
    public async Task ExplicitReversalRestoresSeparateIdentitiesAndPreservesEventIds()
    {
        using var fixture = new Fixture();
        await fixture.Handler.ExecuteAsync(fixture.Command(), CancellationToken.None);
        foreach (Guid id in new[] { fixture.Member.Id, fixture.Primary.Id })
            fixture.Grants[id] = fixture.Grants[id] with
            {
                PermissionCodes = new HashSet<string> { PermissionCodes.EventUpdate, "event:reverse-discovery-identity" }
            };
        var result = await fixture.Handler.ExecuteAsync(fixture.Command("reverse", 1), CancellationToken.None);
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Id).IsEqualTo(fixture.Member.Id);
        await Assert.That(fixture.Revision.IdentityEpoch).IsEqualTo(2);
        await Assert.That(fixture.Identities.All(identity => identity.Alias is null)).IsTrue();
        await Assert.That(fixture.Outbox.Count).IsEqualTo(2);
        await Assert.That(fixture.Events.Items.Count).IsEqualTo(2);
    }

    [Test]
    public async Task OutboxFailureRollsBackAuditAndRelationship()
    {
        using var fixture = new Fixture { FailOutbox = true };
        await Assert.That(async () =>
            await fixture.Handler.ExecuteAsync(fixture.Command(), CancellationToken.None))
            .Throws<InvalidOperationException>();
        await fixture.AssertUnchanged();
    }

    [Test]
    public async Task FullAttemptReplayRechecksMembershipAndDiscardsFailedAttemptWrites()
    {
        using var fixture = new Fixture();
        fixture.BeforeReplay = () => fixture.ActiveMembership = false;
        var result = await fixture.Handler.ExecuteAsync(fixture.Command(), CancellationToken.None);
        await Assert.That(result.IsSuccess).IsFalse();
        await fixture.AssertUnchanged();
    }

    [Test]
    public async Task CompetingPrimariesLeaveOnlyOneCommittedRelationship()
    {
        using var fixture = new Fixture();
        var other = fixture.NewEvent();
        fixture.Events.Items.Add(other);
        fixture.Grants[other.Id] = fixture.Grants[fixture.Primary.Id];
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.BeforeFence = async () =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(5));
        };
        var first = fixture.Handler.ExecuteAsync(fixture.Command(), CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var competing = fixture.Handler.ExecuteAsync(fixture.Command() with
        {
            Review = fixture.Command().Review with { PrimaryEventId = other.Id }
        }, CancellationToken.None);
        fixture.BeforeFence = null;
        release.TrySetResult();
        var winner = await first.WaitAsync(TimeSpan.FromSeconds(5));
        var loser = await competing.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(winner.IsSuccess).IsTrue();
        await Assert.That(loser.IsSuccess).IsFalse();
        await Assert.That(fixture.Revision.IdentityEpoch).IsEqualTo(1);
        await Assert.That(fixture.Identities.Count).IsEqualTo(2);
        await Assert.That(fixture.Audit.Items.Count).IsEqualTo(1);
        await Assert.That(fixture.Outbox.Count).IsEqualTo(1);
    }

    private sealed class Fixture : IEventDiscoveryIdentityRepository, IEventAuthoritySnapshotService,
        ITenantContext, IUserContext, IAuthorizationProvider, IOutboxRepository, IUnitOfWork, IDisposable
    {
        public Guid TenantId { get; } = Guid.CreateVersion7();
        public Guid? UserId { get; } = Guid.CreateVersion7();
        public string? Email => null;
        public string? Username => null;
        public bool IsAuthenticated => UserId.HasValue;
        public Guid GetRequiredUserId() => UserId!.Value;
        public bool ActiveMembership { get; set; } = true;
        public bool ProviderAvailable { get; set; } = true;
        public DateTime? GrantsExpireAtUtc { get; set; }
        public Action? AfterProviderEvaluation { get; set; }
        public bool FailOutbox { get; set; }
        public Action? BeforeReplay { get; set; }
        public Event Member { get; }
        public Event Primary { get; }
        public EventStore Events { get; }
        public AuditStore Audit { get; } = new();
        public List<OutboxMessage> Outbox { get; } = [];
        public List<EventDiscoveryIdentity> Identities { get; } = [];
        public EventDiscoveryRevision Revision { get; }
        public Dictionary<Guid, EventAuthorityForUser> Grants { get; } = [];
        public List<AuthorizationRequest> ProviderRequests { get; } = [];
        public Func<Task>? BeforeFence { get; set; }
        public Func<Task>? BeforeCommit { get; set; }
        private bool _transaction;
        private long? _expectedRevision;
        private EventDiscoveryRevision? _proposedRevision;
        private readonly SemaphoreSlim _transactionGate = new(1, 1);
        public void Dispose() => _transactionGate.Dispose();
        public ReviewEventDiscoveryAliasCommandHandler Handler { get; }
        public GetEventDuplicateCandidatesQueryHandler QueryHandler { get; }

        public Fixture(TimeProvider? clock = null)
        {
            Member = NewEvent();
            Primary = NewEvent();
            Events = new EventStore(TenantId);
            Events.Items.AddRange([Member, Primary]);
            Revision = new() { Id = Guid.CreateVersion7(), TenantId = TenantId };
            foreach (var entity in Events.Items)
                Grants[entity.Id] = new(new HashSet<string>(),
                    new HashSet<string> { PermissionCodes.EventUpdate, "event:review-discovery-identity" }, false, true);
            var memberships = new MembershipStore(this);
            Handler = new(this, Events, this, memberships, this, Audit, this, this, this, this, clock ?? TimeProvider.System);
            QueryHandler = new(Events, this, this, memberships, this, this, this, this, clock ?? TimeProvider.System);
        }

        public Event NewEvent(bool privateEvent = false, Guid? tenantId = null) => new(EventStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(), TenantId = tenantId ?? TenantId,
            Title = "Shared programme", PublicCode = Guid.CreateVersion7().ToString("N"),
            ActorId = Guid.CreateVersion7(),
            Actor = new() { Id = Guid.CreateVersion7(), ActorType = null!, Pii = null! },
            Tenant = null!, VisibilityType = null!, EventStatus = null!, EventFormat = null!,
            VisibilityTypeId = (int)(privateEvent ? VisibilityTypeEnum.Private : VisibilityTypeEnum.Public)
        };

        public ReviewEventDiscoveryAliasCommand Command(string decision = "same-offering", long revision = 0) =>
            new(Member.Id, new(Primary.Id, revision, decision, "same_program"));

        public async Task AssertUnchanged()
        {
            await Assert.That(Identities.Count).IsEqualTo(0);
            await Assert.That(Revision.IdentityEpoch).IsEqualTo(0);
            await Assert.That(Audit.Items.Count).IsEqualTo(0);
            await Assert.That(Outbox.Count).IsEqualTo(0);
        }

        public async Task AcquireFenceAsync(Guid tenantId, IReadOnlyCollection<Guid> identityIds, CancellationToken ct)
        {
            if (!_transaction || tenantId != TenantId) throw new InvalidOperationException("Missing transaction.");
            if (BeforeFence is not null) await BeforeFence();
        }
        public void ExpectRevisionAtCommit(Guid tenantId, long expectedRevision)
        {
            if (!_transaction || tenantId != TenantId)
                throw new InvalidOperationException("Missing transaction.");
            if (expectedRevision < 0 || _expectedRevision is { } previous && previous != expectedRevision)
                throw new InvalidOperationException("discovery_revision_conflict");
            _expectedRevision = expectedRevision;
            _proposedRevision ??= new EventDiscoveryRevision
            {
                Id = Revision.Id, TenantId = TenantId, IdentityEpoch = expectedRevision,
                DisclosureEpoch = Revision.DisclosureEpoch
            };
        }
        public Task<EventDiscoveryIdentity?> FindAsync(Guid tenantId, EventDiscoverySourceKind kind, string key, CancellationToken ct) =>
            Task.FromResult(Identities.SingleOrDefault(item => item.TenantId == tenantId && item.SourceKind == kind && item.SourceKey == key));
        public Task<IReadOnlyList<EventDiscoveryIdentity>> GetBindingsAsync(
            Guid tenantId, EventDiscoverySourceKind kind, IReadOnlyCollection<string> keys, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<EventDiscoveryIdentity>>(Identities.Where(item =>
                item.TenantId == tenantId && item.SourceKind == kind && keys.Contains(item.SourceKey)).ToArray());
        public async Task<EventDiscoveryIdentity> GetOrCreateAsync(Guid tenantId, EventDiscoverySourceKind kind, string key, CancellationToken ct)
        {
            var item = await FindAsync(tenantId, kind, key, ct);
            if (item is not null) return item;
            item = EventDiscoveryIdentity.Create(tenantId, kind, key);
            Identities.Add(item);
            return item;
        }
        public Task<EventDiscoveryRevision?> GetRevisionAsync(Guid tenantId, CancellationToken ct) =>
            Task.FromResult<EventDiscoveryRevision?>(tenantId == TenantId ? Revision : null);
        public Task<IReadOnlyList<EventDiscoveryIdentity>> GetGroupAsync(Guid tenantId, Guid id, CancellationToken ct)
        {
            var identity = Identities.Single(item => item.Id == id && item.TenantId == tenantId);
            Guid root = identity.Alias?.PrimaryIdentityId ?? identity.Id;
            return Task.FromResult<IReadOnlyList<EventDiscoveryIdentity>>(Identities.Where(item =>
                item.TenantId == tenantId && (item.Id == root || item.Alias?.PrimaryIdentityId == root)).ToArray());
        }
        public Task<EventDiscoveryRevision> ReviewAsync(Guid tenant, Guid member, Guid primary, long expected,
            Guid reviewer, string reason, DateTime at, CancellationToken ct)
        {
            ExpectRevisionAtCommit(tenant, expected);
            EventDiscoveryIdentityRules.Review(_proposedRevision!, expected, Identities, member, primary, reviewer, reason, at);
            return Task.FromResult(_proposedRevision!);
        }
        public Task<EventDiscoveryRevision> ReverseAsync(Guid tenant, Guid member, Guid primary, long expected,
            Guid reviewer, string reason, DateTime at, CancellationToken ct)
        {
            ExpectRevisionAtCommit(tenant, expected);
            EventDiscoveryIdentityRules.Reverse(_proposedRevision!, expected, Identities, member, primary, reviewer, reason, at);
            return Task.FromResult(_proposedRevision!);
        }
        public Task<EventDiscoveryRevision> AdvanceDisclosureAsync(Guid tenant, CancellationToken ct) => throw new NotSupportedException();
        public Task<EventAuthoritySnapshot> GetCommitBoundForUserAndEventsAsync(
            Guid tenant, Guid user, IReadOnlyCollection<Guid> ids, DateTime at, CancellationToken ct)
        {
            if (!_transaction) throw new InvalidOperationException("Missing transaction.");
            return GetForUserAndEventsAsync(tenant, user, ids, at, ct);
        }
        public Task<EventAuthoritySnapshot> GetForUserAndEventsAsync(Guid tenant, Guid user, IReadOnlyCollection<Guid> ids, DateTime at, CancellationToken ct) =>
            Task.FromResult(new EventAuthoritySnapshot(tenant, user,
                Grants.Where(pair => ids.Contains(pair.Key) && (GrantsExpireAtUtc is null || at < GrantsExpireAtUtc))
                    .ToDictionary()));
        public Task<AuthorizationDecision> AuthorizeAsync(AuthorizationRequest request, CancellationToken ct = default)
        {
            ProviderRequests.Add(request);
            AfterProviderEvaluation?.Invoke();
            return Task.FromResult(ProviderAvailable
                ? AuthorizationDecision.Allow(AuthorizationProviderMetadata.Cerbos)
                : AuthorizationDecision.Deny(AuthorizationProviderMetadata.Cerbos, AuthorizationDecisionReasonCodes.ProviderUnavailable));
        }
        public async Task<IReadOnlyList<AuthorizationDecision>> AuthorizeBatchAsync(IReadOnlyList<AuthorizationRequest> requests, CancellationToken ct = default)
        {
            var result = new List<AuthorizationDecision>();
            foreach (var request in requests) result.Add(await AuthorizeAsync(request, ct));
            return result;
        }
        public async Task<T> ExecuteSerializableAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
        {
            await _transactionGate.WaitAsync(ct);
            _transaction = true;
            int identities = Identities.Count, audit = Audit.Items.Count, outbox = Outbox.Count;
            long revision = Revision.IdentityEpoch;
            var aliases = Identities.ToDictionary(item => item.Id, item => item.Alias);
            void Rollback()
            {
                Identities.RemoveRange(identities, Identities.Count - identities);
                foreach (var item in Identities) item.Alias = aliases[item.Id];
                Audit.Items.RemoveRange(audit, Audit.Items.Count - audit);
                Outbox.RemoveRange(outbox, Outbox.Count - outbox);
                Revision.IdentityEpoch = revision;
                _expectedRevision = null;
                _proposedRevision = null;
            }
            try
            {
                T result = await operation(ct);
                if (BeforeReplay is { } replay)
                {
                    BeforeReplay = null;
                    Rollback();
                    replay();
                    result = await operation(ct);
                }
                if (BeforeCommit is not null)
                    await BeforeCommit();
                if (_expectedRevision is { } expected && Revision.IdentityEpoch != expected)
                    throw new InvalidOperationException("discovery_revision_conflict");
                if (_proposedRevision is not null)
                    Revision.IdentityEpoch = _proposedRevision.IdentityEpoch;
                return result;
            }
            catch
            {
                Rollback();
                throw;
            }
            finally
            {
                _expectedRevision = null;
                _proposedRevision = null;
                _transaction = false;
                _transactionGate.Release();
            }
        }
        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) =>
            throw new NotSupportedException("Review must select serializable isolation.");
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<T> ExecuteReadCommittedAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OutboxMessage> Create(OutboxMessage message) { Outbox.Add(message); return Task.FromResult(message); }
        public Task<IReadOnlyList<OutboxMessage>> CreateRange(IReadOnlyCollection<OutboxMessage> messages, CancellationToken ct = default)
        {
            if (FailOutbox) throw new InvalidOperationException("Outbox unavailable.");
            Outbox.AddRange(messages);
            return Task.FromResult<IReadOnlyList<OutboxMessage>>(messages.ToArray());
        }
        public Task<List<OutboxMessage>> GetPendingBatch(int size, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<DateTime?> TryClaimForProcessing(Guid id, DateTime at, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> TryReplaceProcessingPayloadAsync(Guid id, string expected, string replacement, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> MarkAsCompleted(Guid id, DateTime at, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OutboxFailureTransition> MarkAsFailed(Guid id, DateTime lease, string error, bool retry, int delay, DateTime at, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<DateTime?> TryClaimDeadLetterReconciliation(Guid id, DateTime at, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> MarkDeadLetterReconciled(Guid id, DateTime at, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<OutboxMessage>> GetFailedEntries(int limit = 100, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> CountIncompleteByEventTypeAsync(string type, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> CountDeadLetteredByEventTypeAsync(string type, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> DeleteCompletedOlderThan(DateTime cutoff, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class ReviewClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private abstract class Store<T> : IGenericRepository<T, Guid> where T : class
    {
        public List<T> Items { get; } = [];
        public virtual Task<T> Create(T entity) { Items.Add(entity); return Task.FromResult(entity); }
        public Task<T?> GetById(Guid id) => throw new NotSupportedException();
        public Task<IReadOnlyList<T>> GetAll() => Task.FromResult<IReadOnlyList<T>>(Items.ToArray());
        public Task<(IReadOnlyList<T> Items, int TotalCount)> GetAllPaged(int page, int size) => throw new NotSupportedException();
        public Task<bool> Exists(Guid id) => throw new NotSupportedException();
        public Task Update(T entity) => throw new NotSupportedException();
        public Task Delete(T entity) => throw new NotSupportedException();
    }

    private sealed class AuditStore : Store<AuditLog>, IAuditLogRepository
    {
        public bool FailCreate { get; set; }
        public override Task<AuditLog> Create(AuditLog entity) =>
            FailCreate ? throw new InvalidOperationException("Audit unavailable.") : base.Create(entity);
        public Task<(IReadOnlyList<AuditLog> Items, int TotalCount)> GetTemplateSyncHistoryAsync(
            string type, string id, int page, int size, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class MembershipStore(Fixture fixture) : Store<TenantUser>, ITenantUserRepository
    {
        public Task<bool> FenceActiveTenantUserAsync(Guid tenant, Guid user, CancellationToken ct = default) =>
            IsActiveTenantUserAsync(tenant, user, ct);
        public Task<bool> IsActiveTenantUserAsync(Guid tenant, Guid user, CancellationToken ct = default) =>
            Task.FromResult(tenant == fixture.TenantId && user == fixture.UserId && fixture.ActiveMembership);
        public Task<TenantUser?> GetByTenantAndUserAsync(Guid tenant, Guid user, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<TenantUser?> GetByTenantAndActorAsync(Guid tenant, Guid actor, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<TenantUser>> GetActiveTenantsForUserAsync(Guid user, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> TryRemoveMembershipAsync(Guid tenant, Guid user, Guid by, DateTime at, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class EventStore(Guid tenantId) : Store<Event>, IEventRepository
    {
        public Task<IReadOnlyList<Event>> SeekPublicDiscoveryAsync(
            EventQuerySpecification specification, EventDiscoverySourceCursor? after, int take,
            CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<Event>> GetPublicDiscoveryMembersAsync(
            EventQuerySpecification specification, IReadOnlyDictionary<Guid, Guid> matchingSessions,
            CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<Event>> GetAuthorizationTargetsByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Event>>(Items.Where(item => item.TenantId == tenantId && ids.Contains(item.Id)).ToArray());
        public Task<(List<Event> Items, int TotalCount)> GetEventsWithDetailsPaged(int page, int size,
            EventQuerySpecification specification, CancellationToken ct = default)
        {
            var eligible = specification.Apply(Items.Where(item => item.TenantId == tenantId && !item.IsDeleted).AsQueryable()).ToList();
            return Task.FromResult((eligible.Skip((page - 1) * size).Take(size).ToList(), eligible.Count));
        }
        public Task<Event?> GetEventWithDetails(Guid id) => throw new NotSupportedException();
        public Task<Event?> GetEventWithDetailsAsync(Guid id, Guid tenant, CancellationToken ct) => throw new NotSupportedException();
        public Task<Event?> GetPublicEventWithDetailsByCodeAsync(string code, CancellationToken ct) => throw new NotSupportedException();
        public Task<Event?> GetPublicEventForOpenGraphAsync(string code, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> IsPubliclyEligibleAsync(Guid tenant, Guid id, CancellationToken ct) => Task.FromResult(Items.Any(item =>
            item.TenantId == tenant && item.Id == id && !item.IsDeleted && item.VisibilityTypeId == (int)VisibilityTypeEnum.Public));
        public Task<Event?> GetScheduleGraphForUpdateAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<Event?> GetAuthorizationTargetByIdAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<Event?> GetRegistrationStatusEventForUpdateAsync(Guid id, Guid tenant, CancellationToken ct) => throw new NotSupportedException();
        public Task<AtprotoEventPublicationEntityGraph?> GetAtprotoPublicationGraphAsync(Guid tenant, Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<Event?> GetAtprotoLifecycleStateAsync(Guid tenant, Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<List<Event>> GetEventsWithDetails() => throw new NotSupportedException();
        public Task<List<Event>> GetMyEventsWithDetails(string user) => throw new NotSupportedException();
        public Task<IReadOnlyList<Event>> GetEventsByActorWithDetails(Guid actor, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<(List<Event> Items, int TotalCount)> GetEventsWithDetailsPaged(int page, int size) => throw new NotSupportedException();
        public Task<List<Event>> GetPublishedPublicEventsForSitemap(int count, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Event>> SearchAiReferenceEventsAsync(string term, int limit, CancellationToken ct) => throw new NotSupportedException();
        public Task<(List<Event> Items, int TotalCount)> GetMyEventsWithDetailsPaged(string user, int page, int size) => throw new NotSupportedException();
    }
}
