using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventResource;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Infrastructure.Tests.Fixtures;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Event.Persistence.IntegrationTests;

public sealed partial class EventResourceDiscoveryQueryTests
{
    private ManagementRequest ManagementWorkflow(
        ExploreDbContext context, Guid tenantId, Guid actor)
    {
        var repository = new EventResourceRepository(context);
        var unit = new EfCoreUnitOfWork(context);
        var clock = new Clock();
        var governance = Substitute.For<IEventResourceGovernancePolicyReader>();
        governance.ReadAsync(tenantId, Arg.Any<CancellationToken>()).Returns(EventResourceGovernancePolicy.Create(
            Enum.GetValues<EventResourceDeliveryTypeEnum>(), Enum.GetValues<EventResourceAudienceKindEnum>(),
            [EventResourceGovernancePolicy.PdfMediaType], 10_485_760, true,
            ["https://resources.example.test"], 30, 500, long.MaxValue));
        var routes = Substitute.For<IEventResourceProviderSnapshotReader>();
        routes.ReadAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(new EventResourceProviderSnapshot(EventResourceProviderMode.Local, "", "default"));
        var provider = Substitute.For<IEventResourceAuthorizationProvider>();
        provider.CheckAsync(Arg.Any<EventResourceProviderInput>(), Arg.Any<CancellationToken>())
            .Returns(EventResourceProviderDecision.Allow);
        provider.CheckBatchAsync(Arg.Any<IReadOnlyList<EventResourceProviderInput>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IReadOnlyList<EventResourceProviderInput>>()!
                .Select(_ => EventResourceProviderDecision.Allow).ToArray());
        var tenant = context.TenantContext;
        if (tenant is null)
        {
            tenant = Substitute.For<ITenantContext>();
            tenant.TenantId.Returns(tenantId);
            context.TenantContext = tenant;
        }
        else if (tenant.TenantId != tenantId)
            throw new InvalidOperationException("The management request must retain its original tenant.");
        var human = new CurrentHumanPrincipalFixture(actor, tenantId, borrowedContext: context,
            configureServices: collection => collection.AddSingleton<IPrivacyIdentityFenceAuthority>(
                database.Privacy.Authority(context, clock)), timeProvider: clock);
        var services = human.Services;
        var workflow = new EventResourceManagementWorkflow(repository, unit, new EventResourceStorageLifecycleService(
            new EventResourceStorageLifecycleRepository(context), unit, clock), new(unit,
            new EventResourceAuthoritySnapshotReader(repository, new EventAuthoritySnapshotService(context), governance,
                new ExternalApiKeyAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context)),
                Substitute.For<IConfiguredApiKeyResourceAuthorityReader>(),
                new EventResourceModerationSnapshotReader(context,
                    Substitute.For<IModerationMachineSubjectBindingRepository>(),
                    global::Event.Persistence.IntegrationTests.Moderation.ModerationRecognitionResourceFixture.Reader(context))),
            routes, provider, clock), tenant, services.GetRequiredService<ICurrentUserService>(), human.Machine, clock,
            new EventPublicationMutationBoundary(context, services.GetRequiredService<IPrivacyIdentityFenceAuthority>(),
                services.GetRequiredService<IUserContext>(), human.Admin, human.Machine, clock,
                global::Event.Persistence.IntegrationTests.Moderation.ModerationRecognitionResourceFixture.Reader(context),
                services.GetRequiredService<IHumanActionCommitFinalizer>()));
        return new(human, workflow);
    }

    private sealed class ManagementRequest(CurrentHumanPrincipalFixture human, EventResourceManagementWorkflow workflow)
        : IAsyncDisposable
    {
        public EventResourceManagementWorkflow Workflow => workflow;
        public Task InitializeAsync() => human.InitializeAsync();
        public Task PrepareAsync() => human.PrepareAsync();
        public ValueTask DisposeAsync() => human.DisposeAsync();
    }

    private async Task<EventResourceAuditEntry> ManagementAuditAsync(EventResource resource, Guid actor)
    {
        await using var context = database.CreateContext();
        var audit = EventResourceAuditEntry.Create(resource.TenantId, resource.Id, actor,
            EventResourceAuditAction.UpdateMetadata, EventResourceAuditOutcome.Succeeded,
            EventResourceAuditReason.OrganizerMutation, Now);
        context.EventResourceAuditEntries.Add(audit);
        await context.SaveChangesAsync();
        return audit;
    }

    private async Task ApplyManagementQuarantineAsync(EventResource resource)
    {
        await using var writer = database.CreateIndependentContext();
        var current = await writer.EventResources.Include(row => row.ModerationQuarantines)
            .SingleAsync(row => row.Id == resource.Id);
        await Assert.That(current.MaterialVersionId).IsEqualTo(resource.MaterialVersionId);
        // Accepted aggregate quarantine is fixture state, not effect-writer activation evidence.
        current.ApplyModerationQuarantine(Guid.CreateVersion7(), Guid.CreateVersion7(), 0,
            current.MaterialVersionId!.Value, current.ConcurrencyStamp, new DateTimeOffset(Now));
        await writer.SaveChangesAsync();
    }

    private async Task RevokeManagementOrganizerAsync(Guid eventId)
    {
        await using var writer = database.CreateIndependentContext();
        var parent = await writer.Events.SingleAsync(row => row.Id == eventId);
        parent.OrganizerActorId = null;
        await writer.SaveChangesAsync();
    }

    private static async Task AssertSafeManagementItemAsync(EventResourceManagementDto value, EventResource resource)
    {
        await Assert.That(value.Id).IsEqualTo(resource.Id);
        await Assert.That(value.EventId).IsEqualTo(resource.EventId);
        await Assert.That(value.MaterialVersionId).IsEqualTo(resource.MaterialVersionId);
        await Assert.That(value.Status).IsEqualTo("quarantined");
        await Assert.That(value.Draft).IsNull();
        await Assert.That(value.File).IsNull();
        await Assert.That(value.ExternalDestinationSafeOrigin).IsNull();
        await Assert.That(value.CreatedAt).IsNull();
        await Assert.That(value.UpdatedAt).IsNull();
    }

    private static async Task AssertSafeManagementExportAsync(EventResourceMetadataExportDto value, EventResource resource)
    {
        await Assert.That(value.Id).IsEqualTo(resource.Id);
        await Assert.That(value.MaterialVersionId).IsEqualTo(resource.MaterialVersionId);
        await Assert.That(value.Status).IsEqualTo("quarantined");
        await Assert.That(value.EventSessionId).IsNull();
        await Assert.That(value.Title).IsNull();
        await Assert.That(value.PublicTitle).IsNull();
        await Assert.That(value.Description).IsNull();
        await Assert.That(value.SensitiveNotes).IsNull();
        await Assert.That(value.Kind).IsNull();
        await Assert.That(value.DisclosureMode).IsNull();
        await Assert.That(value.DeliveryType).IsNull();
        await Assert.That(value.LanguageCode).IsNull();
        await Assert.That(value.AccessibilityNote).IsNull();
        await Assert.That(value.SortOrder).IsNull();
        await Assert.That(value.AccessibleAlternativeEventResourceId).IsNull();
        await Assert.That(value.Availability).IsNull();
        await Assert.That(value.AudienceRules).IsNull();
        await Assert.That(value.File).IsNull();
        await Assert.That(value.Download).IsNull();
        await Assert.That(value.DownloadAuthorized).IsFalse();
    }
}
