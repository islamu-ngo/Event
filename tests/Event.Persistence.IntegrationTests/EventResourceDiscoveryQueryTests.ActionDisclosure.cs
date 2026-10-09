using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventResource;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests;

public sealed partial class EventResourceDiscoveryQueryTests
{
    [Test]
    [Arguments("download", false)]
    [Arguments("download", true)]
    [Arguments("access", false)]
    [Arguments("access", true)]
    [Arguments("view-management", false)]
    [Arguments("view-management", true)]
    public async Task PreparedAudienceActionCannotSurviveChangedAuthorityWhileViewRemainsAllowed(
        string action, bool collection)
    {
        var (scope, publisher) = await SeedAsync();
        var resource = await ActionResourceAsync(scope, publisher, action);
        bool actionAllowed = true;
        var provider = new Provider
        {
            Decide = input => input.Action == action && !actionAllowed
                ? EventResourceProviderDecision.Deny : EventResourceProviderDecision.Allow
        };
        await using var context = database.CreateContext();
        var workflow = Workflow(context, scope.TenantAId, publisher, provider,
            origins: ["https://resources.example.test"], allowUnscanned: true);
        var detail = collection ? null : await workflow.GetAsync(resource.Id, default);
        var page = collection ? await workflow.ListAsync(scope.EventAId, 100, null, default) : null;
        bool delivered = false;
        async Task<EventResourceAudienceDetailDto> Prepare(EventResourceAudienceDetailDto value)
        {
            await Assert.That(ActionAuthorized(value, action)).IsTrue();
            await Assert.That(value.Title).IsEqualTo(resource.Title);
            actionAllowed = false;
            return value;
        }
        var outcome = collection
            ? await page!.Projection!.DiscloseAsync((value, _) => Prepare(value.Items.Single()),
                (_, _) => { delivered = true; return Task.CompletedTask; })
            : await detail!.Projection!.DiscloseAsync((value, _) => Prepare(value),
                (_, _) => { delivered = true; return Task.CompletedTask; });
        await Assert.That(outcome).IsEqualTo(EventResourceAuthorityOutcome.NotFound);
        await Assert.That(delivered).IsFalse();
        // No metadata authority or resource version changed: a fresh representation is still allowed,
        // but must no longer offer the revoked action.
        var fresh = await workflow.GetAsync(resource.Id, default);
        await Assert.That(fresh.Failure).IsEqualTo(EventResourceAudienceFailure.None);
        var released = await DiscloseAsync(fresh.Projection);
        await Assert.That(released.Title).IsEqualTo(resource.Title);
        await Assert.That(ActionAuthorized(released, action)).IsFalse();
        await Assert.That((await context.EventResources.AsNoTracking()
            .SingleAsync(row => row.Id == resource.Id)).ConcurrencyStamp).IsEqualTo(resource.ConcurrencyStamp);
        await Assert.That(provider.Sizes.All(size => size <=
            EventResourceAuthorityRequest.MaximumBatchChecks)).IsTrue();
    }

    [Test]
    [Arguments("download")]
    [Arguments("access")]
    [Arguments("view-management")]
    public async Task UnadoptedAudiencePreservesOrdinaryActionsAndRejectsChangedDeniedDecisions(string action)
    {
        var (scope, publisher) = await SeedAsync();
        var resource = await ActionResourceAsync(scope, publisher, action);
        var provider = new Provider();
        await using var context = database.CreateContext();
        var workflow = Workflow(context, scope.TenantAId, publisher, provider,
            origins: ["https://resources.example.test"], allowUnscanned: true);
        var ordinary = await workflow.GetAsync(resource.Id, default);
        var value = await DiscloseAsync(ordinary.Projection);
        await Assert.That(ActionAuthorized(value, action)).IsTrue();
        var wire = JsonSerializer.SerializeToElement(value);
        await Assert.That(wire.TryGetProperty(nameof(value.DownloadAuthorized), out _)).IsFalse();
        await Assert.That(wire.TryGetProperty(nameof(value.AccessAuthorized), out _)).IsFalse();
        await Assert.That(wire.TryGetProperty(nameof(value.ManagementAuthorized), out _)).IsFalse();

        provider.Decide = input => input.Action == action
            ? EventResourceProviderDecision.Deny : EventResourceProviderDecision.Allow;
        var denied = await workflow.GetAsync(resource.Id, default);
        bool delivered = false;
        var outcome = await denied.Projection!.DiscloseAsync(async (prepared, _) =>
        {
            await Assert.That(ActionAuthorized(prepared, action)).IsFalse();
            provider.Decide = _ => EventResourceProviderDecision.Allow;
            return prepared;
        }, (_, _) => { delivered = true; return Task.CompletedTask; });
        await Assert.That(outcome).IsEqualTo(EventResourceAuthorityOutcome.NotFound);
        await Assert.That(delivered).IsFalse();
    }

    [Test]
    public async Task FullAudiencePageBatchesEveryActionAndRejectsOneChangedAccessDecision()
    {
        var (scope, publisher) = await SeedAsync();
        var rows = Enumerable.Range(0, 100).Select(index =>
            Resource(scope, publisher, index, EventResourceAudienceKindEnum.Public)).ToArray();
        await SaveAsync(rows);
        var provider = new Provider();
        await using var context = database.CreateContext();
        var workflow = Workflow(context, scope.TenantAId, publisher, provider,
            origins: ["https://resources.example.test"]);
        var page = await workflow.ListAsync(scope.EventAId, 100, null, default);
        await Assert.That(page.Failure).IsEqualTo(EventResourceAudienceFailure.None);
        bool delivered = false;
        var outcome = await page.Projection!.DiscloseAsync(async (value, _) =>
        {
            await Assert.That(value.Items.Length).IsEqualTo(100);
            await Assert.That(value.Items.All(item => item.AccessAuthorized && item.ManagementAuthorized)).IsTrue();
            provider.Decide = input => input.Resource.Id == rows[^1].Id && input.Action == "access"
                ? EventResourceProviderDecision.Deny : EventResourceProviderDecision.Allow;
            return value;
        }, (_, _) => { delivered = true; return Task.CompletedTask; });
        await Assert.That(outcome).IsEqualTo(EventResourceAuthorityOutcome.NotFound);
        await Assert.That(delivered).IsFalse();
        await Assert.That(provider.Sizes.Contains(200)).IsTrue();
        await Assert.That(provider.Sizes.Contains(301)).IsTrue();
        await Assert.That(provider.Sizes.Count).IsEqualTo(4);
    }

    private static bool ActionAuthorized(EventResourceAudienceDetailDto value, string action) => action switch
    {
        "download" => value.DownloadAuthorized,
        "access" => value.AccessAuthorized,
        "view-management" => value.ManagementAuthorized,
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    private async Task<EventResource> ActionResourceAsync(
        EventResourcePersistenceTests.ResourceScope scope, Guid publisher, string action)
    {
        var resource = Resource(scope, publisher, 0, EventResourceAudienceKindEnum.Public);
        if (action != "download")
        {
            await SaveAsync(resource);
            return resource;
        }
        await using var context = database.CreateContext();
        var fixture = await context.StorageObjects.AsNoTracking().SingleAsync(row => row.Id == scope.StorageAId);
        var storage = new StorageObject
        {
            Id = Guid.CreateVersion7(), TenantId = scope.TenantAId, Tenant = null!,
            FileTypeId = fixture.FileTypeId, FileType = null!, Provider = fixture.Provider,
            StorageProviderBindingId = fixture.StorageProviderBindingId,
            ObjectKey = $"fixtures/{Guid.CreateVersion7():N}.pdf",
            FullName = "audience-action.pdf", SafeDisplayName = "audience-action.pdf",
            Extension = ".pdf", ContentType = "application/pdf",
            Sha256Checksum = Convert.ToHexString(SHA256.HashData("audience action fixture"u8)),
            Size = 1024, Purpose = StorageObjectPurposes.EventResource,
            Visibility = StorageObjectVisibilities.PrivateOwner,
            OwningResourceKind = StorageOwningResourceKinds.EventResource, OwningResourceId = resource.Id,
            LifecycleState = StorageObjectLifecycleStates.Active, ConcurrencyStamp = Guid.CreateVersion7()
        };
        storage.RecordEventResourceInspection(storage.Id, storage.Sha256Checksum!);
        resource.SetStoredFile(storage.Id, resource.ConcurrencyStamp, publisher, Now);
        context.AddRange(storage, resource);
        await context.SaveChangesAsync();
        return resource;
    }
}
