using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Features.Federation.Atproto.Services;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Configuration;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class PostgresModerationCompensationAuthorityTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments(false, false, true, null, true, false)]
    [Arguments(false, true, true, true, false, false)]
    [Arguments(true, false, false, true, false, true)]
    public async Task OriginalGlobalCompensationReadsDedicatedCapabilityForEachExactLineage(
        bool instanceEnabled, bool instanceLocked, bool ownOverride, bool? foreignOverride,
        bool expectedOwn, bool expectedForeign)
    {
        await using var packet = await CompensationRows.CreateAsync(fixture);
        await packet.SetFederationPolicyAsync(instanceEnabled, instanceLocked, ownOverride, foreignOverride);

        var candidates = await packet.ReadCandidatesAsync(identityOnly: false);

        await Assert.That(candidates.Any(row => row.Lineage.TenantId == packet.Rows.A)).IsTrue();
        await Assert.That(candidates.Any(row => row.Lineage.TenantId == packet.Rows.B)).IsTrue();
        foreach (var candidate in candidates)
        {
            await Assert.That(candidate.Lineage.TenantId == packet.Rows.A || candidate.Lineage.TenantId == packet.Rows.B).IsTrue();
            var capability = HierarchicalSettingMerge.Resolve(
                GovernanceSettingKeys.Federation.AtprotoEventsEnabled, candidate.Policy.EventsEnabled);
            await Assert.That(capability).IsNotNull();
            await Assert.That(capability!.Value).IsEqualTo(
                (candidate.Lineage.TenantId == packet.Rows.A ? expectedOwn : expectedForeign) ? "true" : "false");
            await Assert.That(capability.IsLocked).IsEqualTo(instanceLocked);
            await Assert.That(candidate.Policy.EventsEnabled.User).IsNull();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ModerationOwnershipDiscoveryIncludesForeignGroundedTargets(bool identityOnly)
    {
        await using var packet = await CompensationRows.CreateAsync(fixture);
        await using var context = await packet.Factory.CreateDbContextAsync(packet.Rows.A);
        await packet.Rows.AssertOrdinaryRoleAsync(context);
        await packet.AssertRequestIsolationAsync(context);

        var candidates = await packet.ReadCandidatesAsync(identityOnly);
        var ownerships = candidates.Where(row => row.Settled is not null).Select(row => row.Lineage);

        Guid[] expected = identityOnly
            ? [packet.OwnEventId, packet.ForeignEventId, packet.DeletedEventId]
            : [packet.OwnEventId, packet.ForeignEventId, packet.DeletedEventId, packet.OtherDidEventId];
        await Assert.That(ownerships.Select(row => row.SourceEntityId).Order().ToArray())
            .IsEquivalentTo(expected);
        await Assert.That(context.TenantFilterTenantId).IsEqualTo(packet.Rows.A);
        await packet.AssertRequestIsolationAsync(context);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ModerationUnsettledDiscoveryIncludesForeignPendingAndProcessingTargets(bool identityOnly)
    {
        await using var packet = await CompensationRows.CreateAsync(fixture);
        await using var context = await packet.Factory.CreateDbContextAsync(packet.Rows.A);
        await packet.Rows.AssertOrdinaryRoleAsync(context);
        await packet.AssertRequestIsolationAsync(context);

        var candidates = await packet.ReadCandidatesAsync(identityOnly);
        var unsettled = candidates.SelectMany(row => row.Unsettled);

        Guid[] expected = identityOnly
            ? [packet.OwnOutboxId, packet.ForeignOutboxId, packet.DeletedOutboxId]
            : [packet.OwnOutboxId, packet.ForeignOutboxId, packet.DeletedOutboxId, packet.OtherDidOutboxId];
        await Assert.That(unsettled.Select(row => row.Id).Order().ToArray()).IsEquivalentTo(expected);
        await Assert.That(context.TenantFilterTenantId).IsEqualTo(packet.Rows.A);
        await packet.AssertRequestIsolationAsync(context);
    }

    [Test]
    public async Task RequestLeaseCannotResolveOrSupersedeForeignLifecycle()
    {
        await using var packet = await CompensationRows.CreateAsync(fixture);
        await using var context = await packet.Factory.CreateDbContextAsync(packet.Rows.A);
        await packet.Rows.AssertOrdinaryRoleAsync(context);
        await packet.AssertRequestIsolationAsync(context);
        await Assert.That(await new EventRepository(context).GetAtprotoLifecycleStateAsync(
            packet.Rows.B, packet.ForeignEventId, CancellationToken.None)).IsNull();
        await new EfCoreUnitOfWork(context).ExecuteInTransactionAsync(async token =>
        {
            await Assert.That(await packet.OutboxRepository(context).SupersedePriorAsync(
                packet.Outbox(Guid.CreateVersion7(), packet.Rows.B, packet.ForeignEventId,
                    packet.Did, PdsSyncStatus.Pending, PdsSyncOperation.Delete),
                CompensationRows.Now, token)).IsEqualTo(0);
            await context.SaveChangesAsync(token);
        });
        await using var oracle = fixture.CreateDbContext();
        await Assert.That(await oracle.PdsSyncOutbox.IgnoreQueryFilters()
            .Where(row => row.Id == packet.ForeignOutboxId).Select(row => row.Status).SingleAsync())
            .IsEqualTo(PdsSyncStatus.Processing);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExactForeignTenantLeaseKeepsCompensationLineageAtomic(bool rollback)
    {
        await using var packet = await CompensationRows.CreateAsync(fixture);
        await using var context = await packet.Factory.CreateDbContextAsync(packet.Rows.B);
        await packet.Rows.AssertOrdinaryRoleAsync(context);
        var repository = packet.OutboxRepository(context);
        Guid successorId = Guid.CreateVersion7();
        var successor = packet.Outbox(successorId, packet.Rows.B, packet.ForeignEventId,
            packet.Did, PdsSyncStatus.Pending, PdsSyncOperation.Delete);
        var unitOfWork = new EfCoreUnitOfWork(context);

        async Task Compensate(CancellationToken token)
        {
            var lifecycle = await new EventRepository(context).GetAtprotoLifecycleStateAsync(
                packet.Rows.B, packet.ForeignEventId, token);
            await Assert.That(lifecycle).IsNotNull();
            await Assert.That(lifecycle!.ConcurrencyStamp).IsEqualTo(packet.ForeignSourceVersion);
            var deletedLifecycle = await new EventRepository(context).GetAtprotoLifecycleStateAsync(
                packet.Rows.B, packet.DeletedEventId, token);
            await Assert.That(deletedLifecycle).IsNotNull();
            await Assert.That(deletedLifecycle!.IsDeleted).IsTrue();
            await Assert.That(await repository.SupersedePriorAsync(
                successor, CompensationRows.Now, token)).IsEqualTo(1);
            await repository.AddAsync(successor, token);
            if (rollback)
                throw new CompensationPrecommitFailure();
        }

        if (rollback)
            await Assert.ThrowsAsync<CompensationPrecommitFailure>(() =>
                unitOfWork.ExecuteInTransactionAsync(Compensate));
        else
            await unitOfWork.ExecuteInTransactionAsync(Compensate);

        await Assert.That(context.TenantFilterTenantId).IsEqualTo(packet.Rows.B);
        await using var oracle = fixture.CreateDbContext();
        var prior = await oracle.PdsSyncOutbox.IgnoreQueryFilters()
            .SingleAsync(row => row.Id == packet.ForeignOutboxId);
        await Assert.That(prior.Status).IsEqualTo(rollback ? PdsSyncStatus.Processing : PdsSyncStatus.Superseded);
        await Assert.That(prior.SupersededById).IsEqualTo(rollback ? (Guid?)null : successorId);
        await Assert.That(prior.LeaseToken).IsEqualTo(rollback ? packet.ForeignLeaseToken : (Guid?)null);
        await Assert.That(await oracle.PdsSyncOutbox.IgnoreQueryFilters()
            .CountAsync(row => row.Id == successorId && row.Operation == PdsSyncOperation.Delete))
            .IsEqualTo(rollback ? 0 : 1);
        await Assert.That(await oracle.PdsSyncOutbox.IgnoreQueryFilters()
            .Where(row => row.Id == packet.OwnOutboxId).Select(row => row.Status).SingleAsync())
            .IsEqualTo(PdsSyncStatus.Pending);
        await Assert.That(await oracle.Events.IgnoreQueryFilters()
            .Where(row => row.Id == packet.ForeignEventId).Select(row => row.ConcurrencyStamp).SingleAsync())
            .IsEqualTo(packet.ForeignSourceVersion);
    }

    private sealed class CompensationPrecommitFailure : Exception;

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GlobalFixedDeleteAndExactSupersessionCommitOrRollbackTogether(bool rollback)
    {
        await using var packet = await CompensationRows.CreateAsync(fixture);
        Guid successorId = Guid.CreateVersion7();
        await packet.WriteForeignDeleteAsync(successorId, rollback);
        await using var oracle = fixture.CreateDbContext();
        var prior = await oracle.PdsSyncOutbox.IgnoreQueryFilters()
            .SingleAsync(row => row.Id == packet.ForeignOutboxId);
        await Assert.That(prior.Status).IsEqualTo(rollback ? PdsSyncStatus.Processing : PdsSyncStatus.Superseded);
        await Assert.That(prior.SupersededById).IsEqualTo(rollback ? (Guid?)null : successorId);
        await Assert.That(prior.LeaseToken).IsEqualTo(rollback ? packet.ForeignLeaseToken : (Guid?)null);
        await Assert.That(prior.Payload).IsEqualTo("{}");
        await Assert.That(await oracle.PdsSyncOutbox.IgnoreQueryFilters()
            .CountAsync(row => row.Id == successorId && row.Operation == PdsSyncOperation.Delete &&
                row.Payload == null && row.TenantId == packet.Rows.B)).IsEqualTo(rollback ? 0 : 1);
        await Assert.That(await oracle.PdsSyncOutbox.IgnoreQueryFilters()
            .Where(row => row.Id == packet.OwnOutboxId).Select(row => row.Status).SingleAsync())
            .IsEqualTo(PdsSyncStatus.Pending);
    }

    [Test]
    public async Task GlobalRepeatedCompensationReplacesActiveDeleteWithoutUniqueKeyConflict()
    {
        await using var packet = await CompensationRows.CreateAsync(fixture);
        Guid first = Guid.CreateVersion7();
        Guid replacement = Guid.CreateVersion7();
        await packet.WriteForeignDeleteAsync(first, rollback: false);
        await packet.WriteForeignDeleteAsync(replacement, rollback: false);
        await using var oracle = fixture.CreateDbContext();
        var priorDelete = await oracle.PdsSyncOutbox.IgnoreQueryFilters().SingleAsync(row => row.Id == first);
        await Assert.That(priorDelete.Status).IsEqualTo(PdsSyncStatus.Superseded);
        await Assert.That(priorDelete.SupersededById).IsEqualTo(replacement);
        await Assert.That(await oracle.PdsSyncOutbox.IgnoreQueryFilters().CountAsync(row =>
            row.SourceEntityId == packet.ForeignEventId && row.Operation == PdsSyncOperation.Delete &&
            row.Status == PdsSyncStatus.Pending)).IsEqualTo(1);
    }

    [Test]
    public async Task GlobalActivationDenialRollsBackStagedDeleteAndPredecessorSupersession()
    {
        await using var packet = await CompensationRows.CreateAsync(fixture);
        await using var oracle = fixture.CreateDbContext();
        var helper = oracle.GetService<ISqlGenerationHelper>();
        var entity = oracle.Model.FindEntityType(typeof(PdsSyncOutbox))!;
        string table = helper.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
        string policy = helper.DelimitIdentifier(PostgresAtprotoModerationCompensationContract.PolicyName + "_activate");
        Guid successorId = Guid.CreateVersion7();
        try
        {
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP POLICY {policy} ON {table}";
            await command.ExecuteNonQueryAsync();
            var rejected = await Assert.ThrowsAsync<PostgresException>(() =>
                packet.WriteForeignDeleteAsync(successorId, rollback: false));
            await Assert.That(rejected!.SqlState).IsEqualTo("42501");
            var predecessor = await oracle.PdsSyncOutbox.IgnoreQueryFilters()
                .SingleAsync(row => row.Id == packet.ForeignOutboxId);
            await Assert.That(predecessor.Status).IsEqualTo(PdsSyncStatus.Processing);
            await Assert.That(predecessor.LeaseToken).IsEqualTo(packet.ForeignLeaseToken);
            await Assert.That(predecessor.SupersededById).IsNull();
            await Assert.That(predecessor.SupersededAt).IsNull();
            await Assert.That(await oracle.PdsSyncOutbox.IgnoreQueryFilters()
                .AnyAsync(row => row.Id == successorId)).IsFalse();
        }
        finally
        {
            await PostgresAtprotoModerationCompensationContract.ApplyAsync(oracle);
        }
    }

    [Test]
    public async Task GlobalProcessingDeleteReplacementPreservesDelayAndRejectsOldClaim()
    {
        await using var packet = await CompensationRows.CreateAsync(fixture);
        Guid first = Guid.CreateVersion7();
        Guid replacement = Guid.CreateVersion7();
        await packet.WriteForeignDeleteAsync(first, rollback: false);
        await using var oracle = fixture.CreateDbContext();
        var processing = await oracle.PdsSyncOutbox.IgnoreQueryFilters().SingleAsync(row => row.Id == first);
        processing.Status = PdsSyncStatus.Processing;
        processing.LeaseOwner = "held-delete-worker";
        processing.LeaseToken = Guid.CreateVersion7();
        processing.LeaseFence = 3;
        processing.LeaseExpiresAt = CompensationRows.Now.AddMinutes(10);
        var claim = new PdsSyncClaim(first, processing.TenantId, processing.UserId,
            processing.LeaseToken.Value, processing.LeaseFence);
        await oracle.SaveChangesAsync();
        await packet.WriteForeignDeleteAsync(replacement, rollback: false);
        oracle.ChangeTracker.Clear();
        var prior = await oracle.PdsSyncOutbox.IgnoreQueryFilters().SingleAsync(row => row.Id == first);
        var successor = await oracle.PdsSyncOutbox.IgnoreQueryFilters().SingleAsync(row => row.Id == replacement);
        await Assert.That(prior.Status).IsEqualTo(PdsSyncStatus.Superseded);
        await Assert.That(prior.LeaseFence).IsEqualTo(3);
        await Assert.That(prior.LeaseToken).IsNull();
        await Assert.That(prior.SupersededById).IsEqualTo(replacement);
        await Assert.That(successor.NextRetryAt).IsEqualTo(CompensationRows.Now.AddMinutes(10).AddSeconds(5));
        await using var request = await packet.Factory.CreateDbContextAsync(packet.Rows.B);
        var repository = packet.OutboxRepository(request);
        await Assert.That(await repository.GetActiveClaimAsync(claim, CompensationRows.Now.AddSeconds(1))).IsNull();
        await Assert.That(await repository.TrySettleAsync(claim,
            "at://" + packet.Did + "/" + successor.Collection + "/" + successor.RecordKey,
            "obsolete-worker-cid", CompensationRows.Now.AddSeconds(1))).IsFalse();
    }

    private sealed class CompensationRows(PostgreSqlContainerFixture fixture) : IAsyncDisposable
    {
        internal static readonly DateTime Now = new(2028, 7, 10, 12, 0, 0, DateTimeKind.Utc);
        internal PostgresAuthenticationLookupAuthorityTests.AuthenticationRows Rows { get; private set; } = null!;
        internal TenantExploreDbContextFactory Factory { get; private set; } = null!;
        internal Guid ActorId { get; } = Guid.CreateVersion7();
        internal Guid IdentityId { get; } = Guid.CreateVersion7();
        private Guid _actorStamp;
        private Guid _identityStamp;
        internal Guid OwnEventId { get; } = Guid.CreateVersion7();
        internal Guid ForeignEventId { get; } = Guid.CreateVersion7();
        internal Guid DeletedEventId { get; } = Guid.CreateVersion7();
        internal Guid OtherDidEventId { get; } = Guid.CreateVersion7();
        internal Guid OwnOutboxId { get; } = Guid.CreateVersion7();
        internal Guid ForeignOutboxId { get; } = Guid.CreateVersion7();
        internal Guid DeletedOutboxId { get; } = Guid.CreateVersion7();
        internal Guid OtherDidOutboxId { get; } = Guid.CreateVersion7();
        internal Guid ForeignSourceVersion { get; } = Guid.CreateVersion7();
        internal Guid ForeignLeaseToken { get; } = Guid.CreateVersion7();
        internal string Did => "did:plc:" + ActorId.ToString("N")[..24];
        private string[] _tables = [];
        private readonly string _settlementRole = "moderation_settlement_" + Guid.CreateVersion7().ToString("N");
        private GlobalExploreDbContextFactory _settlementFactory = null!;
        private GlobalExploreDbContextFactory _globalFactory = null!;
        private long? _ownedModerationGrantor;
        private (string Value, bool Locked, DateTime? UpdatedAt, Guid? UpdatedBy)? _originalCapability;

        internal static async Task<CompensationRows> CreateAsync(PostgreSqlContainerFixture fixture)
        {
            var packet = new CompensationRows(fixture)
            {
                Rows = await PostgresAuthenticationLookupAuthorityTests.AuthenticationRows.CreateAsync(fixture)
            };
            await using var seed = fixture.CreateDbContext();
            Guid otherActorId = Guid.CreateVersion7();
            var moderatedActor = Actor(packet.ActorId);
            moderatedActor.Suspend("policy_violation", Now, packet.Rows.UserId);
            var moderatedIdentity = new AtprotoIdentity(AtprotoDid.Parse(packet.Did))
            {
                Id = packet.IdentityId,
                ActorId = packet.ActorId,
                Actor = null!,
                PdsHost = "https://pds.example.test",
                CreatedAt = Now
            };
            moderatedIdentity.Suspend("policy_violation", Now, packet.Rows.UserId);
            seed.Actors.AddRange(moderatedActor, Actor(otherActorId));
            seed.AtprotoIdentities.Add(moderatedIdentity);
            // Opaque provider-fixture bytes, not an OAuth credential or decryptable session.
            seed.UserAuthenticationTokens.Add(new UserAuthenticationToken
            {
                Id = Guid.CreateVersion7(),
                TenantId = packet.Rows.B,
                Tenant = null!,
                UserId = packet.Rows.UserId,
                User = null!,
                Provider = AuthenticationProviderDeploymentOptions.AtprotoProvider,
                SubjectDid = packet.Did,
                PdsHost = "https://pds.example.test",
                SessionCiphertext = RandomNumberGenerator.GetBytes(32),
                EncryptionKeyId = Guid.CreateVersion7().ToString("N"),
                OAuthClientKeyId = Guid.CreateVersion7().ToString("N"),
                EnvelopeVersion = 1,
                CreatedAt = Now,
                ConcurrencyStamp = Guid.CreateVersion7()
            });
            seed.UserExternalLogins.Add(new UserExternalLogin
            {
                Id = Guid.CreateVersion7(),
                UserId = packet.Rows.UserId,
                User = null!,
                AuthenticationProviderId = (int)AuthenticationProviderKind.Atproto,
                AuthenticationProvider = null!,
                ProviderKey = packet.Did,
                CreatedAt = Now
            });
            Guid otherActorEvent = Guid.CreateVersion7();
            Guid tombstonedEvent = Guid.CreateVersion7();
            Guid ungroundedEvent = Guid.CreateVersion7();
            string otherDid = "did:plc:" + otherActorId.ToString("N")[..24];
            int publicCodeOrdinal = 0;
            foreach (var target in new[]
            {
                (packet.OwnEventId, packet.Rows.A, packet.ActorId, packet.Did, false, false),
                (packet.ForeignEventId, packet.Rows.B, packet.ActorId, packet.Did, false, false),
                (packet.DeletedEventId, packet.Rows.B, packet.ActorId, packet.Did, true, false),
                (packet.OtherDidEventId, packet.Rows.B, packet.ActorId, otherDid, false, false),
                (otherActorEvent, packet.Rows.B, otherActorId, packet.Did, false, false),
                (tombstonedEvent, packet.Rows.B, packet.ActorId, packet.Did, false, true)
            })
            {
                var source = new Explore.Domain.Event(EventStatusEnum.Published)
                {
                    Id = target.Item1,
                    TenantId = target.Item2,
                    Tenant = null!,
                    ActorId = target.Item3,
                    Actor = null!,
                    Title = "Moderation compensation source",
                    EventStatus = null!,
                    PublicCode = "MOD" + (++publicCodeOrdinal).ToString("D8", System.Globalization.CultureInfo.InvariantCulture),
                    EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                    VisibilityTypeId = (int)VisibilityTypeEnum.Public,
                    VisibilityType = null!,
                    EventFormatId = (int)EventFormatEnum.Digital,
                    EventFormat = null!,
                    CreatedAt = Now,
                    ConcurrencyStamp = target.Item1 == packet.ForeignEventId
                        ? packet.ForeignSourceVersion : Guid.CreateVersion7(),
                    IsDeleted = target.Item5
                };
                var record = new AtprotoRecord
                {
                    Id = Guid.CreateVersion7(),
                    Did = target.Item4,
                    Collection = AtprotoEventPublicationPlanner.EventCollection,
                    RecordKey = target.Item1.ToString("N"),
                    Cid = "moderation-cid",
                    RecordJson = "{}",
                    RecordHash = new string('a', 64),
                    SourceVersion = 1,
                    Direction = AtprotoRecordDirection.Outbound,
                    Provenance = AtprotoRecordProvenance.LocalLifecycle,
                    UpdatedAt = Now,
                    TombstonedAt = target.Item6 ? Now : null
                };
                record.Uri = $"at://{record.Did}/{record.Collection}/{record.RecordKey}";
                seed.AddRange(source, record, new AtprotoOutboundRecordOwnership
                {
                    AtprotoRecordId = record.Id,
                    TenantId = target.Item2,
                    UserId = packet.Rows.UserId,
                    SourceEntityType = AtprotoEventPublicationPlanner.EventSourceType,
                    SourceEntityId = source.Id,
                    SourceVersion = source.ConcurrencyStamp,
                    CreatedAt = Now,
                    UpdatedAt = Now
                });
            }
            // An ownership with no exact matching lifecycle must never enter either moderation target set.
            var orphanRecord = new AtprotoRecord
            {
                Id = Guid.CreateVersion7(),
                Did = packet.Did,
                Collection = AtprotoEventPublicationPlanner.EventCollection,
                RecordKey = ungroundedEvent.ToString("N"),
                RecordJson = "{}",
                RecordHash = new string('b', 64),
                SourceVersion = 1,
                Direction = AtprotoRecordDirection.Outbound,
                Provenance = AtprotoRecordProvenance.LocalLifecycle,
                UpdatedAt = Now
            };
            orphanRecord.Uri = $"at://{orphanRecord.Did}/{orphanRecord.Collection}/{orphanRecord.RecordKey}";
            seed.AddRange(orphanRecord, new AtprotoOutboundRecordOwnership
            {
                AtprotoRecordId = orphanRecord.Id,
                TenantId = packet.Rows.B,
                UserId = packet.Rows.UserId,
                SourceEntityType = AtprotoEventPublicationPlanner.EventSourceType,
                SourceEntityId = ungroundedEvent,
                SourceVersion = Guid.CreateVersion7(),
                CreatedAt = Now,
                UpdatedAt = Now
            });
            var foreign = packet.Outbox(packet.ForeignOutboxId, packet.Rows.B, packet.ForeignEventId,
                packet.Did, PdsSyncStatus.Processing);
            foreign.LeaseToken = packet.ForeignLeaseToken;
            foreign.LeaseOwner = "moderation-predecessor";
            foreign.LeaseFence = 1;
            foreign.LeaseExpiresAt = Now.AddMinutes(5);
            var superseded = packet.Outbox(Guid.CreateVersion7(), packet.Rows.B, packet.DeletedEventId,
                packet.Did, PdsSyncStatus.Superseded);
            superseded.SupersededAt = Now;
            superseded.SupersededById = packet.DeletedOutboxId;
            seed.PdsSyncOutbox.AddRange(
                packet.Outbox(packet.OwnOutboxId, packet.Rows.A, packet.OwnEventId, packet.Did, PdsSyncStatus.Pending),
                foreign,
                packet.Outbox(packet.DeletedOutboxId, packet.Rows.B, packet.DeletedEventId, packet.Did, PdsSyncStatus.Pending),
                packet.Outbox(packet.OtherDidOutboxId, packet.Rows.B, packet.OtherDidEventId, otherDid, PdsSyncStatus.Pending),
                packet.Outbox(Guid.CreateVersion7(), packet.Rows.B, otherActorEvent, packet.Did, PdsSyncStatus.Pending),
                packet.Outbox(Guid.CreateVersion7(), packet.Rows.B, ungroundedEvent, packet.Did, PdsSyncStatus.Pending),
                packet.Outbox(Guid.CreateVersion7(), packet.Rows.B, packet.DeletedEventId, packet.Did,
                    PdsSyncStatus.Pending, collection: "app.bsky.feed.post"),
                packet.Outbox(Guid.CreateVersion7(), packet.Rows.B, packet.DeletedEventId, packet.Did,
                    PdsSyncStatus.Pending, sourceType: "Location"),
                packet.Outbox(Guid.CreateVersion7(), packet.Rows.B, packet.DeletedEventId, packet.Did, PdsSyncStatus.Completed),
                superseded);
            try
            {
                await seed.SaveChangesAsync();
            }
            catch (DbUpdateException failure) when (failure.InnerException is PostgresException databaseFailure)
            {
                throw new InvalidOperationException(
                    $"Moderation fixture seed failed: {databaseFailure.SqlState}: {databaseFailure.ConstraintName}",
                    failure);
            }
            packet._actorStamp = moderatedActor.ConcurrencyStamp;
            packet._identityStamp = moderatedIdentity.ConcurrencyStamp;
            packet._tables = new[] { typeof(Explore.Domain.Event), typeof(AtprotoRecord),
                typeof(AtprotoOutboundRecordOwnership), typeof(PdsSyncOutbox) }
                .Select(type => seed.Model.FindEntityType(type)!)
                .Select(entity => $"\"{entity.GetSchema()}\".\"{entity.GetTableName()}\"").ToArray();
            await using var admin = new NpgsqlConnection(fixture.ConnectionString);
            await admin.OpenAsync();
            await using var grant = admin.CreateCommand();
            string settlementPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            grant.CommandText = $"""
                GRANT SELECT ON {string.Join(", ", packet._tables)} TO "{packet.Rows.RuntimeOptions.Username}";
                GRANT INSERT, UPDATE ON {packet._tables[3]} TO "{packet.Rows.RuntimeOptions.Username}";
                CREATE ROLE "{packet._settlementRole}" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD '{settlementPassword}';
                GRANT {PostgresPdsSettlementContract.GlobalRole} TO "{packet._settlementRole}";
                """;
            await grant.ExecuteNonQueryAsync();
            packet.Factory = new TenantExploreDbContextFactory(packet.Rows.RuntimeOptions, poolSize: 1);
            packet._settlementFactory = new GlobalExploreDbContextFactory(packet.Rows.RuntimeOptions with
            {
                Role = PrimaryDatabaseRole.Global,
                Username = packet._settlementRole,
                Password = settlementPassword
            }, poolSize: 1);
            await packet.EnrollConfiguredGlobalAsync(admin);
            packet._globalFactory = new GlobalExploreDbContextFactory(
                fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), poolSize: 1);
            // Independent privileged oracle certifies the foreign rows exist before SUT discovery runs.
            await using var oracle = fixture.CreateDbContext();
            await Assert.That(await oracle.Events.IgnoreQueryFilters().CountAsync(row =>
                row.Id == packet.ForeignEventId && row.TenantId == packet.Rows.B)).IsEqualTo(1);
            await Assert.That(await oracle.AtprotoOutboundRecordOwnerships.IgnoreQueryFilters().CountAsync(row =>
                row.SourceEntityId == packet.ForeignEventId && row.TenantId == packet.Rows.B)).IsEqualTo(1);
            await Assert.That(await oracle.PdsSyncOutbox.IgnoreQueryFilters().CountAsync(row =>
                row.Id == packet.ForeignOutboxId && row.TenantId == packet.Rows.B
                    && row.Status == PdsSyncStatus.Processing)).IsEqualTo(1);
            return packet;

            static Actor Actor(Guid id)
            {
                var subject = new ExternalActorSubject
                {
                    Id = Guid.CreateVersion7(),
                    FirstObservedAt = Now,
                    LastObservedAt = Now,
                    CreatedAt = Now
                };
                return new Actor
                {
                    Id = id,
                    ActorTypeId = (int)ActorTypeEnum.ExternalUnclassified,
                    ActorType = null!,
                    ExternalActorSubjectId = subject.Id,
                    ExternalActorSubject = subject,
                    Pii = new ActorPii { DisplayName = "Moderation compensation actor" },
                    CreatedAt = Now
                };
            }
        }

        internal PdsSyncOutboxRepository OutboxRepository(ExploreDbContext context) => new(context, _settlementFactory);

        internal async Task SetFederationPolicyAsync(bool instanceEnabled, bool instanceLocked,
            bool ownOverride, bool? foreignOverride)
        {
            const string key = GovernanceSettingKeys.Federation.AtprotoEventsEnabled;
            await using var seed = fixture.CreateDbContext();
            var instance = await seed.Set<AtprotoFederationInstanceSetting>().SingleAsync(row => row.SettingKey == key);
            _originalCapability = (instance.Value, instance.IsLocked, instance.UpdatedAt, instance.UpdatedBy);
            instance.SetValue(instanceEnabled ? "true" : "false", Now, null);
            instance.SetLock(instanceLocked, Now, null);
            seed.Add(AtprotoFederationTenantSetting.Create(Rows.A, key,
                ownOverride ? "true" : "false", false, Now, null));
            if (foreignOverride is { } enabled)
                seed.Add(AtprotoFederationTenantSetting.Create(Rows.B, key,
                    enabled ? "true" : "false", false, Now, null));
            await seed.SaveChangesAsync();
            await Assert.That(await seed.SystemSettings.AnyAsync(row => row.SettingKey == key)).IsFalse();
            await Assert.That(await seed.TenantSettingOverrides.IgnoreQueryFilters().AnyAsync(row => row.SettingKey == key)).IsFalse();
        }

        private async Task EnrollConfiguredGlobalAsync(NpgsqlConnection admin)
        {
            if (await ReadModerationMembershipAsync(admin) is not null) return;
            await using var grant = admin.CreateCommand();
            grant.CommandText = $"""
                GRANT {Quote(PostgresAtprotoModerationCompensationContract.GlobalRole)}
                TO {Quote(fixture.NativeQueueEnrollment.GlobalLogin)}
                WITH INHERIT TRUE, SET FALSE, ADMIN FALSE
                """;
            await grant.ExecuteNonQueryAsync();
            _ownedModerationGrantor = await ReadModerationMembershipAsync(admin)
                ?? throw new InvalidOperationException("The documented Global moderation membership was not created.");
        }

        private async Task<long?> ReadModerationMembershipAsync(NpgsqlConnection admin)
        {
            await using var command = admin.CreateCommand();
            command.CommandText = """
                SELECT edge.grantor::bigint, edge.inherit_option, edge.set_option, edge.admin_option
                FROM pg_catalog.pg_auth_members edge
                JOIN pg_catalog.pg_roles parent ON parent.oid=edge.roleid
                JOIN pg_catalog.pg_roles member ON member.oid=edge.member
                WHERE parent.rolname=@parent AND member.rolname=@member
                """;
            command.Parameters.AddWithValue("parent", PostgresAtprotoModerationCompensationContract.GlobalRole);
            command.Parameters.AddWithValue("member", fixture.NativeQueueEnrollment.GlobalLogin);
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;
            long grantor = reader.GetInt64(0);
            if (!reader.GetBoolean(1) || reader.GetBoolean(2) || reader.GetBoolean(3) || await reader.ReadAsync())
                throw new InvalidOperationException("Unexpected Global moderation membership drift; the fixture will not normalize it.");
            return grantor;
        }

        private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

        internal async Task<List<AtprotoModerationDeleteCandidate>> ReadCandidatesAsync(bool identityOnly)
        {
            await using var global = await _globalFactory.CreateDbContextAsync();
            await Assert.That(global.TenantFilterTenantId).IsNull();
            // Execute-only Global authority must not gain the caller's private table grants.
            var denied = await Assert.ThrowsAsync<PostgresException>(() =>
                global.Events.IgnoreQueryFilters().Select(row => row.Title).ToListAsync());
            await Assert.That(denied!.SqlState).IsEqualTo("42501");
            var target = new AtprotoModerationTarget(ActorId,
                identityOnly ? IdentityId : null, identityOnly ? Did : null,
                identityOnly ? _identityStamp : _actorStamp);
            var candidates = new List<AtprotoModerationDeleteCandidate>();
            await new EfCoreUnitOfWork(global).ExecuteSerializableAsync(async token =>
            {
                var originalId = global.ContextId;
                var originalConnection = global.Database.GetDbConnection();
                var originalTransaction = global.Database.CurrentTransaction!.GetDbTransaction();
                int originalBackend = ((NpgsqlConnection)originalConnection).ProcessID;
                await foreach (var candidate in new AtprotoModerationCompensationStore(global, fixture.NativeQueueEnrollment)
                                   .ReadDeleteCandidatesAsync(target, token))
                {
                    await Assert.That(global.ContextId).IsEqualTo(originalId);
                    await Assert.That(ReferenceEquals(global.Database.GetDbConnection(), originalConnection)).IsTrue();
                    await Assert.That(ReferenceEquals(global.Database.CurrentTransaction?.GetDbTransaction(), originalTransaction)).IsTrue();
                    await Assert.That(((NpgsqlConnection)originalConnection).ProcessID).IsEqualTo(originalBackend);
                    candidates.Add(candidate);
                }
                return candidates.Count;
            }, CancellationToken.None);
            await Assert.That(global.TenantFilterTenantId).IsNull();
            return candidates;
        }

        internal async Task WriteForeignDeleteAsync(Guid successorId, bool rollback)
        {
            await using var global = await _globalFactory.CreateDbContextAsync();
            var unitOfWork = new EfCoreUnitOfWork(global);
            async Task<int> Write(CancellationToken token)
            {
                var store = new AtprotoModerationCompensationStore(global, fixture.NativeQueueEnrollment);
                var target = new AtprotoModerationTarget(ActorId, null, null, _actorStamp);
                AtprotoModerationDeleteCandidate? foreign = null;
                await foreach (var candidate in store.ReadDeleteCandidatesAsync(target, token))
                    if (candidate.Lineage.SourceEntityId == ForeignEventId)
                        foreign = candidate;
                await Assert.That(foreign).IsNotNull();
                DateTime? notBefore = foreign!.Unsettled.Select(mutation =>
                    AtprotoEventPublicationPlanner.CompensationNotBefore(
                        mutation.Status, mutation.NextRetryAt, mutation.LeaseExpiresAt, Now)).Max();
                var write = new AtprotoModerationDeleteWrite(
                    foreign.Lineage, foreign.Fence, successorId, Now, "https://pds.example.test/",
                    foreign.Settled?.RecordId, null, notBefore);
                await store.WriteDeleteAsync(target, write, token);
                // Identical retries must not create or supersede a different successor.
                await store.WriteDeleteAsync(target, write, token);
                if (rollback)
                    throw new CompensationPrecommitFailure();
                return 1;
            }
            if (rollback)
                await Assert.ThrowsAsync<CompensationPrecommitFailure>(() =>
                    unitOfWork.ExecuteSerializableAsync(Write, CancellationToken.None));
            else
                await unitOfWork.ExecuteSerializableAsync(Write, CancellationToken.None);
        }

        internal PdsSyncOutbox Outbox(Guid id, Guid tenant, Guid source, string did, PdsSyncStatus status,
            PdsSyncOperation operation = PdsSyncOperation.Create, string? collection = null, string? sourceType = null) => new()
            {
                Id = id,
                TenantId = tenant,
                UserId = Rows.UserId,
                Did = did,
                Collection = collection ?? AtprotoEventPublicationPlanner.EventCollection,
                RecordKey = source.ToString("N"),
                Operation = operation,
                Payload = operation == PdsSyncOperation.Delete ? null : "{}",
                PayloadHash = new string('c', 64),
                IdempotencyKey = id.ToString("N"),
                PdsHost = "https://pds.example.test",
                SourceEntityType = sourceType ?? AtprotoEventPublicationPlanner.EventSourceType,
                SourceEntityId = source,
                SourceVersion = source == ForeignEventId
                ? ForeignSourceVersion : Guid.CreateVersion7(),
                Status = status,
                CreatedAt = Now,
                MaxRetries = 5,
                ProcessedAt = status == PdsSyncStatus.Completed ? Now : null,
                SettledUri = status == PdsSyncStatus.Completed
                ? $"at://{did}/{AtprotoEventPublicationPlanner.EventCollection}/{source:N}" : null,
                SettledCid = status == PdsSyncStatus.Completed ? "moderation-cid" : null
            };

        internal async Task AssertRequestIsolationAsync(ExploreDbContext context)
        {
            await Assert.That(await context.Events.IgnoreQueryFilters().CountAsync(row =>
                row.Id == OwnEventId)).IsEqualTo(1);
            await Assert.That(await context.PdsSyncOutbox.IgnoreQueryFilters().CountAsync(row =>
                row.Id == OwnOutboxId)).IsEqualTo(1);
            await Assert.That(await context.AtprotoOutboundRecordOwnerships.IgnoreQueryFilters().CountAsync(row =>
                row.SourceEntityId == OwnEventId)).IsEqualTo(1);
            await Assert.That(await context.Events.IgnoreQueryFilters().CountAsync(row =>
                row.Id == ForeignEventId)).IsEqualTo(0);
            await Assert.That(await context.PdsSyncOutbox.IgnoreQueryFilters().CountAsync(row =>
                row.Id == ForeignOutboxId)).IsEqualTo(0);
            await Assert.That(await context.AtprotoOutboundRecordOwnerships.IgnoreQueryFilters().CountAsync(row =>
                row.SourceEntityId == ForeignEventId)).IsEqualTo(0);
        }

        public async ValueTask DisposeAsync()
        {
            if (_originalCapability is { } original)
            {
                await using var seed = fixture.CreateDbContext();
                const string key = GovernanceSettingKeys.Federation.AtprotoEventsEnabled;
                var instance = await seed.Set<AtprotoFederationInstanceSetting>().SingleAsync(row => row.SettingKey == key);
                instance.SetValue(original.Value, Now, null);
                instance.SetLock(original.Locked, Now, null);
                instance.UpdatedAt = original.UpdatedAt;
                instance.UpdatedBy = original.UpdatedBy;
                await seed.Set<AtprotoFederationTenantSetting>().IgnoreQueryFilters()
                    .Where(row => row.SettingKey == key && (row.TenantId == Rows.A || row.TenantId == Rows.B)).ExecuteDeleteAsync();
                await seed.SaveChangesAsync();
            }
            await using var admin = new NpgsqlConnection(fixture.ConnectionString);
            await admin.OpenAsync();
            if (_ownedModerationGrantor is { } ownedGrantor)
            {
                if (await ReadModerationMembershipAsync(admin) != ownedGrantor)
                    throw new InvalidOperationException("The fixture-owned moderation membership changed; cleanup will not repair it.");
                await using var ownedRevoke = admin.CreateCommand();
                ownedRevoke.CommandText = $"""
                    REVOKE {Quote(PostgresAtprotoModerationCompensationContract.GlobalRole)}
                    FROM {Quote(fixture.NativeQueueEnrollment.GlobalLogin)} GRANTED BY CURRENT_USER
                    """;
                await ownedRevoke.ExecuteNonQueryAsync();
            }
            await using var revoke = admin.CreateCommand();
            revoke.CommandText = $"""
                REVOKE ALL ON {string.Join(", ", _tables)} FROM "{Rows.RuntimeOptions.Username}";
                REVOKE {PostgresPdsSettlementContract.GlobalRole} FROM "{_settlementRole}";
                DROP ROLE "{_settlementRole}";
                """;
            await revoke.ExecuteNonQueryAsync();
            await Rows.DisposeAsync();
        }
    }
}
