using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Domain;
using Explore.Persistence.Repositories;
using Explore.Application.Authentication;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using System.Data;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed class ModerationNativeTargetCompositionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    [Arguments(8)]
    [Arguments(9)]
    [Arguments(10)]
    [Arguments(11)]
    [Arguments(12)]
    [Arguments(13)]
    public async Task MixedNativeApproval_RequiresBothIndependentOwnersAndTheEarliestDeadline(int change)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var resourceId = await ModerationResourceGenerationWriterTests.SeedAsync(database, storedFile: change >= 6);
        Guid eventId;
        await using (var reader = database.Context())
            eventId = await reader.Set<EventResource>().Where(row => row.Id == resourceId).Select(row => row.EventId).SingleAsync();
        var storageActorId = Guid.CreateVersion7();
        var replacementActorId = Guid.CreateVersion7();
        var expectedActors = new List<Guid>();
        if (change >= 6)
        {
            var eventResource = await ModerationResourceGenerationWriterTests.SeedAsync(database);
            await using var writer = database.Context();
            eventId = await writer.Set<EventResource>().Where(row => row.Id == eventResource).Select(row => row.EventId).SingleAsync();
            foreach (var (id, human) in new[] { (storageActorId, database.ReviewerId), (replacementActorId, database.UrgencyAssessorId) })
                writer.Actors.Add(new Actor
                {
                    Id = id,
                    ActorTypeId = (int)Explore.Domain.Enums.ActorTypeEnum.User,
                    UserId = human,
                    Pii = new ActorPii { FullName = "Native target ancestry" }
                });
            await writer.SaveChangesAsync();
            var storageId = await writer.Set<EventResource>().Where(row => row.Id == resourceId).Select(row => row.StorageObjectId).SingleAsync();
            await writer.StorageObjects.Where(row => row.Id == storageId).ExecuteUpdateAsync(
                update => update.SetProperty(row => row.ActorId, storageActorId));
            var resourceEventId = await writer.Set<EventResource>().Where(row => row.Id == resourceId).Select(row => row.EventId).SingleAsync();
            expectedActors.AddRange(await writer.Events.Where(row => row.Id == eventId || row.Id == resourceEventId)
                .Select(row => row.ActorId).ToListAsync());
            expectedActors.Add(storageActorId);
        }
        var resourceGenerationId = Guid.CreateVersion7();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, new RecordModerationResourceGenerationSource
        {
            OperationId = Guid.CreateVersion7(),
            SourceId = resourceGenerationId,
            Target = database.Scope,
            ExpectedQualificationRevision = 1,
            ResourceId = resourceId,
            ExpectedResourceRevision = 0,
            ValidUntil = Now.AddMinutes(10)
        })).IsSuccess).IsTrue();
        var eventGenerationId = Guid.CreateVersion7();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, new RecordModerationEventGenerationSource
        {
            OperationId = Guid.CreateVersion7(),
            SourceId = eventGenerationId,
            Target = database.Scope,
            ExpectedQualificationRevision = 2,
            EventId = eventId,
            ExpectedEventRevision = 0,
            ValidUntil = Now.AddMinutes(10)
        })).IsSuccess).IsTrue();
        var opened = database.OpenCase() with { ExpectedQualificationRevision = 3 };
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, opened)).IsSuccess).IsTrue();
        var request = database.Publication(4);
        var publication = await database.PublishAsync(database.AdministratorId, request);
        await Assert.That(publication.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        var profile = request.Proposed.Profiles.Single();
        var original = ModerationDecision.Propose(Guid.CreateVersion7(), opened.CaseId,
            SubjectReference.Create(ModerationSubjectKind.Human, database.SubjectId, database.Instance),
            SubjectReference.Create(ModerationSubjectKind.Human, database.AdministratorId, database.Instance),
            database.Scope, database.Scope, profile.Rule, [ModerationCapability.Publish], Now, Now.AddHours(1), null, 1, Now);
        await using (var writer = database.Context())
        {
            writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
            writer.ModerationDecisions.Add(original);
            await writer.SaveChangesAsync();
        }
        var resourceTarget = new ModerationPolicyTarget(database.Scope, original.Subject.Kind, original.Subject.Id,
            resourceId, 1, "resource.read", null);
        var eventTarget = new ModerationPolicyTarget(database.Scope, original.Subject.Kind, original.Subject.Id,
            eventId, 1, "event.publish", null);
        var selectionId = Guid.CreateVersion7();
        var effectId = Guid.CreateVersion7();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, new RecordModerationContributionSelectionSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 5,
            SelectionId = selectionId,
            EffectId = effectId,
            CaseId = opened.CaseId,
            ExpectedCaseRevision = 1,
            DecisionId = original.Id,
            ExpectedDecisionRevision = 2,
            PolicyId = request.Proposed.PolicyId,
            ExpectedPolicyRevision = 1,
            PublicationId = publication.Publication!.Id,
            ProfileId = profile.Id,
            Contribution = new(original.Id, original.Rule, original.Scope, original.Subject.Kind, original.Subject.Id,
                original.StartsAt, null,
                profile.Effects.Select(effect => new ModerationPolicyEffectTarget(effect, resourceTarget)).ToImmutableArray(),
                [new(null, eventTarget)]),
            ValidUntil = Now.AddMinutes(10)
        })).IsSuccess).IsTrue();
        ModerationContributionSelection selection;
        await using (var reader = database.Context())
        {
            reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
            selection = await reader.ModerationContributionSelections.Include(row => row.Bindings).SingleAsync();
        }
        var epoch = 6L;
        foreach (var binding in selection.Bindings.Where(row => row.TargetId == resourceId).OrderBy(row => row.Ordinal))
        {
            await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, new AdoptModerationResourceTargetSource
            {
                OperationId = Guid.CreateVersion7(),
                Target = database.Scope,
                ExpectedQualificationRevision = epoch++,
                AdoptionId = Guid.CreateVersion7(),
                SelectionId = selectionId,
                BindingOrdinal = binding.Ordinal,
                GenerationId = resourceGenerationId,
                ExpectedGenerationRevision = 1,
                CaseId = opened.CaseId,
                ExpectedCaseRevision = 1,
                ValidUntil = Now.AddMinutes(5)
            })).IsSuccess).IsTrue();
        }
        var eventAdoptionId = Guid.CreateVersion7();
        if (change != 1)
        {
            await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, new AdoptModerationEventTargetSource
            {
                OperationId = Guid.CreateVersion7(),
                Target = database.Scope,
                ExpectedQualificationRevision = epoch++,
                AdoptionId = eventAdoptionId,
                SelectionId = selectionId,
                BindingOrdinal = selection.Bindings.Single(row => row.TargetId == eventId).Ordinal,
                GenerationId = eventGenerationId,
                ExpectedGenerationRevision = 1,
                CaseId = opened.CaseId,
                ExpectedCaseRevision = 1,
                ValidUntil = Now.AddMinutes(4)
            })).IsSuccess).IsTrue();
        }
        await Assert.That((await database.ExecuteAsync(database.ReviewerId, new ApproveModerationNoticeDisclosureSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = epoch++,
            Intent = new(Guid.CreateVersion7(), effectId, original.Id, 2, original.CaseId, 1,
                selection.PolicyId, 1, publication.Publication.Id, selection.ProfileId, original.Id, selection.SelectionDigest,
                new("Native source classification."), new("Current source authority and protected response."),
                ImmutableArray.Create(new ModerationNoticeSafeText("Approved mixed native target explanation.")),
                new("Protected response remains available."), new("Request independent review."),
                new("No automated finding."), new("Purpose-bound retention."), Now.AddMinutes(10), Now.AddYears(1))
        })).IsSuccess).IsTrue();
        if (change == 2)
            await Assert.That((await database.ExecuteAsync(database.AdministratorId, new RevokeModerationEventTargetSource
            {
                OperationId = Guid.CreateVersion7(),
                Target = database.Scope,
                ExpectedQualificationRevision = epoch,
                AdoptionId = eventAdoptionId,
                ExpectedAdoptionRevision = 1,
                CaseId = opened.CaseId,
                ExpectedCaseRevision = 1
            })).IsSuccess).IsTrue();
        if (change is 3 or 4)
        {
            await using var writer = database.Context();
            if (change == 3)
                await writer.Events.Where(row => row.Id == eventId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ConcurrencyStamp, Guid.NewGuid()));
            else
                await writer.Set<EventResource>().Where(row => row.Id == resourceId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ConcurrencyStamp, Guid.NewGuid()));
        }
        var clock = new ObservationClock();
        ApiKeyPrincipalContext? principal = null;
        if (change >= 11)
        {
            await using var writer = database.Context();
            var host = await writer.ModerationHostBindings.AsNoTracking().SingleAsync();
            var keyId = ApiKeyHashing.CreateKeyId();
            var serviceActorId = Guid.CreateVersion7();
            writer.Actors.Add(new Actor
            {
                Id = serviceActorId,
                ActorTypeId = (int)ActorTypeEnum.System,
                ServicePrincipalId = host.MaintenanceServicePrincipalId,
                Pii = new ActorPii { FullName = "Installed maintenance actor" }
            });
            writer.ExternalApiKeys.Add(new ExternalApiKey
            {
                Id = Guid.CreateVersion7(),
                KeyId = keyId,
                Name = "Native target maintenance credential",
                SecretHash = ApiKeyHashing.ComputeHash(ApiKeyHashing.CreateSecret()),
                Scopes = "events:read",
                OwnerType = ExternalApiKeyOwnerType.InstanceAdmin,
                OwnerId = database.AdministratorId,
                ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.Active,
                ExternalApiKeyStatus = null!,
                ExternalApiKeyCreditPeriodId = (int)ExternalApiKeyCreditPeriodEnum.None,
                ExternalApiKeyCreditPeriod = null!
            });
            writer.ModerationMachineSubjectBindings.Add(ModerationMachineSubjectBinding.Establish(
                Guid.CreateVersion7(), host.Id, ModerationMachineCredentialSource.Persisted, keyId, null,
                ExternalApiKeyOwnerType.InstanceAdmin, database.AdministratorId, host.MaintenanceServicePrincipalId));
            await writer.SaveChangesAsync();
            expectedActors.Add(serviceActorId);
            principal = new(keyId, null, ExternalApiKeyOwnerType.InstanceAdmin, database.AdministratorId, [])
            { CredentialSource = ApiKeyCredentialSource.Persisted };
        }
        var checkpoint = new NativeParentsBeforeTenant(resourceId, eventId, replacementActorId, change);
        await using var context = database.Context(change >= 6 ? checkpoint : null);
        var privacyAuthority = database.Privacy.Authority(context, clock);
        var participant = database.Participant(context, database.SubjectId, clock);
        var operation = ModerationOperation.Transition(ModerationOperationKind.RecordResponse,
            original.Subject, database.Scope, Guid.CreateVersion7(), effectId, original.Id, 1, 1, Guid.Empty);
        var store = new ModerationStore(context, participant, clock, privacyAuthority, ModerationLedgerPersistenceTests.Capture(context, privacyAuthority, participant, clock));
        ModerationPolicyPublicationNoticeApproval? approval = null;
        if (principal is not null)
        {
            var http = new HttpContextAccessor();
            var machine = new MachinePrincipalAccessor(http);
            machine.SetPrincipal(principal);
            var admin = new AdminContext(http, new AdministrativeAuthorityRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context)),
                new OrganizationMemberRepository(context), new GroupMemberRepository(context), new ExternalLoginAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context), null),
                NullLogger<AdminContext>.Instance);
            var keys = new ExternalApiKeyAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context));
            var bindings = new ModerationMachineSubjectBindingRepository(context, admin, machine, keys,
                new ModerationNativeAuthorityParticipantTests.UnconfiguredKeys(), clock);
            await privacyAuthority.ExecuteSerializedAsync(async token =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                var acquired = await new ModerationMaintenanceAuthority(context, machine, bindings, keys, clock)
                    .AcquireAsync(ModerationOperation.Reconcile(original.Subject, database.Scope, Guid.CreateVersion7(), effectId,
                        original.Id, 1, 1, Guid.CreateVersion7(), 1), token);
                if (change == 13) await Assert.That(acquired).IsNull();
                else
                {
                    await Assert.That(acquired).IsNotNull();
                    await Assert.That(await acquired!.NativeTargets!.MatchesAsync(context, selection, token)).IsEqualTo(change == 11);
                }
                return true;
            }, CancellationToken.None);
        }
        else await store.ExecuteAsync(operation, async (session, token) =>
        {
            var resolved = await participant.ResolveAsync(session, operation, token);
            await Assert.That(resolved.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
            var decision = (await session.FindDecisionAsync(original.Id, original.Subject, database.Scope, token))!;
            decision.RecordResponse(1, Now);
            var effect = ModerationEffectIntent.Create(effectId, operation.OperationId, decision.Id, decision.Revision,
                null, null, database.Scope.TrustDomainId, (int)database.Scope.Kind, database.Scope.Id,
                (int)decision.Subject.Kind, decision.Subject.Id, (int)operation.Kind, 1,
                resolved.Grant!.QualificationRevision, Now, null, null);
            context.ModerationEffectIntents.Add(effect);
            if (change == 5) clock.UtcNow = Now.AddMinutes(4);
            if (change is 9 or 10)
            {
                var ordinal = selection.Bindings.Where(row => row.TargetId == (change == 9 ? resourceId : eventId))
                    .OrderBy(row => row.Ordinal).First().Ordinal;
                var observed = change == 9
                    ? await participant.AcquireResourceTargetObservationAsync(context.Database.CurrentTransaction!.TransactionId,
                        publication.Publication, effect, decision, resolved.Grant, ordinal, clock.UtcNow, token)
                    : await participant.AcquireEventTargetObservationAsync(context.Database.CurrentTransaction!.TransactionId,
                        publication.Publication, effect, decision, resolved.Grant, ordinal, clock.UtcNow, token);
                await Assert.That(observed).IsNotNull();
            }
            else approval = await ((IModerationPolicyPublicationNoticeAuthority)participant).AcquireAsync(
                    context.Database.CurrentTransaction!.TransactionId, publication.Publication, effect, decision,
                    null, resolved.Grant, clock.UtcNow, token);
            return ModerationOperationResult.Unavailable();
        }, CancellationToken.None);
        if (change >= 6)
        {
            await Assert.That(checkpoint.Observed).IsTrue();
            await Assert.That(expectedActors.All(checkpoint.BeforeTenant.Contains)).IsTrue();
            await Assert.That(checkpoint.BeforeTenant.Where(expectedActors.Contains).Distinct()
                .SequenceEqual(expectedActors.Distinct().Order())).IsTrue();
            await Assert.That(checkpoint.ActorAnchors.Contains(replacementActorId)).IsFalse();
        }
        if (change is 0 or 6)
        {
            await Assert.That(approval).IsNotNull();
            await Assert.That(approval!.Targets.Length).IsEqualTo(2);
            await Assert.That(approval.Targets.All(row => row.ApprovedSource == row.CurrentSource)).IsTrue();
            await Assert.That(approval.Targets.Select(row => row.Target.TargetId).Order().ToArray())
                .IsEquivalentTo(new[] { resourceId, eventId }.Order().ToArray());
            await Assert.That(approval.ValidUntil).IsEqualTo(Now.AddMinutes(4));
            await Assert.That(approval.Targets.All(row => row.ValidUntil == approval.ValidUntil)).IsTrue();
        }
        else await Assert.That(approval).IsNull();
        await using var unchanged = database.Context();
        unchanged.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That((await unchanged.ModerationDecisions.SingleAsync()).Revision).IsEqualTo(1L);
        await Assert.That(await unchanged.ModerationEffectIntents.CountAsync()).IsEqualTo(0);
    }

    private sealed class NativeParentsBeforeTenant(Guid resourceId, Guid eventId, Guid replacement, int change) : DbCommandInterceptor
    {
        public bool Observed { get; private set; }
        public List<Guid> ActorAnchors { get; } = [];
        public IReadOnlyList<Guid> BeforeTenant { get; private set; } = [];
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var context = (Explore.Persistence.ExploreDbContext)eventData.Context!;
            string Table(Type type)
            {
                var entity = context.Model.FindEntityType(type)!;
                return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
            }
            if (command.CommandText.StartsWith($"UPDATE {Table(typeof(Actor))}", StringComparison.Ordinal))
                ActorAnchors.AddRange(command.Parameters.Cast<DbParameter>().Select(row => row.Value).OfType<Guid>());
            if (!Observed && command.CommandText.StartsWith($"UPDATE {Table(typeof(Tenant))}", StringComparison.Ordinal))
            {
                Observed = true;
                BeforeTenant = ActorAnchors.ToArray();
                if (change == 13)
                    await context.ExternalApiKeys.ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAt, Now.UtcDateTime), cancellationToken);
                if (change == 7)
                    await context.Events.Where(row => row.Id == eventId).ExecuteUpdateAsync(
                        update => update.SetProperty(row => row.ActorId, replacement), cancellationToken);
                if (change is 8 or 12)
                {
                    var id = await context.Set<EventResource>().Where(row => row.Id == resourceId).Select(row => row.StorageObjectId).SingleAsync(cancellationToken);
                    await context.StorageObjects.Where(row => row.Id == id).ExecuteUpdateAsync(
                        update => update.SetProperty(row => row.ActorId, replacement), cancellationToken);
                }
            }
            return result;
        }
    }

    private sealed class ObservationClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
