using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventResource;
using Explore.Application.Features.EventResources.Handlers.Commands;
using Explore.Application.Features.EventResources.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Event.Persistence.IntegrationTests;

public sealed partial class EventResourceManagementPersistenceTests
{
    [Test]
    public async Task NativeUpdateReplayDoesNotReplacePolicyOrDuplicateAudit()
    {
        var (scope, actor) = await SeedAsync();
        await using var context = database.CreateContext();
        var workflow = Workflow(context, scope.TenantAId, actor);
        var id = Guid.CreateVersion7();
        var create = await new CreateEventResourceCommandHandler(workflow).ExecuteAsync(new(scope.EventAId, id, Draft()));
        await Assert.That(create.IsSuccess).IsTrue();
        var detail = await workflow.GetAsync(id, default);
        var command = new UpdateEventResourceCommand(id, detail.Value!.Version, Draft() with
        {
            Title = "Revised material", AudienceRules = [new(EventResourceAudienceKindEnum.EventStaff)]
        });
        var handler = new UpdateEventResourceCommandHandler(workflow);
        await Assert.That((await handler.ExecuteAsync(command)).IsSuccess).IsTrue();
        var replay = await handler.ExecuteAsync(command);
        await Assert.That(replay.FailureCode).IsEqualTo(FailureCodes.ConcurrencyConflict);
        await using var verify = database.CreateContext();
        var saved = await verify.EventResources.Include(value => value.AudienceRules).SingleAsync(value => value.Id == id);
        await Assert.That(saved.Title).IsEqualTo(command.Draft.Title);
        await Assert.That(saved.AudienceRules.Count).IsEqualTo(1);
        await Assert.That(saved.AudienceRules.Single().AudienceKindId).IsEqualTo((int)EventResourceAudienceKindEnum.EventStaff);
        await Assert.That(await verify.EventResourceAuditEntries.CountAsync(value => value.EventResourceId == id)).IsEqualTo(2);
    }

    [Test]
    public async Task FailedAuditRollsBackMetadataAndPolicyWithoutConsumingReplayIdentity()
    {
        var (scope, actor) = await SeedAsync();
        Guid id = Guid.CreateVersion7(), version;
        await using (var seed = database.CreateContext())
        {
            await Assert.That((await Workflow(seed, scope.TenantAId, actor)
                .CreateAsync(scope.EventAId, id, Draft(), default)).IsSuccess).IsTrue();
            version = (await seed.EventResources.SingleAsync(value => value.Id == id)).ConcurrencyStamp;
        }
        var revised = Draft() with
        {
            Title = "Audited revision", AudienceRules = [new(EventResourceAudienceKindEnum.Organizer)]
        };
        await using (var failing = database.CreateContext(new AuditFailure()))
            await Assert.That(async () => await Workflow(failing, scope.TenantAId, actor)
                .UpdateAsync(id, version, revised, default)).Throws<DbUpdateException>();
        await using (var rolledBack = database.CreateContext())
        {
            var previous = await rolledBack.EventResources.Include(value => value.AudienceRules)
                .SingleAsync(value => value.Id == id);
            await Assert.That(previous.Title).IsEqualTo(Draft().Title);
            await Assert.That(previous.ConcurrencyStamp).IsEqualTo(version);
            await Assert.That(previous.AudienceRules.Single().AudienceKindId).IsEqualTo((int)EventResourceAudienceKindEnum.Public);
            await Assert.That(await rolledBack.EventResourceAuditEntries.CountAsync(value => value.EventResourceId == id))
                .IsEqualTo(1);
        }
        await using (var resumed = database.CreateContext())
        {
            var workflow = Workflow(resumed, scope.TenantAId, actor);
            await Assert.That((await workflow.UpdateAsync(id, version, revised, default)).IsSuccess).IsTrue();
            await Assert.That((await workflow.UpdateAsync(id, version, revised, default)).IsSuccess).IsFalse();
        }
        await using var verify = database.CreateContext();
        var committed = await verify.EventResources.Include(value => value.AudienceRules)
            .SingleAsync(value => value.Id == id);
        await Assert.That(committed.Title).IsEqualTo(revised.Title);
        await Assert.That(committed.AudienceRules.Single().AudienceKindId).IsEqualTo((int)EventResourceAudienceKindEnum.Organizer);
        await Assert.That(await verify.EventResourceAuditEntries.CountAsync(value => value.EventResourceId == id))
            .IsEqualTo(2);
    }

    [Test]
    public async Task RevocationAfterAcceptedReadCannotReuseFinalMutationAuthority()
    {
        var (scope, actor) = await SeedAsync();
        await using var context = database.CreateIndependentContext();
        var unit = new RevokeAfterAcceptedRead(new EfCoreUnitOfWork(context), async () =>
        {
            await using var writer = database.CreateIndependentContext();
            var parent = await writer.Events.SingleAsync(value => value.Id == scope.EventAId);
            parent.OrganizerActorId = null;
            await writer.SaveChangesAsync();
        });
        var id = Guid.CreateVersion7();
        var result = await Workflow(context, scope.TenantAId, actor, unitOfWork: unit)
            .CreateAsync(scope.EventAId, id, Draft(), default);
        await Assert.That(result.IsSuccess).IsFalse();
        await using var verify = database.CreateContext();
        await Assert.That(await verify.EventResources.AnyAsync(value => value.Id == id)).IsFalse();
        await Assert.That(await verify.EventResourceAuditEntries.AnyAsync(value => value.EventResourceId == id)).IsFalse();
    }

    [Test]
    public async Task AudienceEditCannotRepublishAfterWithdrawalAndManagerRevocation()
    {
        var (scope, actor) = await SeedAsync();
        Guid id, version;
        await using (var seed = database.CreateContext())
        {
            var parent = await seed.Events.SingleAsync(value => value.Id == scope.EventAId);
            parent.Publish(Now);
            var resource = EventResourcePersistenceTests.CreateDraft(scope.TenantAId, scope.EventAId);
            id = resource.Id;
            resource.SetExternalDestination("protected-payload", 1, "https://resource.example.org",
                resource.ConcurrencyStamp, actor, Now);
            resource.Publish(new(scope.TenantAId, scope.EventAId, null, EventStatusEnum.Published,
                false, true, null, false, new(null, null, null, null)), true,
                resource.ConcurrencyStamp, actor, Now);
            seed.EventResources.Add(resource);
            await seed.SaveChangesAsync();
            version = resource.ConcurrencyStamp;
        }
        await using var context = database.CreateIndependentContext();
        var unit = new RevokeAfterAcceptedRead(new EfCoreUnitOfWork(context), async () =>
        {
            await using var writer = database.CreateIndependentContext();
            var parent = await writer.Events.SingleAsync(value => value.Id == scope.EventAId);
            parent.OrganizerActorId = null;
            var resource = await writer.EventResources.SingleAsync(value => value.Id == id);
            resource.Withdraw(resource.ConcurrencyStamp, actor, Now);
            await writer.SaveChangesAsync();
        });
        var result = await Workflow(context, scope.TenantAId, actor,
            Policy(origins: ["https://resource.example.org"]), unitOfWork: unit)
            .UpdateAsync(id, version, Draft() with
            {
                Title = "Stale audience revision",
                DeliveryType = EventResourceDeliveryTypeEnum.ExternalLink,
                AudienceRules = [new(EventResourceAudienceKindEnum.Organizer)]
            }, default);
        await Assert.That(result.IsSuccess).IsFalse();
        await using var verify = database.CreateContext();
        var current = await verify.EventResources.Include(value => value.AudienceRules)
            .SingleAsync(value => value.Id == id);
        await Assert.That(current.PublicationStateId).IsEqualTo((int)EventResourcePublicationStateEnum.Withdrawn);
        await Assert.That(current.Title).IsNotEqualTo("Stale audience revision");
        await Assert.That(current.AudienceRules.Single().AudienceKindId).IsEqualTo((int)EventResourceAudienceKindEnum.Public);
        await Assert.That(current.ExternalDestinationCiphertext).IsEqualTo("protected-payload");
        await Assert.That((await verify.Events.SingleAsync(value => value.Id == scope.EventAId)).OrganizerActorId).IsNull();
        await Assert.That(await verify.EventResourceAuditEntries.AnyAsync(value => value.EventResourceId == id)).IsFalse();
    }

    [Test]
    public async Task DraftPublicationCannotSurviveManagerGrantRemovalBeforeFinalMutation()
    {
        var (scope, actor) = await SeedAsync();
        Guid id, version;
        await using (var seed = database.CreateContext())
        {
            var parent = await seed.Events.SingleAsync(value => value.Id == scope.EventAId);
            parent.Publish(Now);
            var resource = EventResourcePersistenceTests.CreateDraft(scope.TenantAId, scope.EventAId);
            id = resource.Id;
            resource.SetExternalDestination("protected-payload", 1, "https://resource.example.org",
                resource.ConcurrencyStamp, actor, Now);
            seed.EventResources.Add(resource);
            await seed.SaveChangesAsync();
            version = resource.ConcurrencyStamp;
        }
        await using var context = database.CreateIndependentContext();
        var unit = new RevokeAfterAcceptedRead(new EfCoreUnitOfWork(context), async () =>
        {
            await using var writer = database.CreateIndependentContext();
            var parent = await writer.Events.SingleAsync(value => value.Id == scope.EventAId);
            parent.OrganizerActorId = null;
            await writer.SaveChangesAsync();
        });
        var protector = Substitute.For<IEventResourceDestinationProtector>();
        protector.Unprotect(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>())
            .Returns("https://resource.example.org/handout");
        var result = await Workflow(context, scope.TenantAId, actor,
            Policy(origins: ["https://resource.example.org"]), unitOfWork: unit)
            .ChangeStateAsync(id, version, EventResourceManagementAction.Publish, default, protector);
        await Assert.That(result.IsSuccess).IsFalse();
        await using var verify = database.CreateContext();
        var current = await verify.EventResources.SingleAsync(value => value.Id == id);
        await Assert.That(current.PublicationStateId).IsEqualTo((int)EventResourcePublicationStateEnum.Draft);
        await Assert.That(current.ConcurrencyStamp).IsEqualTo(version);
        await Assert.That((await verify.Events.SingleAsync(value => value.Id == scope.EventAId)).OrganizerActorId).IsNull();
        await Assert.That(await verify.EventResourceAuditEntries.AnyAsync(value => value.EventResourceId == id)).IsFalse();
    }

    [Test]
    public async Task ConcurrentCreateAtLastGovernedSlotCannotOvershoot()
    {
        var (scope, actor) = await SeedAsync();
        await using (var seed = database.CreateContext())
        {
            seed.EventResources.AddRange(Enumerable.Range(0, 499)
                .Select(_ => EventResourcePersistenceTests.CreateDraft(scope.TenantAId, scope.EventAId)));
            await seed.SaveChangesAsync();
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var bothAtProvider = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int entered = 0;
        var provider = Substitute.For<IEventResourceAuthorizationProvider>();
        provider.CheckAsync(Arg.Any<EventResourceProviderInput>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            if (Interlocked.Increment(ref entered) == 2) bothAtProvider.TrySetResult();
            await bothAtProvider.Task.WaitAsync(call.ArgAt<CancellationToken>(1));
            return EventResourceProviderDecision.Allow;
        });
        async Task<BaseCommandResponse<Guid>> Create()
        {
            await using var context = database.CreateIndependentContext();
            return await Workflow(context, scope.TenantAId, actor, Policy(), provider)
                .CreateAsync(scope.EventAId, Guid.CreateVersion7(), Draft(), timeout.Token);
        }
        var results = await Task.WhenAll(Task.Run(Create, timeout.Token), Task.Run(Create, timeout.Token))
            .WaitAsync(timeout.Token);
        await Assert.That(results.Count(result => result.IsSuccess)).IsEqualTo(1);
        await using var verify = database.CreateContext();
        await Assert.That(await verify.EventResources.CountAsync(value => value.EventId == scope.EventAId)).IsEqualTo(500);
        var id = results.Single(value => value.IsSuccess).Id;
        await Assert.That(await verify.EventResourceAuditEntries.CountAsync(value => value.EventResourceId == id)).IsEqualTo(1);
    }

    [Test]
    public async Task ForeignSessionTicketTargetAndAlternativeReferencesFailBeforePersistence()
    {
        var (scope, actor) = await SeedAsync();
        await using var context = database.CreateContext();
        var workflow = Workflow(context, scope.TenantAId, actor);
        var hiddenAlternative = EventResourcePersistenceTests.CreateDraft(scope.TenantAId, scope.EventCId);
        context.EventResources.Add(hiddenAlternative);
        await context.SaveChangesAsync();
        EventResourceDraftDto[] invalid =
        [
            Draft() with { EventSessionId = scope.SessionBId },
            Draft() with { EventSessionId = scope.SessionAId, AudienceRules = [new(EventResourceAudienceKindEnum.SessionSpeaker, scope.SessionA2Id)] },
            Draft() with { AudienceRules = [new(EventResourceAudienceKindEnum.TicketHolder, scope.SessionAId, scope.CatalogBId, scope.TicketTypeBId)] },
            Draft() with { AudienceRules = [new(EventResourceAudienceKindEnum.TicketHolder, scope.SessionAId, scope.CatalogBId, scope.TicketTypeAId)] },
            Draft() with { AudienceRules = [new(EventResourceAudienceKindEnum.CheckedInParticipant,
                AdmissionTargetType: AdmissionTargetTypeEnum.EventDay, AdmissionTargetId: scope.TargetCId, AdmissionTargetScopeId: scope.DayCId)] },
            Draft() with { AccessibleAlternativeEventResourceId = Guid.CreateVersion7() },
            Draft() with { AccessibleAlternativeEventResourceId = hiddenAlternative.Id }
        ];
        foreach (var draft in invalid)
        {
            var result = await new CreateEventResourceCommandHandler(workflow)
                .ExecuteAsync(new(scope.EventAId, Guid.CreateVersion7(), draft));
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Errors).IsNotNull();
        }
        await using var verify = database.CreateContext();
        await Assert.That(await verify.EventResources.AnyAsync(value => value.EventId == scope.EventAId)).IsFalse();
    }

    [Test]
    public async Task AuditExpiryAndZeroRetentionAreEnforcedWithoutWaitingForSweep()
    {
        var (scope, actor) = await SeedAsync();
        await using var context = database.CreateContext();
        var id = Guid.CreateVersion7();
        var workflow = Workflow(context, scope.TenantAId, actor);
        await Assert.That((await workflow.CreateAsync(scope.EventAId, id, Draft(), default)).IsSuccess).IsTrue();
        var expired = EventResourceAuditEntry.Create(scope.TenantAId, id, actor, EventResourceAuditAction.UpdateMetadata,
            EventResourceAuditOutcome.Succeeded, EventResourceAuditReason.OrganizerMutation, Now.AddDays(-30));
        context.EventResourceAuditEntries.Add(expired);
        await context.SaveChangesAsync();
        var audit = await workflow.GetAuditAsync(id, 100, default);
        await Assert.That(audit.Value!.Items.Length).IsEqualTo(1);
        await Assert.That(audit.Value.Items.Any(value => value.Id == expired.Id)).IsFalse();
        var zero = await Workflow(context, scope.TenantAId, actor, Policy(retention: 0)).GetAuditAsync(id, 100, default);
        await Assert.That(zero.Value!.Items.Length).IsEqualTo(0);
        var collection = await Workflow(context, scope.TenantAId, actor, Policy(capacity: 0))
            .ListAsync(scope.EventAId, 1, 20, default);
        await Assert.That(collection.Value!.Items.Select(item => item.Id).SequenceEqual([id])).IsTrue();
    }

    [Test]
    public async Task BothPlaceholderTypesRemainUnpublishableAndArchiveIsTerminal()
    {
        var (scope, actor) = await SeedAsync();
        await using var context = database.CreateContext();
        var workflow = Workflow(context, scope.TenantAId, actor, Policy(capacity: 1));
        foreach (var type in Enum.GetValues<EventResourceDeliveryTypeEnum>())
        {
            var id = Guid.CreateVersion7();
            var draft = Draft() with { DeliveryType = type };
            await Assert.That((await workflow.CreateAsync(scope.EventAId, id, draft, default)).IsSuccess).IsTrue();
            var detail = (await workflow.GetAsync(id, default)).Value!;
            await Assert.That((await workflow.ChangeStateAsync(id, detail.Version, EventResourceManagementAction.Publish, default)).IsSuccess).IsFalse();
            await Assert.That((await workflow.ChangeStateAsync(id, detail.Version, EventResourceManagementAction.Archive, default)).IsSuccess).IsTrue();
            var archived = (await workflow.GetAsync(id, default)).Value!;
            await Assert.That((await workflow.UpdateAsync(id, archived.Version, draft, default)).IsSuccess).IsFalse();
            await Assert.That((await workflow.ChangeStateAsync(id, archived.Version, EventResourceManagementAction.Delete, default)).IsSuccess).IsTrue();
            var tombstone = await new EventResourceRepository(context).GetReplayIdentityAsync(scope.TenantAId, id, default);
            await Assert.That(tombstone!.IsDeleted).IsTrue();
            await Assert.That(tombstone.PublicationStateId).IsEqualTo((int)EventResourcePublicationStateEnum.Archived);
        }
    }

    private sealed class RevokeAfterAcceptedRead(IUnitOfWork inner, Func<Task> revoke) : IUnitOfWork
    {
        private int _serializableReads;
        public async Task<T> ExecuteSerializableAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
        {
            var value = await inner.ExecuteSerializableAsync(operation, ct);
            if (++_serializableReads == 2) await revoke();
            return value;
        }
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default) => inner.ExecuteInTransactionAsync(operation, ct);
        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) => inner.ExecuteInTransactionAsync(operation, ct);
        public Task<T> ExecuteReadCommittedAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) => inner.ExecuteReadCommittedAsync(operation, ct);
    }
}
