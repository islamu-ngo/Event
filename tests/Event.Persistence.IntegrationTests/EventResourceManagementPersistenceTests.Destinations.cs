using System.Text.Json;
using Explore.Application.Contracts.Services;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Event.Persistence.IntegrationTests;

public sealed partial class EventResourceManagementPersistenceTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task StoredDestinationConversionRetiresOnlyUnsharedTarget(bool shared)
    {
        var (scope, actor) = await SeedAsync();
        var resource = EventResourcePersistenceTests.CreateDraft(scope.TenantAId, scope.EventAId);
        resource.SetStoredFile(scope.StorageAId, resource.ConcurrencyStamp, actor, Now);
        await using (var seed = database.CreateContext())
        {
            seed.EventResources.Add(resource);
            if (shared)
            {
                var participation = new OrganizationTenant
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = scope.TenantAId,
                    Tenant = null!,
                    Organization = new Organization
                    {
                        Id = Guid.CreateVersion7(),
                        Pii = new OrganizationPii { FullName = "Retained document owner" }
                    },
                    ApprovalStatusId = (int)ApprovalStatusEnum.Pending,
                    ApprovalStatus = null!
                };
                var document = await seed.StorageObjects.SingleAsync(row => row.Id == scope.StorageAId);
                seed.OrganizationTenantEvidence.Add(
                    OrganizationTenantEvidence.CreatePending(participation, document));
            }
            await seed.SaveChangesAsync();
        }
        await using var context = database.CreateContext();
        var workflow = Workflow(context, scope.TenantAId, actor,
            Policy(origins: ["https://files.example.com"]));
        var protector = Substitute.For<IEventResourceDestinationProtector>();
        protector.CurrentVersion.Returns(1);
        protector.Protect(Arg.Any<string>(), scope.TenantAId, resource.Id, 1)
            .Returns("opaque-conversion-envelope");

        var result = await workflow.ConfigureExternalDestinationAsync(resource.Id,
            resource.ConcurrencyStamp, "https://files.example.com/material", protector, default);

        await Assert.That(result.IsSuccess).IsTrue();
        await using var verify = database.CreateContext();
        var converted = await verify.EventResources.SingleAsync(row => row.Id == resource.Id);
        await Assert.That(converted.StorageObjectId).IsNull();
        await Assert.That(converted.EventResourceDeliveryTypeId)
            .IsEqualTo((int)EventResourceDeliveryTypeEnum.ExternalLink);
        if (shared)
        {
            var source = await verify.StorageObjects.SingleAsync(row => row.Id == scope.StorageAId);
            await Assert.That(source.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.Active);
            await Assert.That(await verify.StorageObjectDeletionTombstones.AnyAsync(row => row.Id == source.Id))
                .IsFalse();
            await Assert.That(await verify.OrganizationTenantEvidence.CountAsync(row => row.DocumentStorageObjectId == source.Id))
                .IsEqualTo(1);
        }
        else
        {
            await Assert.That(await verify.StorageObjects.AnyAsync(row => row.Id == scope.StorageAId)).IsFalse();
            var custody = await verify.StorageObjectDeletionTombstones.SingleAsync(row => row.Id == scope.StorageAId);
            await Assert.That(custody.State).IsEqualTo(StorageObjectDeletionState.Ready);
            await Assert.That(custody.ProviderBindingId).IsNotEqualTo(Guid.Empty);
            await Assert.That(custody.ObjectKey).IsNotNull();
        }
    }

    [Test]
    public async Task ProtectedDestinationWriteCommitsOpaquePayloadAndAuditBeforeExternalPublication()
    {
        var (scope, actor) = await SeedAsync();
        await using var context = database.CreateContext();
        var workflow = Workflow(context, scope.TenantAId, actor,
            Policy(origins: ["https://files.example.com"]));
        var draft = Draft() with { DeliveryType = EventResourceDeliveryTypeEnum.ExternalLink };
        var resourceId = Guid.CreateVersion7();
        await Assert.That((await workflow.CreateAsync(scope.EventAId, resourceId, draft, default)).IsSuccess).IsTrue();
        var version = (await context.EventResources.SingleAsync(item => item.Id == resourceId)).ConcurrencyStamp;
        var protector = Substitute.For<IEventResourceDestinationProtector>();
        protector.CurrentVersion.Returns(1);
        protector.Protect(Arg.Any<string>(), scope.TenantAId, resourceId, 1).Returns("opaque-protected-envelope");
        string secret = Guid.CreateVersion7().ToString("N");
        string input = $"https://files.example.com/share?token={secret}#page=2";
        protector.Unprotect("opaque-protected-envelope", scope.TenantAId, resourceId, 1).Returns(input);

        var saved = await workflow.ConfigureExternalDestinationAsync(resourceId, version, input, protector, default);

        await Assert.That(saved.IsSuccess).IsTrue();
        await using var verify = database.CreateContext();
        var resource = await verify.EventResources.SingleAsync(item => item.Id == resourceId);
        await Assert.That(resource.ExternalDestinationCiphertext).IsEqualTo("opaque-protected-envelope");
        await Assert.That(resource.ExternalDestinationSafeOrigin).IsEqualTo("https://files.example.com");
        await Assert.That(resource.ExternalDestinationProtectionVersion).IsEqualTo(1);
        await Assert.That(resource.StorageObjectId).IsNull();
        await Assert.That(JsonSerializer.Serialize(await verify.EventResourceAuditEntries.SingleAsync(item =>
            item.EventResourceId == resourceId && item.Action == EventResourceAuditAction.ConfigureDelivery)))
            .DoesNotContain(secret);
        var detail = (await workflow.GetAsync(resourceId, default)).Value!;
        await Assert.That(detail.ExternalDestinationSafeOrigin).IsEqualTo("https://files.example.com");
        string serialized = JsonSerializer.Serialize(detail);
        await Assert.That(serialized).DoesNotContain(secret);
        await Assert.That(serialized).DoesNotContain("opaque-protected-envelope");

        var parent = await context.Events.SingleAsync(item => item.Id == scope.EventAId);
        parent.Publish(Now);
        await context.SaveChangesAsync();
        var published = await workflow.ChangeStateAsync(resourceId, resource.ConcurrencyStamp,
            EventResourceManagementAction.Publish, default, protector);
        await Assert.That(published.IsSuccess).IsTrue();
    }

    [Test]
    public async Task HostileDestinationFailsWithoutPersistingItOrEchoingInput()
    {
        var (scope, actor) = await SeedAsync();
        await using var context = database.CreateContext();
        var workflow = Workflow(context, scope.TenantAId, actor,
            Policy(origins: ["https://files.example.com"]));
        var resourceId = Guid.CreateVersion7();
        await Assert.That((await workflow.CreateAsync(scope.EventAId, resourceId,
            Draft() with { DeliveryType = EventResourceDeliveryTypeEnum.ExternalLink }, default)).IsSuccess).IsTrue();
        var version = (await context.EventResources.SingleAsync(item => item.Id == resourceId)).ConcurrencyStamp;
        var protector = Substitute.For<IEventResourceDestinationProtector>();
        protector.CurrentVersion.Returns(1);
        string secret = Guid.CreateVersion7().ToString("N");

        var failed = await workflow.ConfigureExternalDestinationAsync(resourceId, version,
            $"https://visitor:{secret}@files.example.com/share", protector, default);

        await Assert.That(failed.IsSuccess).IsFalse();
        await Assert.That(failed.Message ?? string.Empty).DoesNotContain(secret);
        await using var verify = database.CreateContext();
        await Assert.That((await verify.EventResources.SingleAsync(item => item.Id == resourceId))
            .ExternalDestinationCiphertext).IsNull();
        await Assert.That(await verify.EventResourceAuditEntries.CountAsync(item =>
            item.EventResourceId == resourceId && item.Action == EventResourceAuditAction.ConfigureDelivery))
            .IsEqualTo(0);
    }
}
