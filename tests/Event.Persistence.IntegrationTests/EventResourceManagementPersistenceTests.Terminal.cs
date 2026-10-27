using System.Security.Cryptography;
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
    public async Task PreparedManagementMetadataCannotSurviveAChangedResourceAtNativeDelivery()
    {
        var (scope, actor) = await SeedAsync();
        await using var context = database.CreateIndependentContext();
        await using var request = Workflow(context, scope.TenantAId, actor);
        await request.InitializeAsync();
        await request.PrepareAsync();
        var workflow = request.Workflow;
        var id = Guid.CreateVersion7();
        await Assert.That((await workflow.CreateAsync(scope.EventAId, id, Draft(), default)).IsSuccess).IsTrue();
        var prepared = await workflow.GetAsync(id, default);
        bool released = false;
        var outcome = await prepared.Projection!.DiscloseAsync(async (value, _) =>
        {
            await using var writer = database.CreateIndependentContext();
            var resource = await writer.EventResources.SingleAsync(row => row.Id == id);
            resource.UpdateMetadata(new()
            {
                Title = "New management revision",
                Kind = EventResourceKindEnum.GeneralDocument,
                DisclosureMode = EventResourceDisclosureModeEnum.EligibleOnly
            }, resource.ConcurrencyStamp, actor, Now);
            await writer.SaveChangesAsync();
            return value;
        }, (_, _) => { released = true; return Task.CompletedTask; });
        await Assert.That(outcome).IsEqualTo(EventResourceAuthorityOutcome.NotFound);
        await Assert.That(released).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AuditExpiryAtPreparationOrFinalProviderCannotReleaseHistoricalEntries(bool duringProvider)
    {
        var (scope, actor) = await SeedAsync();
        var clock = new TerminalClock(new(Now));
        bool final = false;
        var provider = Substitute.For<IEventResourceAuthorizationProvider>();
        provider.CheckAsync(Arg.Any<EventResourceProviderInput>(), Arg.Any<CancellationToken>())
            .Returns(EventResourceProviderDecision.Allow);
        provider.CheckBatchAsync(Arg.Any<IReadOnlyList<EventResourceProviderInput>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var inputs = call.Arg<IReadOnlyList<EventResourceProviderInput>>();
                if (final && duringProvider && inputs.Any(input => input.Action == "view-audit"))
                    clock.Advance(TimeSpan.FromSeconds(1));
                return inputs.Select(_ => EventResourceProviderDecision.Allow).ToArray();
            });
        await using var context = database.CreateIndependentContext();
        await using var request = Workflow(context, scope.TenantAId, actor, provider: provider, clock: clock);
        await request.InitializeAsync();
        await request.PrepareAsync();
        var workflow = request.Workflow;
        var id = Guid.CreateVersion7();
        await Assert.That((await workflow.CreateAsync(scope.EventAId, id, Draft(), default)).IsSuccess).IsTrue();
        var nearExpiry = EventResourceAuditEntry.Create(scope.TenantAId, id, actor,
            EventResourceAuditAction.UpdateMetadata, EventResourceAuditOutcome.Succeeded,
            EventResourceAuditReason.OrganizerMutation, Now.AddDays(-30).AddSeconds(1));
        context.EventResourceAuditEntries.Add(nearExpiry);
        await context.SaveChangesAsync();
        var prepared = await workflow.GetAuditAsync(id, 100, default);
        bool released = false;
        var outcome = await prepared.Projection!.DiscloseAsync(async (value, _) =>
        {
            await Assert.That(value.Items.Any(row => row.Id == nearExpiry.Id)).IsTrue();
            final = true;
            if (!duringProvider) clock.Advance(TimeSpan.FromSeconds(1));
            return value;
        }, (_, _) => { released = true; return Task.CompletedTask; });
        await Assert.That(outcome).IsNotEqualTo(EventResourceAuthorityOutcome.Allowed);
        await Assert.That(released).IsFalse();
    }

    [Test]
    public async Task ExportCannotDeliverADownloadAffordanceAfterItsExactProviderDecisionChanges()
    {
        var (scope, actor) = await SeedAsync(Now);
        var id = Guid.CreateVersion7();
        bool downloadAllowed = true;
        var provider = Substitute.For<IEventResourceAuthorizationProvider>();
        provider.CheckAsync(Arg.Any<EventResourceProviderInput>(), Arg.Any<CancellationToken>())
            .Returns(EventResourceProviderDecision.Allow);
        provider.CheckBatchAsync(Arg.Any<IReadOnlyList<EventResourceProviderInput>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IReadOnlyList<EventResourceProviderInput>>()
                .Select(input => input.Action == "download" && !downloadAllowed
                    ? EventResourceProviderDecision.Deny : EventResourceProviderDecision.Allow).ToArray());
        await using var context = database.CreateContext();
        await using var request = Workflow(context, scope.TenantAId, actor, Policy(allowUnscanned: true), provider);
        await request.InitializeAsync();
        await request.PrepareAsync();
        var workflow = request.Workflow;
        await Assert.That((await workflow.CreateAsync(scope.EventAId, id, Draft() with
        {
            DisclosureMode = EventResourceDisclosureModeEnum.Public,
            PublicTitle = "Current public file"
        }, default)).IsSuccess).IsTrue();
        var resource = await context.EventResources.SingleAsync(row => row.Id == id);
        var fixture = await context.StorageObjects.AsNoTracking().SingleAsync(row => row.Id == scope.StorageAId);
        var storage = new StorageObject
        {
            Id = Guid.CreateVersion7(),
            TenantId = scope.TenantAId,
            Tenant = null!,
            FileTypeId = fixture.FileTypeId,
            FileType = null!,
            Provider = fixture.Provider,
            StorageProviderBindingId = fixture.StorageProviderBindingId,
            ObjectKey = $"fixtures/{Guid.CreateVersion7():N}.pdf",
            FullName = "qualified-export.pdf",
            SafeDisplayName = "qualified-export.pdf",
            Extension = ".pdf",
            ContentType = "application/pdf",
            Sha256Checksum = Convert.ToHexString(SHA256.HashData("qualified export fixture"u8)),
            Size = 1024,
            Purpose = StorageObjectPurposes.EventResource,
            Visibility = StorageObjectVisibilities.PrivateOwner,
            OwningResourceKind = StorageOwningResourceKinds.EventResource,
            OwningResourceId = id,
            LifecycleState = StorageObjectLifecycleStates.Active,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        storage.RecordEventResourceInspection(storage.Id, storage.Sha256Checksum!);
        context.StorageObjects.Add(storage);
        await context.SaveChangesAsync();
        resource.SetStoredFile(storage.Id, resource.ConcurrencyStamp, actor, Now);
        resource.Publish(new(scope.TenantAId, scope.EventAId, null, EventStatusEnum.Published,
            false, true, null, false, new(new(Now), new(Now.AddHours(1)), null, null)),
            true, resource.ConcurrencyStamp, actor, Now);
        await context.SaveChangesAsync();
        var prepared = await workflow.ExportAsync(scope.EventAId, 1, 20, default);
        bool released = false;
        var outcome = await prepared.Projection!.DiscloseAsync(async (value, _) =>
        {
            await Assert.That(value.Items.Single().DownloadAuthorized).IsTrue();
            downloadAllowed = false;
            return value;
        }, (_, _) => { released = true; return Task.CompletedTask; });
        await Assert.That(outcome).IsNotEqualTo(EventResourceAuthorityOutcome.Allowed);
        await Assert.That(released).IsFalse();
    }

    private sealed class TerminalClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan elapsed) => now += elapsed;
    }
}
