using System.Text.Json;
using Explore.Application.DTOs.EventResource;
using Explore.Application.Services;
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
    public async Task NativeAudienceQuarantineDisclosesOnlyExactMaterialStatus(string action, bool collection)
    {
        var (scope, publisher) = await SeedAsync();
        var resource = await ActionResourceAsync(scope, publisher, action);
        await using (var writer = database.CreateContext())
        {
            var current = await writer.EventResources.Include(row => row.ModerationQuarantines)
                .SingleAsync(row => row.Id == resource.Id);
            current.ApplyModerationQuarantine(Guid.CreateVersion7(), Guid.CreateVersion7(), 0,
                current.MaterialVersionId!.Value, current.ConcurrencyStamp, new DateTimeOffset(Now));
            await writer.SaveChangesAsync();
        }
        await using var context = database.CreateMeasurementContext();
        var workflow = Workflow(context, scope.TenantAId, publisher,
            origins: ["https://resources.example.test"], allowUnscanned: true);
        EventResourceAudienceDetailDto? disclosed = null;
        var outcome = collection
            ? await (await workflow.ListAsync(scope.EventAId, 100, null, default)).Projection!
                .DiscloseAsync((page, _) => Task.FromResult(page.Items.Single()),
                    (value, _) => { disclosed = value; return Task.CompletedTask; })
            : await (await workflow.GetAsync(resource.Id, default)).Projection!
                .DiscloseAsync((value, _) => Task.FromResult(value),
                    (value, _) => { disclosed = value; return Task.CompletedTask; });

        await Assert.That(outcome).IsEqualTo(EventResourceAuthorityOutcome.Allowed);
        await Assert.That(disclosed).IsNotNull();
        await Assert.That(disclosed!.Id).IsEqualTo(resource.Id);
        await Assert.That(disclosed.MaterialVersionId).IsEqualTo(resource.MaterialVersionId);
        await Assert.That(disclosed.Availability).IsEqualTo("quarantined");
        await Assert.That(disclosed.Title).IsNull();
        await Assert.That(disclosed.Description).IsNull();
        await Assert.That(disclosed.File).IsNull();
        await Assert.That(disclosed.ExternalDestinationSafeOrigin).IsNull();
        await Assert.That(disclosed.AccessibleAlternativeEventResourceId).IsNull();
        await Assert.That(disclosed.DownloadAuthorized).IsFalse();
        await Assert.That(disclosed.AccessAuthorized).IsFalse();
        await Assert.That(disclosed.ManagementAuthorized).IsFalse();
        await RecordAudienceDatabaseObservationAsync(context);
    }

    [Test]
    [Arguments("download", false)]
    [Arguments("download", true)]
    [Arguments("access", false)]
    [Arguments("access", true)]
    [Arguments("view-management", false)]
    [Arguments("view-management", true)]
    public async Task QuarantineAfterNativeAudiencePreparationPreventsTerminalDisclosure(string action, bool collection)
    {
        var (scope, publisher) = await SeedAsync();
        var resource = await ActionResourceAsync(scope, publisher, action);
        await using var context = database.CreateMeasurementContext();
        var workflow = Workflow(context, scope.TenantAId, publisher,
            origins: ["https://resources.example.test"], allowUnscanned: true);
        var detail = collection ? null : await workflow.GetAsync(resource.Id, default);
        var page = collection ? await workflow.ListAsync(scope.EventAId, 100, null, default) : null;
        await Assert.That(collection ? page!.Projection is not null : detail!.Projection is not null).IsTrue();
        await using (var writer = database.CreateContext())
        {
            var current = await writer.EventResources.Include(row => row.ModerationQuarantines)
                .SingleAsync(row => row.Id == resource.Id);
            current.ApplyModerationQuarantine(Guid.CreateVersion7(), Guid.CreateVersion7(), 0,
                current.MaterialVersionId!.Value, current.ConcurrencyStamp, new DateTimeOffset(Now));
            await writer.SaveChangesAsync();
        }
        bool delivered = false;
        var outcome = collection
            ? await page!.Projection!.DiscloseAsync((value, _) => Task.FromResult(value),
                (_, _) => { delivered = true; return Task.CompletedTask; })
            : await detail!.Projection!.DiscloseAsync((value, _) => Task.FromResult(value),
                (_, _) => { delivered = true; return Task.CompletedTask; });

        await Assert.That(outcome).IsEqualTo(EventResourceAuthorityOutcome.NotFound);
        await Assert.That(delivered).IsFalse();
        var fresh = await workflow.GetAsync(resource.Id, default);
        var safe = await DiscloseAsync(fresh.Projection);
        await Assert.That(safe.Availability).IsEqualTo("quarantined");
        await Assert.That(safe.Title).IsNull();
        await Assert.That(safe.File).IsNull();
        await Assert.That(safe.DownloadAuthorized).IsFalse();
        await RecordAudienceDatabaseObservationAsync(context);
    }

    [Test]
    [Arguments("download", false)]
    [Arguments("download", true)]
    [Arguments("access", false)]
    [Arguments("access", true)]
    [Arguments("view-management", false)]
    [Arguments("view-management", true)]
    public async Task QuarantineDuringRepresentationPreparationWithholdsPrivatelySerializedActions(
        string action, bool collection)
    {
        var (scope, publisher) = await SeedAsync();
        var resource = await ActionResourceAsync(scope, publisher, action);
        await using var context = database.CreateMeasurementContext();
        var workflow = Workflow(context, scope.TenantAId, publisher,
            origins: ["https://resources.example.test"], allowUnscanned: true);
        var detail = collection ? null : await workflow.GetAsync(resource.Id, default);
        var page = collection ? await workflow.ListAsync(scope.EventAId, 100, null, default) : null;
        await Assert.That(collection ? page!.Projection is not null : detail!.Projection is not null).IsTrue();
        bool prepared = false;
        bool delivered = false;
        async Task<JsonElement> Prepare(EventResourceAudienceDetailDto value)
        {
            await Assert.That(ActionAuthorized(value, action)).IsTrue();
            await Assert.That(value.Title).IsEqualTo(resource.Title);
            var representation = JsonSerializer.SerializeToElement(value);
            await Assert.That(representation.GetProperty(nameof(value.Title)).GetString()).IsEqualTo(resource.Title);
            await Assert.That(context.Database.CurrentTransaction).IsNull();
            await using var writer = database.CreateIndependentContext();
            var current = await writer.EventResources.Include(row => row.ModerationQuarantines)
                .SingleAsync(row => row.Id == resource.Id);
            current.ApplyModerationQuarantine(Guid.CreateVersion7(), Guid.CreateVersion7(), 0,
                current.MaterialVersionId!.Value, current.ConcurrencyStamp, new DateTimeOffset(Now));
            await writer.SaveChangesAsync();
            prepared = true;
            return representation;
        }
        var outcome = collection
            ? await page!.Projection!.DiscloseAsync((value, _) => Prepare(value.Items.Single()),
                (_, _) => { delivered = true; return Task.CompletedTask; })
            : await detail!.Projection!.DiscloseAsync((value, _) => Prepare(value),
                (_, _) => { delivered = true; return Task.CompletedTask; });

        await Assert.That(prepared).IsTrue();
        await Assert.That(outcome).IsEqualTo(EventResourceAuthorityOutcome.NotFound);
        await Assert.That(delivered).IsFalse();
        var safe = await DiscloseAsync((await workflow.GetAsync(resource.Id, default)).Projection);
        await Assert.That(safe.Id).IsEqualTo(resource.Id);
        await Assert.That(safe.MaterialVersionId).IsEqualTo(resource.MaterialVersionId);
        await Assert.That(safe.Availability).IsEqualTo("quarantined");
        await Assert.That(safe.Title).IsNull();
        await Assert.That(safe.Description).IsNull();
        await Assert.That(safe.File).IsNull();
        await Assert.That(safe.ExternalDestinationSafeOrigin).IsNull();
        await Assert.That(safe.AccessibleAlternativeEventResourceId).IsNull();
        await Assert.That(safe.DownloadAuthorized).IsFalse();
        await Assert.That(safe.AccessAuthorized).IsFalse();
        await Assert.That(safe.ManagementAuthorized).IsFalse();
        await RecordAudienceDatabaseObservationAsync(context);
    }
}
