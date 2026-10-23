using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Notifications;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Privacy.ErasureAuthority.Repositories;
using Explore.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationResourceQuarantineExecutionTests
{
    [Test]
    public async Task OriginalPurposeExpiryDoesNotPreventDurableOverdueEscalationOrReleaseMaterial()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Assert.That((await fixture.ExecuteAsync()).Outcome).IsEqualTo(ModerationResourceQuarantineOutcome.Applied);
        await using var privacy = await DutyPrivacyDatabase.CreateAsync();
        fixture.Clock.UtcNow = Now.AddHours(1);
        var first = await DutyPassAsync(fixture, privacy);
        await Assert.That(first.Available).IsTrue();
        await Assert.That(first.Results.Single().Outcome).IsEqualTo(ModerationResourceReviewDutyOutcome.Escalated);
        var duty = first.Results.Single().Duty!;
        await Assert.That(duty.DueAt).IsEqualTo(Now.AddHours(1));
        await Assert.That(duty.AppliedAt).IsEqualTo(Now);
        await Assert.That(first.Results.Single().Escalation!.PurposeUntil).IsEqualTo(Now.AddHours(1).AddMinutes(5));
        await using (var reader = fixture.Context())
        {
            reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
            var original = await reader.ModerationNoticeSources.AsNoTracking().SingleAsync();
            await Assert.That(original.RetainUntil!.Value).IsLessThan(fixture.Clock.UtcNow);
            await Assert.That(await reader.Set<EventResourceModerationReviewEscalation>().CountAsync()).IsEqualTo(1);
            await Assert.That(await reader.NotificationIntents.CountAsync()).IsEqualTo(first.Results.Single().Escalation!.RecipientCount);
            await Assert.That(await reader.Notifications.CountAsync()).IsEqualTo(first.Results.Single().Escalation!.RecipientCount);
            await Assert.That(await reader.EmailDispatchOutbox.CountAsync()).IsEqualTo(0);
            await Assert.That((await reader.EventResources.SingleAsync(row => row.Id == fixture.ResourceId)).HasPublishablePayload()).IsFalse();
            await Assert.That((await reader.EventResourceModerationQuarantines.SingleAsync()).ReleaseId).IsNull();
        }
        fixture.Clock.UtcNow = Now.AddDays(5);
        var replay = await DutyPassAsync(fixture, privacy);
        await Assert.That(replay.Results.Single().Outcome).IsEqualTo(ModerationResourceReviewDutyOutcome.AlreadyEscalated);
        await Assert.That(replay.Results.Single().Escalation!.EscalatedAt).IsEqualTo(Now.AddHours(1));
        await using var final = fixture.Context();
        await Assert.That(await final.NotificationIntents.CountAsync()).IsEqualTo(first.Results.Single().Escalation!.RecipientCount);
    }

    [Test]
    public async Task ExactObjectDeadlineEscalatesButEarlierDiscoveryDoesNotRestartTheClock()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Assert.That((await fixture.ExecuteAsync()).Outcome).IsEqualTo(ModerationResourceQuarantineOutcome.Applied);
        await using var privacy = await DutyPrivacyDatabase.CreateAsync();
        var discovered = await DutyPassAsync(fixture, privacy);
        await Assert.That(discovered.Results.Single().Outcome).IsEqualTo(ModerationResourceReviewDutyOutcome.AwaitingReview);
        fixture.Clock.UtcNow = Now.AddHours(1).AddTicks(-1);
        await Assert.That((await DutyPassAsync(fixture, privacy)).Results.Single().Outcome)
            .IsEqualTo(ModerationResourceReviewDutyOutcome.AwaitingReview);
        fixture.Clock.UtcNow = Now.AddHours(1);
        var overdue = await DutyPassAsync(fixture, privacy);
        await Assert.That(overdue.Results.Single().Outcome).IsEqualTo(ModerationResourceReviewDutyOutcome.Escalated);
        await Assert.That(overdue.Results.Single().Duty!.DueAt).IsEqualTo(discovered.Results.Single().Duty!.DueAt);
    }

    [Test]
    public async Task UnavailableNativeRouteDoesNotInventDeliveryAndTheNextPassCanEscalate()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Assert.That((await fixture.ExecuteAsync()).Outcome).IsEqualTo(ModerationResourceQuarantineOutcome.Applied);
        await using var privacy = await DutyPrivacyDatabase.CreateAsync();
        fixture.Clock.UtcNow = Now.AddHours(1);
        var unavailable = await DutyPassAsync(fixture, privacy, NotificationOwnership.Disabled);
        await Assert.That(unavailable.Results.Single().Outcome).IsEqualTo(ModerationResourceReviewDutyOutcome.RecipientUnavailable);
        await using (var reader = fixture.Context())
        {
            await Assert.That(await reader.Set<EventResourceModerationReviewEscalation>().CountAsync()).IsEqualTo(0);
            await Assert.That(await reader.NotificationIntents.CountAsync()).IsEqualTo(0);
            await Assert.That((await reader.EventResources.SingleAsync(row => row.Id == fixture.ResourceId)).HasModerationQuarantine).IsTrue();
        }
        await Assert.That((await DutyPassAsync(fixture, privacy)).Results.Single().Outcome)
            .IsEqualTo(ModerationResourceReviewDutyOutcome.Escalated);
    }

    [Test]
    public async Task FiniteEscalationPurposeExpiryAfterFlushRollsBackNotificationsAndReceipt()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Assert.That((await fixture.ExecuteAsync()).Outcome).IsEqualTo(ModerationResourceQuarantineOutcome.Applied);
        await using var privacy = await DutyPrivacyDatabase.CreateAsync();
        fixture.Clock.UtcNow = Now.AddHours(1);
        var barrier = new ExpireDutyAfterFlush(fixture.Clock);
        var result = await DutyPassAsync(fixture, privacy, interceptors: [barrier]);
        await Assert.That(barrier.Observed).IsTrue();
        await Assert.That(result.Results.Single().Outcome).IsEqualTo(ModerationResourceReviewDutyOutcome.Unavailable);
        await using var reader = fixture.Context();
        await Assert.That(await reader.Set<EventResourceModerationReviewEscalation>().CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.NotificationIntents.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.Notifications.CountAsync()).IsEqualTo(0);
        await Assert.That((await reader.EventResourceModerationQuarantines.SingleAsync()).ReleaseId).IsNull();
    }

    [Test]
    [Arguments("current")]
    [Arguments("purpose-deadline")]
    [Arguments("credential-expiry")]
    public async Task OverdueEscalationRechecksItsIndependentPurposeAfterDisclosureFinalization(string boundary)
    {
        await using var fixture = await Fixture.CreateAsync();
        var applied = await fixture.ExecuteAsync();
        await Assert.That(applied.Outcome).IsEqualTo(ModerationResourceQuarantineOutcome.Applied);
        var cause = applied.Receipt!;
        await using var privacy = await DutyPrivacyDatabase.CreateAsync();
        fixture.Clock.UtcNow = Now.AddHours(1);
        long? originalEpoch;
        DateTime? originalKeyExpiry;
        await using (var reader = fixture.Context())
        {
            originalEpoch = await reader.Set<EventDiscoveryRevision>().AsNoTracking()
                .Where(row => row.TenantId == fixture.Database.TenantId)
                .Select(row => (long?)row.DisclosureEpoch).SingleOrDefaultAsync();
            originalKeyExpiry = await reader.ExternalApiKeys.Where(row => row.KeyId == fixture.KeyId)
                .Select(row => row.ExpiresAt).SingleAsync();
        }
        var advancement = new QuarantineDisclosureAdvancement(fixture.Database.TenantId, originalEpoch,
            async context =>
            {
                if (boundary == "purpose-deadline")
                    fixture.Clock.UtcNow = Now.AddHours(1).AddMinutes(5);
                if (boundary == "credential-expiry")
                    await context.ExternalApiKeys.Where(row => row.KeyId == fixture.KeyId)
                        .ExecuteUpdateAsync(update => update.SetProperty(
                            row => row.ExpiresAt, fixture.Clock.UtcNow.UtcDateTime));
            });
        var saved = new EnlistDutyDisclosure(fixture.Database.TenantId, advancement);
        var result = await DutyPassAsync(fixture, privacy, interceptors: [saved, advancement]);
        await Assert.That(saved.ObservedEscalation).IsTrue();
        await Assert.That(advancement.ObservedAdvancement).IsTrue();
        await Assert.That(result.Available).IsTrue();
        var escalated = boundary == "current";
        var outcome = result.Results.Single();
        await Assert.That(outcome.Outcome).IsEqualTo(escalated
            ? ModerationResourceReviewDutyOutcome.Escalated : ModerationResourceReviewDutyOutcome.Unavailable);
        await Assert.That(outcome.Escalation is not null).IsEqualTo(escalated);
        await AssertState(fixture, 1);
        await using var verify = fixture.Context();
        var epoch = await verify.Set<EventDiscoveryRevision>().AsNoTracking()
            .Where(row => row.TenantId == fixture.Database.TenantId)
            .Select(row => (long?)row.DisclosureEpoch).SingleOrDefaultAsync();
        await Assert.That(epoch).IsEqualTo(escalated ? originalEpoch.GetValueOrDefault() + 1 : originalEpoch);
        await Assert.That(await verify.Set<EventResourceModerationReviewDuty>().CountAsync())
            .IsEqualTo(escalated ? 1 : 0);
        await Assert.That(await verify.Set<EventResourceModerationReviewEscalation>().CountAsync())
            .IsEqualTo(escalated ? 1 : 0);
        var recipients = escalated ? outcome.Escalation!.RecipientCount : 0;
        await Assert.That(await verify.NotificationIntents.CountAsync()).IsEqualTo(recipients);
        await Assert.That(await verify.Notifications.CountAsync()).IsEqualTo(recipients);
        await Assert.That(await verify.EmailDispatchOutbox.CountAsync()).IsEqualTo(0);
        await Assert.That(await verify.ExternalApiKeys.Where(row => row.KeyId == fixture.KeyId)
            .Select(row => row.ExpiresAt).SingleAsync()).IsEqualTo(originalKeyExpiry);
        var retained = await verify.EventResourceModerationQuarantines.SingleAsync();
        await Assert.That(retained.Id).IsEqualTo(cause.Id);
        await Assert.That(retained.AppliedAt).IsEqualTo(cause.AppliedAt);
        await Assert.That(retained.ReleaseId).IsNull();
    }

    [Test]
    public async Task ErasureAuthorityExcludesCurrentManagerWithoutRenewingOriginalReviewerAuthority()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Assert.That((await fixture.ExecuteAsync()).Outcome).IsEqualTo(ModerationResourceQuarantineOutcome.Applied);
        await using var privacy = await DutyPrivacyDatabase.CreateAsync();
        fixture.Clock.UtcNow = Now.AddHours(1);
        var erased = fixture.Database.ReviewerId;
        var authority = new EmbeddedPrivacyErasureAuthorityRepository(privacy, fixture.Clock,
            Options.Create(new PrivacyErasureOptions()));
        await authority.AppendAsync(Explore.Application.Contracts.PrivacyErasure.PrivacyErasureRequest.Create(
            Guid.CreateVersion7(), PrivacyErasureSubjectKind.User, erased,
            PrivacyErasureReasonCode.SubjectErasureRequest, 1));
        var result = await DutyPassAsync(fixture, privacy);
        await Assert.That(result.Results.Single().Outcome).IsEqualTo(ModerationResourceReviewDutyOutcome.Escalated);
        await using var reader = fixture.Context();
        await Assert.That(await reader.NotificationIntents.AnyAsync(row => row.RecipientUserId == erased)).IsFalse();
        await Assert.That(await reader.Notifications.AnyAsync(row => row.UserId == erased)).IsFalse();
    }

    [Test]
    public async Task RevokedMaintenanceKeyAndForeignTenantCannotEscalateTheOriginalCause()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Assert.That((await fixture.ExecuteAsync()).Outcome).IsEqualTo(ModerationResourceQuarantineOutcome.Applied);
        await using var privacy = await DutyPrivacyDatabase.CreateAsync();
        fixture.Clock.UtcNow = Now.AddHours(1);
        var foreign = await DutyPassAsync(fixture, privacy, tenantId: fixture.OtherTenantId);
        await Assert.That(foreign.Available).IsTrue();
        await Assert.That(foreign.Results.Length).IsEqualTo(0);
        var revoked = await DutyPassAsync(fixture, privacy, expireKeyAfterAuthentication: true);
        await Assert.That(revoked.Available).IsFalse();
        await using var reader = fixture.Context();
        await Assert.That(await reader.Set<EventResourceModerationReviewEscalation>().CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.NotificationIntents.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task AFullPageReturnsItsNativeCursorAndTheFollowingEmptyPageWraps()
    {
        await using var fixture = await Fixture.CreateAsync();
        var applied = await fixture.ExecuteAsync();
        await Assert.That(applied.Outcome).IsEqualTo(ModerationResourceQuarantineOutcome.Applied);
        await using var privacy = await DutyPrivacyDatabase.CreateAsync();
        var page = await DutyPassAsync(fixture, privacy);
        await Assert.That(page.LastScannedQuarantineId).IsEqualTo(applied.Receipt!.Id);
        var end = await DutyPassAsync(fixture, privacy, after: page.LastScannedQuarantineId);
        await Assert.That(end.Available).IsTrue();
        await Assert.That(end.Results.Length).IsEqualTo(0);
        await Assert.That(end.LastScannedQuarantineId).IsNull();
    }

    private static async Task<ModerationResourceReviewDutyBatch> DutyPassAsync(
        Fixture fixture, DutyPrivacyDatabase privacy,
        NotificationOwnership route = NotificationOwnership.IslamuEvent, Guid? tenantId = null,
        bool expireKeyAfterAuthentication = false, Guid? after = null, params IInterceptor[] interceptors)
    {
        await using var context = fixture.Context(interceptors);
        var identity = await fixture.AuthenticateAsync(context);
        if (tenantId is { } tenant) context.TenantContext = new SelectedTenant(tenant);
        if (expireKeyAfterAuthentication)
        {
            await using var writer = fixture.Context();
            await writer.ExternalApiKeys.Where(row => row.KeyId == fixture.KeyId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAt, fixture.Clock.UtcNow.UtcDateTime));
        }
        return await CreateDutyRepository(fixture, privacy, context, identity, route).ExecutePassAsync(after, 1);
    }

    private static ModerationResourceQuarantineDutyRepository CreateDutyRepository(
        Fixture fixture, DutyPrivacyDatabase privacy, ExploreDbContext context,
        (MachinePrincipalAccessor Machine, ModerationMachineSubjectBindingRepository Bindings) identity,
        NotificationOwnership route = NotificationOwnership.IslamuEvent)
    {
        var unit = new EfCoreUnitOfWork(context);
        var settings = new RelationalSettingMutationLock(context, unit);
        var graph = new RecipientNotificationMaterializer(
            new NotificationIntentRepository(context, settings, fixture.Clock), unit, settings,
            new PrivacyErasureStateRepository(context));
        var materializer = new ModerationResourceReviewEscalationMaterializer(
            new DefaultNotificationOwnershipResolver(Options.Create(new NotificationRoutingOptions
            {
                TrustSafetyModerationOwner = route
            })), graph);
        return new ModerationResourceQuarantineDutyRepository(context, identity.Machine, identity.Bindings,
            new ExternalApiKeyRepository(context),
            new EmbeddedPrivacyErasureAuthorityRepository(privacy, fixture.Clock, Options.Create(new PrivacyErasureOptions()),
                applicationContext: context), materializer, settings, fixture.Clock);
    }

    private sealed class EnlistDutyDisclosure(Guid tenantId, QuarantineDisclosureAdvancement advancement)
        : SaveChangesInterceptor
    {
        public bool ObservedEscalation { get; private set; }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (!ObservedEscalation && eventData.Context is ExploreDbContext context
                && !context.ChangeTracker.HasChanges()
                && context.ChangeTracker.Entries<EventResourceModerationReviewEscalation>().Any())
            {
                await Assert.That(context.IdentityFenceOwnsTransaction).IsFalse();
                ObservedEscalation = true;
                context.DisclosureMutations.Enlist([tenantId]);
                advancement.Armed = true;
            }
            return result;
        }
    }

    private sealed class ExpireDutyAfterFlush(ExecutionClock clock) : SaveChangesInterceptor
    {
        public bool Observed { get; private set; }
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (!Observed)
            {
                Observed = true;
                clock.UtcNow = clock.UtcNow.AddMinutes(5);
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class DutyPrivacyDatabase(
        SqliteConnection connection, DbContextOptions<EmbeddedPrivacyErasureAuthorityDbContext> options)
        : IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>, IAsyncDisposable
    {
        public EmbeddedPrivacyErasureAuthorityDbContext CreateDbContext() => new(options);
        public static async Task<DutyPrivacyDatabase> CreateAsync()
        {
            var connection = await SqliteTestDatabaseFactory.CreateOpenIsolatedConnectionAsync();
            var options = TestDbContextOptions.Create<EmbeddedPrivacyErasureAuthorityDbContext>()
                .UseSqlite(connection).UseSnakeCaseNamingConvention().Options;
            var result = new DutyPrivacyDatabase(connection, options);
            await using var context = result.CreateDbContext();
            await context.Database.EnsureCreatedAsync();
            return result;
        }
        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }
}
