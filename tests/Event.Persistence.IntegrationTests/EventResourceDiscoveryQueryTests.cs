using System.Text.Json;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.EventResources.Handlers.Queries;
using Explore.Application.Features.EventResources.Requests.Queries;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests;

[NotInParallel("EventResourcePersistence")]
[ClassDataSource<EventResourcePersistenceTests.TestDatabase>(Shared = SharedType.PerClass)]
public sealed partial class EventResourceDiscoveryQueryTests(EventResourcePersistenceTests.TestDatabase database)
{
    private static readonly DateTime Now = new(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private readonly EventResourceCursorProtector _cursors = new(new EphemeralDataProtectionProvider(), new Clock());

    [Test]
    public async Task SparseFiveHundredEvaluatesDeniedEarlyRowsAndFindsLateVisibleResourceWithBoundedSql()
    {
        var (scope, user) = await SeedAsync();
        var rows = Enumerable.Range(0, 500).Select(index => Resource(scope, user, index,
            EventResourceAudienceKindEnum.Public)).ToArray();
        await SaveAsync(rows);
        var recorder = new SqlRecorder();
        var provider = new Provider
        {
            Decide = input => input.Resource.Id == scope.EventAId || input.Resource.Id == rows[^1].Id
                ? EventResourceProviderDecision.Allow : EventResourceProviderDecision.Deny
        };
        await using var context = database.CreateContext(recorder);
        var workflow = Workflow(context, scope.TenantAId, user, provider);
        var result = await new ListEventResourcesQueryHandler(workflow).QueryAsync(new(scope.EventAId));
        await Assert.That(result.Failure).IsEqualTo(EventResourceAudienceFailure.None);
        await Assert.That(result.Value!.Items.Select(item => item.Id).SequenceEqual([rows[^1].Id])).IsTrue();
        await Assert.That(result.Value.NextCursor).IsNull();
        await Assert.That(provider.Sizes.Contains(500)).IsTrue();
        await Assert.That(provider.Sizes.Count).IsLessThanOrEqualTo(4);
        await Assert.That(recorder.Commands.Count).IsLessThanOrEqualTo(150);
        await Assert.That(context.ChangeTracker.Entries<EventResource>().Any()).IsFalse();

        var candidate = recorder.Commands.First(command => command.Text.Contains("sort_order", StringComparison.Ordinal)
            && command.Text.Contains("LIMIT", StringComparison.Ordinal)
            && command.Text.Contains("publication_state_id", StringComparison.Ordinal));
        var plans = await ExplainAsync(context, candidate);
        await Assert.That(plans.Any(plan => plan.Contains("tenant_id_event_id_is_deleted_sort_order", StringComparison.Ordinal))).IsTrue();
        await Assert.That(plans.Any(plan => plan.Contains("SCAN ie_event_resources", StringComparison.Ordinal))).IsFalse();
        Console.WriteLine($"Audience sparse500: {recorder.Commands.Count} SELECTs; provider batches {string.Join(',', provider.Sizes)}; SQLite plan: {string.Join("; ", plans)}");
    }

    [Test]
    public async Task EqualSortPagesUseLastReturnedVisiblePositionAndReauthorizeEveryContinuation()
    {
        var (scope, user) = await SeedAsync();
        var rows = Enumerable.Range(0, 5).Select(_ => Resource(scope, user, 7, EventResourceAudienceKindEnum.Public))
            .OrderBy(row => row.Id).ToArray();
        await SaveAsync(rows);
        var provider = new Provider { Decide = input => input.Resource.Id == rows[1].Id || input.Resource.Id == rows[3].Id
            ? EventResourceProviderDecision.Deny : EventResourceProviderDecision.Allow };
        await using var context = database.CreateContext();
        var workflow = Workflow(context, scope.TenantAId, user, provider);
        var first = await workflow.ListAsync(scope.EventAId, 1, null, default);
        await Assert.That(first.Value!.Items.Single().Id).IsEqualTo(rows[0].Id);
        await Assert.That(_cursors.TryUnprotect(first.Value.NextCursor!, new(scope.TenantAId, scope.EventAId, user, false), out var decoded)).IsTrue();
        await Assert.That(decoded!.ResourceId).IsEqualTo(rows[0].Id);
        var second = await workflow.ListAsync(scope.EventAId, 1, first.Value.NextCursor, default);
        await Assert.That(second.Value!.Items.Single().Id).IsEqualTo(rows[2].Id);
        var last = await workflow.ListAsync(scope.EventAId, 1, second.Value.NextCursor, default);
        await Assert.That(last.Value!.Items.Single().Id).IsEqualTo(rows[4].Id);
        await Assert.That(last.Value.NextCursor).IsNull();
        provider.Decide = input => input.Resource.Id == scope.EventAId
            ? EventResourceProviderDecision.Allow : EventResourceProviderDecision.Deny;
        var revoked = await workflow.ListAsync(scope.EventAId, 1, first.Value.NextCursor, default);
        await Assert.That(revoked.Value!.Items.Length).IsEqualTo(0);
        await Assert.That(revoked.Value.NextCursor).IsNull();
    }

    [Test]
    public async Task PrivateProjectionCannotSurviveMembershipDowngradeWithoutResourceStampChange()
    {
        var (scope, publisher) = await SeedAsync();
        var subject = await MemberAsync(scope.TenantAId);
        var resource = Resource(scope, publisher, 0, EventResourceAudienceKindEnum.AuthenticatedTenantMember,
            EventResourceDisclosureModeEnum.Teaser);
        await SaveAsync(resource);
        await using var context = database.CreateContext();
        var workflow = Workflow(context, scope.TenantAId, subject,
            origins: ["https://resources.example.test"]);
        var detail = await new GetEventResourceAudienceDetailQueryHandler(workflow).QueryAsync(new(resource.Id));
        await Assert.That(detail.Failure).IsEqualTo(EventResourceAudienceFailure.None);
        await Assert.That(detail.Value!.Title).IsEqualTo(resource.Title);
        await Assert.That(detail.Value.IsTeaser).IsFalse();
        await Assert.That(detail.Value.ExternalDestinationSafeOrigin).IsEqualTo("https://resources.example.test");
        var before = resource.ConcurrencyStamp;
        await using (var writer = database.CreateIndependentContext())
        {
            await writer.TenantUsers.Where(member => member.UserId == subject)
                .ExecuteUpdateAsync(setters => setters.SetProperty(member => member.StatusId, (int)TenantUserStatusEnum.Suspended));
        }
        var final = await new AuthorizeEventResourceAudienceDisclosureQueryHandler(workflow).QueryAsync(new(detail.Proof!));
        await Assert.That(final).IsEqualTo(EventResourceAudienceFailure.Forbidden);
        await Assert.That((await context.EventResources.AsNoTracking().SingleAsync(row => row.Id == resource.Id)).ConcurrencyStamp).IsEqualTo(before);
        var teaser = await workflow.GetAsync(resource.Id, default);
        await Assert.That(teaser.Failure).IsEqualTo(EventResourceAudienceFailure.None);
        await Assert.That(teaser.Value!.Title).IsEqualTo(resource.PublicTitle);
        await Assert.That(teaser.Value.IsTeaser).IsTrue();
        await Assert.That(teaser.Value.ExternalDestinationSafeOrigin).IsNull();
        await Assert.That(teaser.Value.Description).IsNull();
        await Assert.That(teaser.Value.AccessibilityNote).IsNull();
        await Assert.That(teaser.Value.LanguageCode).IsNull();
        await Assert.That(teaser.Value.AccessibleAlternativeEventResourceId).IsNull();
        var names = JsonSerializer.SerializeToElement(teaser.Value).EnumerateObject().Select(property => property.Name).ToHashSet();
        await Assert.That(names.Overlaps(["SensitiveNotes", "AudienceRules", "StorageObjectId", "ExternalDestinationCiphertext",
            "ExternalDestinationSafeOrigin", "Version", "TotalCount", "TotalPages"])).IsFalse();
    }

    [Test]
    public async Task AlternativesAreIndependentlyAuthorizedAndRecheckedAfterProjection()
    {
        var (scope, user) = await SeedAsync();
        var hidden = Resource(scope, user, 0, EventResourceAudienceKindEnum.AuthenticatedTenantMember);
        var visible = Resource(scope, user, 1, EventResourceAudienceKindEnum.Public, alternative: hidden.Id);
        await SaveAsync(hidden, visible);
        await using var context = database.CreateContext();
        var anonymous = Workflow(context, scope.TenantAId, null);
        var detail = await anonymous.GetAsync(visible.Id, default);
        await Assert.That(detail.Value!.AccessibleAlternativeEventResourceId).IsNull();
        var member = Workflow(context, scope.TenantAId, user);
        var eligible = await member.GetAsync(visible.Id, default);
        await Assert.That(eligible.Value!.AccessibleAlternativeEventResourceId).IsEqualTo(hidden.Id);
        await using (var writer = database.CreateIndependentContext())
            await writer.TenantUsers.Where(value => value.UserId == user)
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.StatusId, (int)TenantUserStatusEnum.Suspended));
        await Assert.That(await member.AuthorizeDisclosureAsync(eligible.Proof!, default)).IsEqualTo(EventResourceAudienceFailure.NotFound);
    }

    [Test]
    public async Task HiddenMissingCrossTenantDraftAndUnsupportedMachineSubjectAreNonEnumerating()
    {
        var (scope, user) = await SeedAsync();
        var hidden = Resource(scope, user, 0, EventResourceAudienceKindEnum.AuthenticatedTenantMember);
        var draft = EventResourcePersistenceTests.CreateDraft(scope.TenantAId, scope.EventAId);
        await SaveAsync(hidden, draft);
        await using var context = database.CreateContext();
        foreach (var id in new[] { hidden.Id, draft.Id, scope.AlternativeBId, Guid.CreateVersion7() })
        {
            var result = await Workflow(context, scope.TenantAId, null).GetAsync(id, default);
            await Assert.That(result.Failure).IsEqualTo(EventResourceAudienceFailure.NotFound);
            await Assert.That(result.Value).IsNull();
            await Assert.That(result.Proof).IsNull();
        }
        var machine = await Workflow(context, scope.TenantAId, user, isMachine: true).GetAsync(hidden.Id, default);
        await Assert.That(machine.Failure).IsEqualTo(EventResourceAudienceFailure.NotFound);
        var wrongEvent = await Workflow(context, scope.TenantAId, user).ListAsync(scope.EventBId, 20, null, default);
        await Assert.That(wrongEvent.Failure).IsEqualTo(EventResourceAudienceFailure.NotFound);
    }

    [Test]
    [Arguments("partial")]
    [Arguments("malformed")]
    [Arguments("timeout")]
    public async Task ProviderFailureCannotReturnPartialPublicMetadata(string failure)
    {
        var (scope, user) = await SeedAsync();
        var resource = Resource(scope, user, 0, EventResourceAudienceKindEnum.Public);
        await SaveAsync(resource);
        var provider = new Provider { Failure = failure };
        await using var context = database.CreateContext();
        var workflow = Workflow(context, scope.TenantAId, user, provider);
        var detail = await workflow.GetAsync(resource.Id, default);
        await Assert.That(detail.Failure).IsEqualTo(EventResourceAudienceFailure.Unavailable);
        await Assert.That(detail.Value).IsNull();
        var list = await workflow.ListAsync(scope.EventAId, 20, null, default);
        await Assert.That(list.Failure).IsEqualTo(EventResourceAudienceFailure.Unavailable);
        await Assert.That(list.Value).IsNull();
    }

    [Test]
    public async Task AnonymousAndMachinePublicTeasersNeverInvokePrincipalProviderAndInvalidCursorsAreClosed()
    {
        var (scope, user) = await SeedAsync();
        var resource = Resource(scope, user, 0, EventResourceAudienceKindEnum.AuthenticatedTenantMember,
            EventResourceDisclosureModeEnum.Teaser);
        await SaveAsync(resource);
        var provider = new Provider { Failure = "timeout" };
        await using var context = database.CreateContext();
        foreach (bool machine in new[] { false, true })
        {
            var workflow = Workflow(context, scope.TenantAId, machine ? user : null, provider, machine);
            var detail = await workflow.GetAsync(resource.Id, default);
            await Assert.That(detail.Value!.IsTeaser).IsTrue();
            var list = await workflow.ListAsync(scope.EventAId, 20, null, default);
            await Assert.That(list.Value!.Items.Single().Id).IsEqualTo(resource.Id);
            foreach (var invalid in new[] { "", "malformed", new string('A', 2049),
                _cursors.Protect(new(scope.TenantBId, scope.EventAId, null, false), new(0, resource.Id)) })
            {
                var result = await workflow.ListAsync(scope.EventAId, 20, invalid, default);
                await Assert.That(result.Failure).IsEqualTo(EventResourceAudienceFailure.InvalidRequest);
                await Assert.That(result.Value).IsNull();
            }
        }
        await Assert.That(provider.Sizes.Count).IsEqualTo(0);
    }

    [Test]
    public async Task QueryGrowthIsByFactCategoryNotResourceCardinality()
    {
        var (scope, user) = await SeedAsync();
        await SaveAsync(Enumerable.Range(0, 5).Select(index => Resource(scope, user, index,
            EventResourceAudienceKindEnum.AuthenticatedTenantMember)).ToArray());
        var small = new SqlRecorder();
        await using (var context = database.CreateContext(small))
            await Assert.That((await Workflow(context, scope.TenantAId, user).ListAsync(scope.EventAId, 20, null, default)).Value!.Items.Length).IsEqualTo(5);
        await SaveAsync(Enumerable.Range(5, 495).Select(index => Resource(scope, user, index,
            EventResourceAudienceKindEnum.AuthenticatedTenantMember)).ToArray());
        var large = new SqlRecorder();
        var provider = new Provider();
        await using (var context = database.CreateContext(large))
            await Assert.That((await Workflow(context, scope.TenantAId, user, provider).ListAsync(scope.EventAId, 20, null, default)).Value!.Items.Length).IsEqualTo(20);
        await Assert.That(large.Commands.Count).IsLessThanOrEqualTo(small.Commands.Count + 2);
        await Assert.That(provider.Sizes.Contains(500)).IsTrue();
        Console.WriteLine($"Audience SQL growth: five={small.Commands.Count}, five hundred={large.Commands.Count}");
    }

}
