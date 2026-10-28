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
using Microsoft.EntityFrameworkCore.Infrastructure;
using Explore.Persistence.Database;

namespace Event.Persistence.IntegrationTests;

[NotInParallel("EventResourcePersistence")]
[ClassDataSource<EventResourcePersistenceTests.TestDatabase>(Shared = SharedType.PerClass)]
public sealed partial class EventResourceDiscoveryQueryTests(EventResourcePersistenceTests.TestDatabase database)
{
    private static readonly DateTime Now = new(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private readonly EventResourceCursorProtector _cursors = new(new EphemeralDataProtectionProvider(), new Clock());

    private static async Task RecordAudienceDatabaseObservationAsync(Explore.Persistence.ExploreDbContext context)
    {
        var identity = context.GetService<IDbContextOptions>()
            .FindExtension<PrimaryDatabasePublicIdentityOptionsExtension>()
            ?? throw new InvalidOperationException("Audience measurement requires native public database composition.");
        var connection = context.Database.GetDbConnection();
        var provider = identity.Provider switch
        {
            Explore.Persistence.Database.PrimaryDatabaseProvider.PostgreSql => "Npgsql.EntityFrameworkCore.PostgreSQL",
            Explore.Persistence.Database.PrimaryDatabaseProvider.Sqlite => "Microsoft.EntityFrameworkCore.Sqlite",
            Explore.Persistence.Database.PrimaryDatabaseProvider.SqlServer => "Microsoft.EntityFrameworkCore.SqlServer",
            Explore.Persistence.Database.PrimaryDatabaseProvider.MariaDb or Explore.Persistence.Database.PrimaryDatabaseProvider.MySql
                => "Pomelo.EntityFrameworkCore.MySql",
            _ => null
        };
        if (provider is null || context.Database.ProviderName != provider)
            throw new InvalidOperationException("Audience measurement must observe its actual composed provider.");
        var alreadyOpen = connection.State == System.Data.ConnectionState.Open;
        if (!alreadyOpen) await context.Database.OpenConnectionAsync();
        try
        {
            Event.Tests.Shared.Moderation.ModerationMeasurementJournalAttribute.RecordDatabaseObservation(
                TUnit.Core.TestContext.Current!, identity.Provider.ToString(), connection.ServerVersion, identity.PublicDigest);
        }
        finally
        {
            if (!alreadyOpen) await context.Database.CloseConnectionAsync();
        }
    }

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
        var page = await DiscloseAsync(result.Projection);
        await Assert.That(page.Items.Select(item => item.Id).SequenceEqual([rows[^1].Id])).IsTrue();
        await Assert.That(page.NextCursor).IsNull();
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
        var provider = new Provider
        {
            Decide = input => input.Resource.Id == rows[1].Id || input.Resource.Id == rows[3].Id
            ? EventResourceProviderDecision.Deny : EventResourceProviderDecision.Allow
        };
        await using var context = database.CreateContext();
        var workflow = Workflow(context, scope.TenantAId, user, provider);
        var first = await workflow.ListAsync(scope.EventAId, 1, null, default);
        var firstPage = await DiscloseAsync(first.Projection);
        await Assert.That(firstPage.Items.Single().Id).IsEqualTo(rows[0].Id);
        await Assert.That(_cursors.TryUnprotect(firstPage.NextCursor!, new(scope.TenantAId, scope.EventAId, user, false), out var decoded)).IsTrue();
        await Assert.That(decoded!.ResourceId).IsEqualTo(rows[0].Id);
        var second = await workflow.ListAsync(scope.EventAId, 1, firstPage.NextCursor, default);
        var secondPage = await DiscloseAsync(second.Projection);
        await Assert.That(secondPage.Items.Single().Id).IsEqualTo(rows[2].Id);
        var last = await workflow.ListAsync(scope.EventAId, 1, secondPage.NextCursor, default);
        var lastPage = await DiscloseAsync(last.Projection);
        await Assert.That(lastPage.Items.Single().Id).IsEqualTo(rows[4].Id);
        await Assert.That(lastPage.NextCursor).IsNull();
        provider.Decide = input => input.Resource.Id == scope.EventAId
            ? EventResourceProviderDecision.Allow : EventResourceProviderDecision.Deny;
        var revoked = await workflow.ListAsync(scope.EventAId, 1, firstPage.NextCursor, default);
        var revokedPage = await DiscloseAsync(revoked.Projection);
        await Assert.That(revokedPage.Items.Length).IsEqualTo(0);
        await Assert.That(revokedPage.NextCursor).IsNull();
    }

    [Test]
    public async Task PrivateProjectionCannotSurviveMembershipDowngradeWithoutResourceStampChange()
    {
        var (scope, publisher) = await SeedAsync();
        var subject = await MemberAsync(scope.TenantAId);
        var resource = Resource(scope, publisher, 0, EventResourceAudienceKindEnum.AuthenticatedTenantMember,
            EventResourceDisclosureModeEnum.Teaser);
        await SaveAsync(resource);
        await using var context = database.CreateMeasurementContext();
        var workflow = Workflow(context, scope.TenantAId, subject,
            origins: ["https://resources.example.test"]);
        var detail = await new GetEventResourceAudienceDetailQueryHandler(workflow).QueryAsync(new(resource.Id));
        await Assert.That(detail.Failure).IsEqualTo(EventResourceAudienceFailure.None);
        await Assert.That(JsonSerializer.Serialize(detail).Contains(resource.Title, StringComparison.Ordinal)).IsFalse();
        var before = resource.ConcurrencyStamp;
        bool released = false;
        var final = await detail.Projection!.DiscloseAsync(async (value, _) =>
        {
            await Assert.That(value.Title).IsEqualTo(resource.Title);
            await Assert.That(value.IsTeaser).IsFalse();
            await Assert.That(value.ExternalDestinationSafeOrigin).IsEqualTo("https://resources.example.test");
            await using var writer = database.CreateIndependentContext();
            await writer.TenantUsers.Where(member => member.UserId == subject)
                .ExecuteUpdateAsync(setters => setters.SetProperty(member => member.StatusId, (int)TenantUserStatusEnum.Suspended));
            return value;
        }, (_, _) => { released = true; return Task.CompletedTask; });
        await Assert.That(final).IsEqualTo(EventResourceAuthorityOutcome.Forbidden);
        await Assert.That(released).IsFalse();
        await Assert.That((await context.EventResources.AsNoTracking().SingleAsync(row => row.Id == resource.Id)).ConcurrencyStamp).IsEqualTo(before);
        var teaser = await workflow.GetAsync(resource.Id, default);
        await Assert.That(teaser.Failure).IsEqualTo(EventResourceAudienceFailure.None);
        var teaserValue = await DiscloseAsync(teaser.Projection);
        await Assert.That(teaserValue.Title).IsEqualTo(resource.PublicTitle);
        await Assert.That(teaserValue.IsTeaser).IsTrue();
        await Assert.That(teaserValue.ExternalDestinationSafeOrigin).IsNull();
        await RecordAudienceDatabaseObservationAsync(context);
        await Assert.That(teaserValue.Description).IsNull();
        await Assert.That(teaserValue.AccessibilityNote).IsNull();
        await Assert.That(teaserValue.LanguageCode).IsNull();
        await Assert.That(teaserValue.AccessibleAlternativeEventResourceId).IsNull();
        var names = JsonSerializer.SerializeToElement(teaserValue).EnumerateObject().Select(property => property.Name).ToHashSet();
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
        await Assert.That((await DiscloseAsync(detail.Projection)).AccessibleAlternativeEventResourceId).IsNull();
        var member = Workflow(context, scope.TenantAId, user);
        var eligible = await member.GetAsync(visible.Id, default);
        bool released = false;
        var outcome = await eligible.Projection!.DiscloseAsync(async (value, _) =>
        {
            await Assert.That(value.AccessibleAlternativeEventResourceId).IsEqualTo(hidden.Id);
            await using var writer = database.CreateIndependentContext();
            await writer.TenantUsers.Where(value => value.UserId == user)
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.StatusId, (int)TenantUserStatusEnum.Suspended));
            return value;
        }, (_, _) => { released = true; return Task.CompletedTask; });
        await Assert.That(outcome).IsEqualTo(EventResourceAuthorityOutcome.NotFound);
        await Assert.That(released).IsFalse();
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
            await Assert.That(result.Projection).IsNull();
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
        await Assert.That(detail.Projection).IsNull();
        var list = await workflow.ListAsync(scope.EventAId, 20, null, default);
        await Assert.That(list.Failure).IsEqualTo(EventResourceAudienceFailure.Unavailable);
        await Assert.That(list.Projection).IsNull();
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
            await Assert.That((await DiscloseAsync(detail.Projection)).IsTeaser).IsTrue();
            var list = await workflow.ListAsync(scope.EventAId, 20, null, default);
            await Assert.That((await DiscloseAsync(list.Projection)).Items.Single().Id).IsEqualTo(resource.Id);
            foreach (var invalid in new[] { "", "malformed", new string('A', 2049),
                _cursors.Protect(new(scope.TenantBId, scope.EventAId, null, false), new(0, resource.Id)) })
            {
                var result = await workflow.ListAsync(scope.EventAId, 20, invalid, default);
                await Assert.That(result.Failure).IsEqualTo(EventResourceAudienceFailure.InvalidRequest);
                await Assert.That(result.Projection).IsNull();
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
        {
            var result = await Workflow(context, scope.TenantAId, user).ListAsync(scope.EventAId, 20, null, default);
            await Assert.That((await DiscloseAsync(result.Projection)).Items.Length).IsEqualTo(5);
        }
        await SaveAsync(Enumerable.Range(5, 495).Select(index => Resource(scope, user, index,
            EventResourceAudienceKindEnum.AuthenticatedTenantMember)).ToArray());
        var large = new SqlRecorder();
        var provider = new Provider();
        await using (var context = database.CreateContext(large))
        {
            var result = await Workflow(context, scope.TenantAId, user, provider).ListAsync(scope.EventAId, 20, null, default);
            await Assert.That((await DiscloseAsync(result.Projection)).Items.Length).IsEqualTo(20);
        }
        await Assert.That(large.Commands.Count).IsLessThanOrEqualTo(small.Commands.Count + 2);
        await Assert.That(provider.Sizes.Contains(500)).IsTrue();
        Console.WriteLine($"Audience SQL growth: five={small.Commands.Count}, five hundred={large.Commands.Count}");
    }

}
