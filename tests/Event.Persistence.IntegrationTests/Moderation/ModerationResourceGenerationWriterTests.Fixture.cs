using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using DomainEvent = Explore.Domain.Event;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationResourceGenerationWriterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    internal static async Task<Guid> SeedAsync(SourceDatabase database, bool missingStoredObject = false, bool storedFile = false)
    {
        await using var context = database.Context();
        var tenant = await context.Tenants.SingleAsync(row => row.Id == database.Scope.Id);
        var actorId = Guid.CreateVersion7();
        var actor = new Actor
        {
            Id = actorId, ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!,
            UserId = database.SubjectId, Pii = new ActorPii { ActorId = actorId, DisplayName = "Native resource publisher" }
        };
        var owningEvent = new DomainEvent(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.Id, Tenant = tenant, ActorId = actor.Id, Actor = actor,
            Title = "Native resource generation", SubmittedByUserId = database.SubjectId,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            VisibilityTypeId = (int)VisibilityTypeEnum.Private, VisibilityType = null!,
            EventFormatId = (int)EventFormatEnum.Local, EventFormat = null!, EventStatus = null!
        };
        var resourceId = Guid.CreateVersion7();
        var resource = EventResource.CreateDraft(resourceId, tenant.Id, owningEvent.Id, null,
            new EventResourceMetadata
            {
                Title = "Native resource", Kind = EventResourceKindEnum.GeneralDocument,
                DisclosureMode = EventResourceDisclosureModeEnum.EligibleOnly
            }, missingStoredObject || storedFile ? EventResourceDeliveryTypeEnum.StoredFile : EventResourceDeliveryTypeEnum.ExternalLink,
            EventResourceAvailability.Create(),
            [EventResourceAudienceRule.Create(tenant.Id, owningEvent.Id, resourceId, EventResourceAudienceKindEnum.Organizer)],
            database.AdministratorId, Now.UtcDateTime);
        if (storedFile)
        {
            var binding = StorageProviderBinding.Local(Path.GetTempPath());
            var fileType = await context.Set<FileType>().FirstAsync();
            var storage = new StorageObject
            {
                Id = Guid.CreateVersion7(), FileTypeId = fileType.Id, FileType = fileType, Provider = binding.Provider,
                StorageProviderBindingId = binding.Id, FullName = "Native observation document", SafeDisplayName = "Native document",
                Extension = ".pdf", TenantId = tenant.Id, Tenant = tenant, Visibility = StorageObjectVisibilities.PrivateOwner,
                Purpose = StorageObjectPurposes.EventResource, LifecycleState = StorageObjectLifecycleStates.Active,
                OwningResourceKind = StorageOwningResourceKinds.EventResource, OwningResourceId = resource.Id
            };
            resource.SetStoredFile(storage.Id, resource.ConcurrencyStamp, database.AdministratorId, Now.UtcDateTime);
            context.AddRange(binding, storage);
        }
        else if (!missingStoredObject)
            resource.SetExternalDestination("encrypted-test-envelope", 1, "https://example.invalid",
                resource.ConcurrencyStamp, database.AdministratorId, Now.UtcDateTime);
        context.AddRange(actor, owningEvent, resource);
        await context.SaveChangesAsync();
        return resource.Id;
    }
}
