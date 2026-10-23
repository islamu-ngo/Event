using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationCaseNativeProofTests
{
    [Test]
    [Arguments(ModerationCaseTargetKind.EventVersion, "unchanged")]
    [Arguments(ModerationCaseTargetKind.LocalIncident, "unchanged")]
    [Arguments(ModerationCaseTargetKind.EventVersion, "publisher")]
    [Arguments(ModerationCaseTargetKind.LocalIncident, "publisher")]
    [Arguments(ModerationCaseTargetKind.EventVersion, "organizer")]
    [Arguments(ModerationCaseTargetKind.LocalIncident, "organizer")]
    [Arguments(ModerationCaseTargetKind.EventVersion, "swap")]
    [Arguments(ModerationCaseTargetKind.LocalIncident, "swap")]
    [Arguments(ModerationCaseTargetKind.EventVersion, "token-digest")]
    [Arguments(ModerationCaseTargetKind.LocalIncident, "token-digest")]
    [Arguments(ModerationCaseTargetKind.EventVersion, "receipt-relationship")]
    [Arguments(ModerationCaseTargetKind.LocalIncident, "receipt-relationship")]
    [Arguments(ModerationCaseTargetKind.EventVersion, "grant-source")]
    [Arguments(ModerationCaseTargetKind.LocalIncident, "grant-source")]
    [Arguments(ModerationCaseTargetKind.EventVersion, "grant-caller")]
    [Arguments(ModerationCaseTargetKind.LocalIncident, "grant-caller")]
    [Arguments(ModerationCaseTargetKind.EventVersion, "issuer-role")]
    [Arguments(ModerationCaseTargetKind.LocalIncident, "issuer-role")]
    [Arguments(ModerationCaseTargetKind.Actor, "unchanged")]
    [Arguments(ModerationCaseTargetKind.Service, "unchanged")]
    [Arguments(ModerationCaseTargetKind.Actor, "organizer")]
    [Arguments(ModerationCaseTargetKind.Service, "organizer")]
    [Arguments(ModerationCaseTargetKind.Actor, "swap")]
    [Arguments(ModerationCaseTargetKind.Service, "swap")]
    [Arguments(ModerationCaseTargetKind.Actor, "native-stamp")]
    [Arguments(ModerationCaseTargetKind.Service, "native-stamp")]
    [Arguments(ModerationCaseTargetKind.Service, "service-owner")]
    public async Task Narrative_output_requires_original_actors_and_exact_read_authority_from_first_tenant_to_delivery(
        ModerationCaseTargetKind kind, string change)
    {
        await using var database = await NativeDatabase.CreateAsync(kind);
        Guid organizerId = Guid.CreateVersion7(), replacementId = Guid.CreateVersion7();
        Guid replacementServiceId = Guid.CreateVersion7(), driftStamp = Guid.CreateVersion7();
        Guid issuerId = Guid.CreateVersion7(), issuerRoleId = Guid.CreateVersion7(), otherCallerId = Guid.CreateVersion7();
        await using (var seed = database.Context())
        {
            foreach (var id in new[] { organizerId, replacementId })
            {
                var service = new ServicePrincipal
                {
                    Id = Guid.CreateVersion7(),
                    Code = $"narrative-order-{id:N}",
                    DisplayName = "Narrative publisher"
                };
                seed.AddRange(service, new Actor
                {
                    Id = id,
                    ActorTypeId = (int)ActorTypeEnum.Bot,
                    ActorType = null!,
                    ServicePrincipalId = service.Id,
                    Pii = new ActorPii { ActorId = id, DisplayName = "Narrative publisher" }
                });
            }
            foreach (var id in new[] { issuerId, otherCallerId })
                seed.Users.Add(new User
                {
                    Id = id,
                    Pii = new UserPii
                    {
                        UserId = id,
                        Email = $"{id:N}@example.invalid",
                        FirstName = "Independent",
                        LastName = "Reader"
                    }
                });
            seed.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = issuerRoleId,
                UserId = issuerId,
                User = null!,
                RoleId = (int)RoleEnum.Admin,
                Role = null!,
                GrantedAt = database.Clock.Now.UtcDateTime
            });
            if (change == "service-owner")
                seed.ServicePrincipals.Add(new ServicePrincipal
                {
                    Id = replacementServiceId,
                    Code = $"narrative-unowned-{replacementServiceId:N}",
                    DisplayName = "Unowned service"
                });
            (await seed.Events.SingleAsync(row => row.Id == database.EventId)).OrganizerActorId = organizerId;
            await seed.SaveChangesAsync();
        }

        // Acceptance, independent native read authority and its bearer all predate the probe.
        var (caseId, setupGrantId) = await GrantedReadAsync(database);
        Guid sourceId;
        await using (var setup = database.Context())
            sourceId = (await setup.ModerationCaseReadGrants.AsNoTracking().SingleAsync(row => row.Id == setupGrantId)).SourceId;
        if (kind is ModerationCaseTargetKind.Actor or ModerationCaseTargetKind.Service)
        {
            await using var setup = database.Context();
            var source = await setup.ModerationCaseTargetSources.AsNoTracking().SingleAsync(row => row.Id == sourceId);
            await Assert.That(source.Kind).IsEqualTo(kind);
            await Assert.That(source.NativeKind).IsEqualTo(kind);
            await Assert.That(source.NativeTargetId).IsEqualTo(kind == ModerationCaseTargetKind.Actor ? database.ActorId : database.ServiceId);
            await Assert.That(source.EventId).IsEqualTo(database.EventId);
        }
        var grantId = await GrantFromIndependentIssuerAsync(caseId, sourceId);
        ModerationCaseIssuedReceipt issued;
        await using (var setup = database.Context())
        {
            var receipt = await IssuanceCommitter(setup, database, database.Caller).IssueReadReceiptAsync(grantId, default);
            await Assert.That(receipt).IsNotNull();
            issued = receipt!;
        }

        Guid alternateCaseId = Guid.Empty, alternateSourceId = Guid.Empty, alternateGrantId = Guid.Empty;
        if (change is "grant-source" or "receipt-relationship")
        {
            await using var setup = database.Context();
            var intake = IssuanceCommitter(setup, database, database.Caller);
            var alternate = database.Request(material: "Independent narrative for a different native occurrence") with
            {
                OccurrenceMaterial = Encoding.UTF8.GetBytes("A separate occurrence at the same native event")
            };
            var submission = await intake.IssueSubmissionReceiptAsync(alternate, default);
            await Assert.That(submission).IsNotNull();
            var accepted = await intake.SubmitAsync(new(ModerationCaseAccessPurpose.SubmitAllegation,
                Guid.Empty, Guid.CreateVersion7(), 0, submission!.Receipt.Id, submission.Token)
            { Narrative = alternate.Material }, default);
            await Assert.That(accepted.Outcome).IsEqualTo(ModerationCaseIntakeCommitOutcome.Accepted);
            alternateCaseId = accepted.Acknowledgement!.CaseId;
            alternateSourceId = submission.Receipt.SourceId;
            alternateGrantId = await GrantFromIndependentIssuerAsync(alternateCaseId, alternateSourceId);
            await Assert.That(alternateCaseId).IsNotEqualTo(caseId);
            await Assert.That(alternateSourceId).IsNotEqualTo(sourceId);
        }

        await using var inspect = database.Context();
        var events = inspect.Events.AsNoTracking().Where(row => row.Id == database.EventId)
            .Select(row => new { row.Id, row.ActorId, row.OrganizerActorId, row.ConcurrencyStamp });
        var actors = inspect.Actors.AsNoTracking().Where(row => row.Id == database.ActorId)
            .Select(row => new { row.Id, row.ConcurrencyStamp, row.ServicePrincipalId });
        var services = inspect.ServicePrincipals.AsNoTracking().Where(row => row.Id == database.ServiceId)
            .Select(row => new { row.Id, row.ConcurrencyStamp });
        var grants = inspect.ModerationCaseReadGrants.AsNoTracking().OrderBy(row => row.Id).Select(row => new
        {
            row.Id,
            row.TenantId,
            row.TrustDomainId,
            row.CaseId,
            row.CaseRevision,
            row.SourceId,
            row.CallerUserId,
            row.IssuerUserId,
            row.IssuerRoleGrantId,
            row.IssuerPlatformGrant,
            row.Revision,
            row.IssuedAt,
            row.ExpiresAt,
            row.RevokedAt
        });
        var receipts = inspect.ModerationCaseTargetReceipts.AsNoTracking().OrderBy(row => row.Id).Select(row => new
        {
            row.Id,
            row.TenantId,
            row.CallerUserId,
            row.HostBindingId,
            row.QualificationEpochId,
            row.QualificationRevision,
            row.RelationshipKind,
            row.RelationshipSourceId,
            row.RelationshipRevision,
            row.RelationshipConcurrencyStamp,
            row.RelationshipFactsDigest,
            row.TokenDigest,
            row.SourceId,
            row.SourceVersion,
            row.MaterialFactId,
            row.TargetKind,
            row.TargetId,
            row.TargetVersion,
            row.OccurrenceId,
            row.LifetimePolicyId,
            row.LifetimePolicyRevision,
            row.Revision,
            row.IssuedAt,
            row.ExpiresAt,
            row.ConsumedAt,
            row.ConsumedCaseId,
            row.ConsumedOperationId
        });
        var sources = inspect.ModerationCaseTargetSources.AsNoTracking().OrderBy(row => row.Id).Select(row => new
        {
            row.Id,
            row.Version,
            row.TenantId,
            row.TrustDomainId,
            row.EventId,
            row.EventVersion,
            row.Kind,
            row.NativeKind,
            row.NativeTargetId,
            row.NativeTargetVersion,
            row.NativeFactsDigest,
            row.OccurrenceId,
            row.OccurrenceVersion,
            row.MaterialFactId,
            row.OccurrenceDigest,
            row.MaterialDigest,
            row.RecordedAt
        });
        var roles = inspect.PlatformUserRoles.AsNoTracking().Where(row => row.Id == issuerRoleId)
            .Select(row => new { row.Id, row.UserId, row.RoleId, row.GrantedAt, row.GrantedBy });
        var eventsBefore = await events.ToArrayAsync();
        var actorsBefore = await actors.ToArrayAsync();
        var servicesBefore = await services.ToArrayAsync();
        var grantsBefore = await grants.ToArrayAsync();
        var receiptsBefore = await receipts.ToArrayAsync();
        var sourcesBefore = await sources.ToArrayAsync();
        var rolesBefore = await roles.ToArrayAsync();
        var caseCount = await inspect.ModerationCases.CountAsync();
        var acknowledgementCount = await inspect.ModerationCaseIntakeAcknowledgements.CountAsync();
        var narrative = await inspect.ModerationCaseNarratives.AsNoTracking().SingleAsync(row => row.CaseId == caseId);
        var originalCiphertext = narrative.ProtectedMaterial.ToArray();
        var originalToken = issued.Token.ToArray();
        var changedToken = issued.Token.ToArray();
        changedToken[0] ^= 1;
        var changedDigest = Convert.ToHexStringLower(SHA256.HashData(changedToken));

        var changedRows = 0;
        var probe = new NarrativeActorOrderProbe(database.CallerId, issuerId, issuerRoleId, async (context, token) =>
        {
            await Assert.That(context.Database.CurrentTransaction).IsNotNull();
            await Assert.That(context.IsTenantFilterBypassed).IsFalse();
            var previousTrust = context.ModerationTrustDomainId;
            try
            {
                context.ModerationTrustDomainId = database.TrustId;
                changedRows = change switch
                {
                    "unchanged" => 0,
                    "publisher" => await context.Events.Where(row => row.Id == database.EventId).ExecuteUpdateAsync(
                        update => update.SetProperty(row => row.ActorId, replacementId), token),
                    "organizer" => await context.Events.Where(row => row.Id == database.EventId).ExecuteUpdateAsync(
                        update => update.SetProperty(row => row.OrganizerActorId, replacementId), token),
                    "swap" => await context.Events.Where(row => row.Id == database.EventId).ExecuteUpdateAsync(update => update
                        .SetProperty(row => row.ActorId, organizerId).SetProperty(row => row.OrganizerActorId, database.ActorId), token),
                    "native-stamp" => kind == ModerationCaseTargetKind.Actor
                        ? await context.Actors.Where(row => row.Id == database.ActorId).ExecuteUpdateAsync(
                            update => update.SetProperty(row => row.ConcurrencyStamp, driftStamp), token)
                        : await context.ServicePrincipals.Where(row => row.Id == database.ServiceId).ExecuteUpdateAsync(
                            update => update.SetProperty(row => row.ConcurrencyStamp, driftStamp), token),
                    "service-owner" => await context.Actors.Where(row => row.Id == database.ActorId).ExecuteUpdateAsync(
                        update => update.SetProperty(row => row.ServicePrincipalId, replacementServiceId), token),
                    "token-digest" => await context.ModerationCaseTargetReceipts.Where(row => row.Id == issued.Receipt.Id)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.TokenDigest, changedDigest), token),
                    "receipt-relationship" => await context.ModerationCaseTargetReceipts.Where(row => row.Id == issued.Receipt.Id)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.RelationshipSourceId, alternateGrantId), token),
                    "grant-source" => await context.ModerationCaseReadGrants.Where(row => row.Id == grantId).ExecuteUpdateAsync(update => update
                        .SetProperty(row => row.SourceId, alternateSourceId).SetProperty(row => row.CaseId, alternateCaseId), token),
                    "grant-caller" => await context.ModerationCaseReadGrants.Where(row => row.Id == grantId).ExecuteUpdateAsync(
                        update => update.SetProperty(row => row.CallerUserId, otherCallerId), token),
                    "issuer-role" => await context.PlatformUserRoles.Where(row => row.Id == issuerRoleId).ExecuteUpdateAsync(
                        update => update.SetProperty(row => row.RoleId, (int)RoleEnum.Moderator), token),
                    _ => throw new ArgumentOutOfRangeException(nameof(change))
                };
            }
            finally { context.ModerationTrustDomainId = previousTrust; }
        });
        await using var read = database.Context(probe);
        read.ModerationTrustDomainId = null;
        var caller = database.Caller;
        read.CurrentUserService = caller;
        var intakeReader = new ModerationCaseIntakeRepository(read,
            new ModerationCaseTargetReceiptRepository(read, caller, database.Machine, database.Clock),
            new ModerationCaseTargetProofOwner(read, caller, database.Machine, database.Clock), database.Clock);
        var custody = new ModerationCaseNarrativeRepository(read, NarrativeAdmin(read, caller), caller,
            database.Machine, intakeReader, database.Clock, database.NarrativeProtection);
        var request = new ModerationCaseAccessRequest(ModerationCaseAccessPurpose.ReadAllegation,
            caseId, Guid.Empty, 1, issued.Receipt.Id, issued.Token);

        foreach (var late in new[] { false, true })
        {
            changedRows = 0;
            probe.Arm(late);
            var deliveries = 0;
            ReadOnlyMemory<byte> borrowed = default;
            var allowed = await custody.DiscloseAsync(request, async (material, _) =>
            {
                deliveries++;
                borrowed = material;
                await Assert.That(read.Database.CurrentTransaction).IsNotNull();
                await Assert.That(read.Database.CurrentTransaction!.GetDbTransaction().IsolationLevel)
                    .IsEqualTo(System.Data.IsolationLevel.Serializable);
                await Assert.That(material.ToArray()).IsEquivalentTo(database.Request().Material.ToArray());
            }, default);
            var succeeds = change == "unchanged";
            await Assert.That(allowed).IsEqualTo(succeeds);
            await Assert.That(deliveries).IsEqualTo(succeeds ? 1 : 0);
            await Assert.That(borrowed.Length > 0).IsEqualTo(succeeds);
            await Assert.That(borrowed.ToArray().All(value => value == 0)).IsTrue();
            await Assert.That(probe.ObservedMutationCheckpoint).IsTrue();
            await Assert.That(changedRows).IsEqualTo(succeeds ? 0 : 1);
            await Assert.That(probe.SawTenant).IsTrue();
            await Assert.That(probe.CallerBeforeFirstActor).IsTrue();
            await Assert.That(probe.OriginalIssuerBeforeFirstActor).IsTrue();
            await Assert.That(probe.TrustAtFirstActor).IsEqualTo(database.TrustId);
            await Assert.That(probe.ActorsBeforeTenant.Distinct().SequenceEqual(new[] { database.ActorId, organizerId }.Order())).IsTrue();
            await Assert.That(probe.ActorAnchors.All(id => id == database.ActorId || id == organizerId)).IsTrue();
            await AssertCleanAndUnchangedAsync();

            if (!succeeds)
            {
                // The same owners must discard failed leases and acquire fresh native authority.
                probe.Disarm();
                await Assert.That(await custody.DiscloseAsync(request, async (material, _) =>
                {
                    deliveries++;
                    borrowed = material;
                    await Assert.That(read.Database.CurrentTransaction).IsNotNull();
                    await Assert.That(material.ToArray()).IsEquivalentTo(database.Request().Material.ToArray());
                }, default)).IsTrue();
                await Assert.That(deliveries).IsEqualTo(1);
                await Assert.That(borrowed.Length).IsGreaterThan(0);
                await Assert.That(borrowed.ToArray().All(value => value == 0)).IsTrue();
                await AssertCleanAndUnchangedAsync();
            }
        }

        probe.Disarm();
        var forbiddenDeliveries = 0;
        await Assert.That(await custody.DiscloseAsync(request with { PresentedToken = changedToken }, (_, _) =>
        {
            forbiddenDeliveries++;
            return Task.CompletedTask;
        }, default)).IsFalse();
        await Assert.That(forbiddenDeliveries).IsEqualTo(0);
        await AssertCleanAndUnchangedAsync();

        async Task<Guid> GrantFromIndependentIssuerAsync(Guid targetCaseId, Guid targetSourceId)
        {
            await using var setup = database.Context();
            var issuer = new NativeCaller(issuerId);
            setup.CurrentUserService = issuer;
            await using var transaction = await setup.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var owner = new ModerationCaseTargetProofOwner(setup, issuer, database.Machine, database.Clock);
            var grant = await owner.GrantReadAsync(new(Guid.CreateVersion7(), targetCaseId, 1, targetSourceId,
                database.CallerId, database.Clock.Now.AddMinutes(1)), default);
            await Assert.That(grant).IsNotNull();
            await setup.SaveChangesAsync();
            await Assert.That(await owner.RecheckIssuanceAsync(default)).IsTrue();
            await transaction.CommitAsync();
            await Assert.That(grant!.IssuerUserId).IsEqualTo(issuerId);
            await Assert.That(grant.IssuerRoleGrantId).IsEqualTo(issuerRoleId);
            return grant.Id;
        }

        async Task AssertCleanAndUnchangedAsync()
        {
            await Assert.That(read.Database.CurrentTransaction).IsNull();
            await Assert.That(read.ModerationTrustDomainId).IsNull();
            await Assert.That(read.ChangeTracker.Entries().Any()).IsFalse();
            await Assert.That(read.IsTenantFilterBypassed).IsFalse();
            await Assert.That((await events.ToArrayAsync()).SequenceEqual(eventsBefore)).IsTrue();
            await Assert.That((await actors.ToArrayAsync()).SequenceEqual(actorsBefore)).IsTrue();
            await Assert.That((await services.ToArrayAsync()).SequenceEqual(servicesBefore)).IsTrue();
            await Assert.That((await grants.ToArrayAsync()).SequenceEqual(grantsBefore)).IsTrue();
            await Assert.That((await receipts.ToArrayAsync()).SequenceEqual(receiptsBefore)).IsTrue();
            await Assert.That((await sources.ToArrayAsync()).SequenceEqual(sourcesBefore)).IsTrue();
            await Assert.That((await roles.ToArrayAsync()).SequenceEqual(rolesBefore)).IsTrue();
            await Assert.That(await inspect.ModerationCases.CountAsync()).IsEqualTo(caseCount);
            await Assert.That(await inspect.ModerationCaseIntakeAcknowledgements.CountAsync()).IsEqualTo(acknowledgementCount);
            var retained = await inspect.ModerationCaseNarratives.AsNoTracking().SingleAsync(row => row.Id == narrative.Id);
            await Assert.That(retained.ProtectedMaterial.ToArray()).IsEquivalentTo(originalCiphertext);
            await Assert.That(retained.PurgeAt).IsEqualTo(narrative.PurgeAt);
            await Assert.That(issued.Token.ToArray()).IsEquivalentTo(originalToken);
        }
    }

    private sealed class NarrativeActorOrderProbe(Guid callerId, Guid issuerId, Guid issuerRoleId,
        Func<ExploreDbContext, CancellationToken, Task> mutate) : DbCommandInterceptor
    {
        public List<Guid> ActorAnchors { get; } = [];
        public Guid[] ActorsBeforeTenant { get; private set; } = [];
        public bool SawTenant { get; private set; }
        public bool CallerBeforeFirstActor { get; private set; }
        public bool OriginalIssuerBeforeFirstActor { get; private set; }
        public Guid? TrustAtFirstActor { get; private set; }
        public bool ObservedMutationCheckpoint { get; private set; }
        private readonly HashSet<Guid> _users = [];
        private bool _issuerRole;
        private bool _armed;
        private bool _late;
        private bool _narrativeRead;
        private bool _nativeReadAfterNarrative;
        private bool _mutating;

        public void Arm(bool late)
        {
            ActorAnchors.Clear();
            ActorsBeforeTenant = [];
            _users.Clear();
            SawTenant = CallerBeforeFirstActor = OriginalIssuerBeforeFirstActor = ObservedMutationCheckpoint = false;
            _issuerRole = _narrativeRead = _nativeReadAfterNarrative = false;
            TrustAtFirstActor = null;
            _late = late;
            _armed = true;
        }

        public void Disarm() => _armed = false;

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!_armed || _mutating || eventData.Context is not ExploreDbContext context) return result;
            var ids = command.Parameters.Cast<DbParameter>().Select(parameter => parameter.Value).OfType<Guid>().ToArray();
            if (Updates<User>(context, command)) _users.UnionWith(ids);
            if (Updates<PlatformUserRole>(context, command) && ids.Contains(issuerRoleId)) _issuerRole = true;
            if (Updates<Actor>(context, command))
            {
                if (ActorAnchors.Count == 0)
                {
                    CallerBeforeFirstActor = _users.Contains(callerId);
                    OriginalIssuerBeforeFirstActor = _users.Contains(issuerId) && _issuerRole;
                    TrustAtFirstActor = context.ModerationTrustDomainId;
                }
                ActorAnchors.AddRange(ids);
            }
            if (!SawTenant && Updates<Tenant>(context, command))
            {
                SawTenant = true;
                ActorsBeforeTenant = ActorAnchors.ToArray();
                if (!_late) await MutateOnceAsync(context, cancellationToken);
            }
            return result;
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (!_armed || _mutating || !_late || eventData.Context is not ExploreDbContext context) return result;
            if (Reads<ModerationCaseNarrative>(context, command)) _narrativeRead = true;
            if (_narrativeRead && Reads<Explore.Domain.Event>(context, command)) _nativeReadAfterNarrative = true;
            // The custody source read follows protected-row retrieval and its first native
            // recheck; authentication and the final native output recheck still must succeed.
            if (_nativeReadAfterNarrative && Reads<ModerationCaseTargetSource>(context, command))
                await MutateOnceAsync(context, cancellationToken);
            return result;
        }

        private async Task MutateOnceAsync(ExploreDbContext context, CancellationToken token)
        {
            if (ObservedMutationCheckpoint) return;
            ObservedMutationCheckpoint = true;
            _mutating = true;
            try { await mutate(context, token); }
            finally { _mutating = false; }
        }

        private static bool Updates<TEntity>(ExploreDbContext context, DbCommand command) =>
            command.CommandText.StartsWith($"UPDATE {Table<TEntity>(context)}", StringComparison.Ordinal);

        private static bool Reads<TEntity>(ExploreDbContext context, DbCommand command) =>
            command.CommandText.Contains($"FROM {Table<TEntity>(context)} AS ", StringComparison.Ordinal);

        private static string Table<TEntity>(ExploreDbContext context)
        {
            var entity = context.Model.FindEntityType(typeof(TEntity))!;
            return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
        }
    }
}
