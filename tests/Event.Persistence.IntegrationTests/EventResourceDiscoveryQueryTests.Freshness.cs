using System.Data.Common;
using Explore.Application.Contracts.Services;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests;

public sealed partial class EventResourceDiscoveryQueryTests
{
    [Test]
    public async Task MetadataReadInfrastructureFailureIsAClosedUnavailableOutcome()
    {
        var (scope, publisher) = await SeedAsync();
        var resource = Resource(scope, publisher, 0, EventResourceAudienceKindEnum.Public);
        await SaveAsync(resource);
        await using var context = database.CreateContext(new MetadataReadFailure());
        var workflow = Workflow(context, scope.TenantAId, publisher);
        var detail = await workflow.GetAsync(resource.Id, default);
        await Assert.That(detail.Failure).IsEqualTo(EventResourceAudienceFailure.Unavailable);
        await Assert.That(detail.Value).IsNull();
        var page = await workflow.ListAsync(scope.EventAId, 20, null, default);
        await Assert.That(page.Failure).IsEqualTo(EventResourceAudienceFailure.Unavailable);
        await Assert.That(page.Value).IsNull();
    }

    private sealed class MetadataReadFailure : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("sort_order", StringComparison.Ordinal))
                throw new InvalidOperationException("Metadata storage is unavailable.");
            return ValueTask.FromResult(result);
        }
    }

    [Test]
    public Task CanonicalSqliteCursorDiscoveryUsesProviderContract() =>
        Database.EventResourceProviderContractAssertions.AssertCursorDiscoveryIsBoundedAsync(() => database.CreateContext());

    [Test]
    public async Task PrivateParentCollectionCannotStrandAResourceVisibleToItsSessionRegistrant()
    {
        var (scope, publisher) = await SeedAsync();
        var subject = await MemberAsync(scope.TenantAId);
        var resource = Resource(scope, publisher, 0, EventResourceAudienceKindEnum.Public);
        resource.ReplacePolicy(resource.Availability,
            [EventResourceAudienceRule.Create(scope.TenantAId, scope.EventAId, resource.Id,
                EventResourceAudienceKindEnum.SessionRegistrant, scope.SessionAId, requireConfirmedOrder: false)],
            resource.ConcurrencyStamp, publisher, Now);
        await using (var seed = database.CreateContext())
        {
            var parent = await seed.Events.SingleAsync(value => value.Id == scope.EventAId);
            parent.VisibilityTypeId = (int)VisibilityTypeEnum.Private;
            var session = await seed.EventSessions.SingleAsync(value => value.Id == scope.SessionAId);
            session.StartTime = new(Now); session.EndTime = new(Now.AddHours(1));
            session.Publish(EventStatusEnum.Published, Now);
            var order = RegistrationOrder.Create(scope.TenantAId, scope.EventAId, subject, scope.ActorId,
                BookingPartyTypeEnum.Individual, scope.CatalogAId,
                RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 1, 1, 1, null), null, null, "USD", Now, null);
            var participant = RegistrationParticipant.Create(scope.TenantAId, order.Id, subject, ParticipantTypeEnum.Adult, null);
            order.AddParticipant(participant);
            seed.AddRange(resource, order, new EventRegistration { Id = Guid.CreateVersion7(),
                TenantId = scope.TenantAId, Tenant = null!, EventId = parent.Id, Event = parent,
                EventSessionId = session.Id, EventSession = session, LinkedUserId = subject,
                RegistrationOrderId = order.Id, RegistrationParticipantId = participant.Id,
                RegistrationParticipant = participant, CoverageEstablishedAt = Now, ConcurrencyStamp = Guid.CreateVersion7() });
            await seed.SaveChangesAsync();
        }
        await using var context = database.CreateContext();
        var workflow = Workflow(context, scope.TenantAId, subject);
        await Assert.That((await workflow.GetAsync(resource.Id, default)).Failure).IsEqualTo(EventResourceAudienceFailure.None);
        var page = await workflow.ListAsync(scope.EventAId, 20, null, default);
        await Assert.That(page.Failure).IsEqualTo(EventResourceAudienceFailure.None);
        await Assert.That(page.Value!.Items.Single().Id).IsEqualTo(resource.Id);
        await Assert.That(await workflow.AuthorizeDisclosureAsync(page.Proof!, default)).IsEqualTo(EventResourceAudienceFailure.None);
    }

    [Test]
    public async Task MembershipRevokedDuringProviderCannotReuseThePrivateAProjectionOnRetry()
    {
        var (scope, publisher) = await SeedAsync();
        var subject = await MemberAsync(scope.TenantAId);
        var resource = Resource(scope, publisher, 0, EventResourceAudienceKindEnum.AuthenticatedTenantMember,
            EventResourceDisclosureModeEnum.Teaser);
        await SaveAsync(resource);
        var privateInputs = new List<bool>();
        await using var context = database.CreateIndependentContext();
        // Retain a stale entity deliberately; fresh authority must not consume it.
        var tracked = await context.TenantUsers.SingleAsync(value => value.UserId == subject);
        var provider = new Provider { BeforeDecision = async inputs =>
        {
            await Assert.That(context.Database.CurrentTransaction).IsNull();
            privateInputs.Add(inputs[0].Resource.DisclosePrivateMetadata);
            if (!inputs[0].Resource.DisclosePrivateMetadata) return;
            await using var writer = database.CreateIndependentContext();
            await writer.TenantUsers.Where(value => value.UserId == subject)
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.StatusId, (int)TenantUserStatusEnum.Suspended));
        } };
        var detail = await Workflow(context, scope.TenantAId, subject, provider).GetAsync(resource.Id, default);
        await Assert.That(detail.Failure).IsEqualTo(EventResourceAudienceFailure.None);
        await Assert.That(detail.Value!.IsTeaser).IsTrue();
        await Assert.That(detail.Value.Title).IsEqualTo(resource.PublicTitle);
        await Assert.That(detail.Value.Description).IsNull();
        await Assert.That(privateInputs.SequenceEqual([true, false])).IsTrue();
        await Assert.That(tracked.StatusId).IsEqualTo((int)TenantUserStatusEnum.Active);
    }

    [Test]
    public async Task FinalDisclosureBindsContinuationWitnessAndCurrentObserver()
    {
        var (scope, publisher) = await SeedAsync();
        var rows = Enumerable.Range(0, 2).Select(index => Resource(scope, publisher, index,
            EventResourceAudienceKindEnum.Public)).ToArray();
        await SaveAsync(rows);
        await using var context = database.CreateContext();
        var provider = new Provider();
        var workflow = Workflow(context, scope.TenantAId, publisher, provider);
        var page = await workflow.ListAsync(scope.EventAId, 1, null, default);
        await Assert.That(page.Value!.NextCursor).IsNotNull();
        await Assert.That(await Workflow(context, scope.TenantAId, null).AuthorizeDisclosureAsync(page.Proof!, default))
            .IsEqualTo(EventResourceAudienceFailure.NotFound);
        provider.Decide = input => input.Resource.Id == rows[1].Id
            ? EventResourceProviderDecision.Deny : EventResourceProviderDecision.Allow;
        await Assert.That(await workflow.AuthorizeDisclosureAsync(page.Proof!, default))
            .IsEqualTo(EventResourceAudienceFailure.Forbidden);
    }

    [Test]
    public async Task OversizedPersistedActiveSetCannotReturnATruncatedPage()
    {
        var (scope, publisher) = await SeedAsync();
        var rows = Enumerable.Range(0, 501).Select(_ => EventResourcePersistenceTests.CreateDraft(scope.TenantAId, scope.EventAId)).ToArray();
        await SaveAsync(rows);
        await using var context = database.CreateContext();
        var result = await Workflow(context, scope.TenantAId, publisher).ListAsync(scope.EventAId, 20, null, default);
        await Assert.That(result.Failure).IsEqualTo(EventResourceAudienceFailure.Unavailable);
        await Assert.That(result.Value).IsNull();
    }

    [Test]
    [Arguments(0)]
    [Arguments(101)]
    public async Task InvalidPageBoundsCannotDiscloseMetadata(int pageSize)
    {
        var (scope, publisher) = await SeedAsync();
        await using var context = database.CreateContext();
        var result = await Workflow(context, scope.TenantAId, publisher).ListAsync(scope.EventAId, pageSize, null, default);
        await Assert.That(result.Failure).IsEqualTo(EventResourceAudienceFailure.InvalidRequest);
        await Assert.That(result.Value).IsNull();
    }
}
