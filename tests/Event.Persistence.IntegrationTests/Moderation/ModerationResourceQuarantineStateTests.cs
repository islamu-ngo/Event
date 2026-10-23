using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using DomainEvent = Explore.Domain.Event;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

/// <summary>
/// Native operational-state witnesses, not maintenance execution conformance.
/// The tenant-isolation witness also requires the parent's central query-filter hook.
/// </summary>
public sealed class ModerationResourceQuarantineStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FreshNativeReadRetainsIndependentCausesAndBlocksUnloadedDelivery(bool stored)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var resourceId = await SeedAsync(database, stored);
        Guid materialId;
        Guid firstId;
        var effectId = Guid.CreateVersion7();
        await using (var writer = database.Context())
        {
            var resource = await writer.EventResources.Include(row => row.ModerationQuarantines)
                .SingleAsync(row => row.Id == resourceId);
            materialId = resource.MaterialVersionId!.Value;
            firstId = Apply(resource, effectId, 0).Id;
            Apply(resource, effectId, 1);
            Apply(resource, Guid.CreateVersion7(), 0);
            await writer.SaveChangesAsync();
        }
        await using (var reader = database.Context())
        {
            var resource = await reader.EventResources.SingleAsync(row => row.Id == resourceId);
            await Assert.That(resource.MaterialVersionId).IsEqualTo(materialId);
            await Assert.That(resource.ModerationQuarantineCount).IsEqualTo(3);
            await Assert.That(resource.HasModerationQuarantine).IsTrue();
            await Assert.That(resource.HasPublishablePayload()).IsFalse();
            await Assert.That(resource.ModerationQuarantines.Count).IsEqualTo(0);
            await Assert.That(() => resource.SetExternalDestination("protected-replacement", 1, "https://other.example",
                resource.ConcurrencyStamp, database.AdministratorId, Now.UtcDateTime)).Throws<InvalidOperationException>();
            await Assert.That(() => Apply(resource, Guid.CreateVersion7(), 0)).Throws<InvalidOperationException>();
            await reader.Entry(resource).Collection(row => row.ModerationQuarantines).LoadAsync();
            var first = resource.ModerationQuarantines.Single(row => row.Id == firstId);
            var replay = resource.ApplyModerationQuarantine(firstId, effectId, 0, materialId,
                resource.ConcurrencyStamp, Now.AddYears(1));
            await Assert.That(replay.AppliedAt).IsEqualTo(Now);
            await Assert.That(resource.ModerationQuarantines.Count).IsEqualTo(3);
            await Assert.That(await reader.ModerationEffectIntents.CountAsync()).IsEqualTo(0);
        }
    }

    [Test]
    public async Task NativeStateAndCauseReceiptsRollbackTogether()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var resourceId = await SeedAsync(database, false);
        await using (var writer = database.Context())
        {
            await using var transaction = await writer.Database.BeginTransactionAsync();
            var resource = await writer.EventResources.Include(row => row.ModerationQuarantines)
                .SingleAsync(row => row.Id == resourceId);
            Apply(resource, Guid.CreateVersion7(), 0);
            await writer.SaveChangesAsync();
            await transaction.RollbackAsync();
        }
        await using var reader = database.Context();
        var retained = await reader.EventResources.Include(row => row.ModerationQuarantines)
            .SingleAsync(row => row.Id == resourceId);
        await Assert.That(retained.ModerationQuarantineCount).IsEqualTo(0);
        await Assert.That(retained.ModerationQuarantines.Count).IsEqualTo(0);
        await Assert.That(retained.HasPublishablePayload()).IsTrue();
    }

    [Test]
    public async Task StaleOwnerCannotOverwriteAnotherCauseAndFreshRetryPreservesBoth()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var resourceId = await SeedAsync(database, false);
        await using var first = database.Context();
        await using var stale = database.Context();
        var original = await first.EventResources.Include(row => row.ModerationQuarantines)
            .SingleAsync(row => row.Id == resourceId);
        var competing = await stale.EventResources.Include(row => row.ModerationQuarantines)
            .SingleAsync(row => row.Id == resourceId);
        Apply(original, Guid.CreateVersion7(), 0);
        var secondEffect = Guid.CreateVersion7();
        Apply(competing, secondEffect, 0);
        await first.SaveChangesAsync();
        await Assert.That(async () => await stale.SaveChangesAsync()).Throws<DbUpdateConcurrencyException>();
        await using var retry = database.Context();
        var current = await retry.EventResources.Include(row => row.ModerationQuarantines)
            .SingleAsync(row => row.Id == resourceId);
        await Assert.That(current.ModerationQuarantineCount).IsEqualTo(1);
        Apply(current, secondEffect, 0);
        await retry.SaveChangesAsync();
        await using var reader = database.Context();
        var result = await reader.EventResources.Include(row => row.ModerationQuarantines)
            .SingleAsync(row => row.Id == resourceId);
        await Assert.That(result.ModerationQuarantines.Count).IsEqualTo(2);
        await Assert.That(result.ModerationQuarantineCount).IsEqualTo(2);
    }

    [Test]
    public async Task CentralTenantFilterMustHideOperationalReceiptsFromAnotherTenant()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var resourceId = await SeedAsync(database, false);
        Guid receiptId;
        await using (var writer = database.Context())
        {
            var resource = await writer.EventResources.Include(row => row.ModerationQuarantines)
                .SingleAsync(row => row.Id == resourceId);
            receiptId = Apply(resource, Guid.CreateVersion7(), 0).Id;
            await writer.SaveChangesAsync();
        }
        await using var reader = database.Context();
        reader.TenantContext = new SelectedTenant(Guid.CreateVersion7());
        await Assert.That(await reader.Set<EventResourceModerationQuarantine>()
            .AnyAsync(row => row.Id == receiptId)).IsFalse();
        reader.TenantContext = database;
        await Assert.That(await reader.Set<EventResourceModerationQuarantine>()
            .AnyAsync(row => row.Id == receiptId)).IsTrue();
    }

    private static EventResourceModerationQuarantine Apply(EventResource resource, Guid effectId, int ordinal) =>
        resource.ApplyModerationQuarantine(Guid.CreateVersion7(), effectId, ordinal,
            resource.MaterialVersionId!.Value, resource.ConcurrencyStamp, Now);

    private static async Task<Guid> SeedAsync(SourceDatabase database, bool stored)
    {
        await using var context = database.Context();
        var tenant = await context.Tenants.SingleAsync(row => row.Id == database.TenantId);
        var actorId = Guid.CreateVersion7();
        var actor = new Actor
        {
            Id = actorId, ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!,
            UserId = database.SubjectId, Pii = new ActorPii { ActorId = actorId, DisplayName = "Resource owner" }
        };
        var owningEvent = new DomainEvent(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.Id, Tenant = tenant, ActorId = actor.Id, Actor = actor,
            Title = "Resource quarantine", SubmittedByUserId = database.SubjectId,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            VisibilityTypeId = (int)VisibilityTypeEnum.Private, VisibilityType = null!,
            EventFormatId = (int)EventFormatEnum.Local, EventFormat = null!, EventStatus = null!
        };
        var resourceId = Guid.CreateVersion7();
        var resource = EventResource.CreateDraft(resourceId, tenant.Id, owningEvent.Id, null,
            new EventResourceMetadata
            {
                Title = "Material", Kind = EventResourceKindEnum.GeneralDocument,
                DisclosureMode = EventResourceDisclosureModeEnum.EligibleOnly
            }, stored ? EventResourceDeliveryTypeEnum.StoredFile : EventResourceDeliveryTypeEnum.ExternalLink,
            EventResourceAvailability.Create(),
            [EventResourceAudienceRule.Create(tenant.Id, owningEvent.Id, resourceId, EventResourceAudienceKindEnum.Public)],
            database.AdministratorId, Now.UtcDateTime);
        if (stored)
        {
            var binding = StorageProviderBinding.Local(Path.GetTempPath());
            var fileType = await context.Set<FileType>().FirstAsync();
            var storage = new StorageObject
            {
                Id = Guid.CreateVersion7(), FileTypeId = fileType.Id, FileType = fileType, Provider = binding.Provider,
                StorageProviderBindingId = binding.Id, FullName = "Material", SafeDisplayName = "Material",
                Extension = ".pdf", TenantId = tenant.Id, Tenant = tenant, Visibility = StorageObjectVisibilities.PrivateOwner,
                Purpose = StorageObjectPurposes.EventResource, LifecycleState = StorageObjectLifecycleStates.Active,
                OwningResourceKind = StorageOwningResourceKinds.EventResource, OwningResourceId = resourceId
            };
            resource.SetStoredFile(storage.Id, resource.ConcurrencyStamp, database.AdministratorId, Now.UtcDateTime);
            context.AddRange(binding, storage);
        }
        else
            resource.SetExternalDestination("protected-envelope", 1, "https://material.example",
                resource.ConcurrencyStamp, database.AdministratorId, Now.UtcDateTime);
        context.AddRange(actor, owningEvent, resource);
        await context.SaveChangesAsync();
        return resourceId;
    }

    private sealed class SelectedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId { get; } = tenantId;
    }
}
