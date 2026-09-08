using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Specifications.EventReports;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class EventReportRepositoryTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task GetByIdAsync_UsesExplicitTenantPredicate_AndDoesNotTrackSafeGraph()
    {
        await fixture.ResetAsync();
        await using var setupContext = fixture.CreateDbContext();
        var (tenant, @event, user, actor) = await SetupEventAsync(setupContext, "report-repository-by-id");
        var report = CreateReport(tenant.Id, @event.Id, user.Id, actor.Id, reasonCode: "spam");
        var reportCase = EventReportCase.Create(tenant.Id, report.Id, "trust-safety", EventReportPriority.Normal, slaDueAt: null);
        setupContext.EventReports.Add(report);
        setupContext.EventReportTargets.Add(EventReportTarget.CreateEventTarget(tenant.Id, report.Id, @event.Id));
        setupContext.EventReportEvidenceItems.Add(EventReportEvidence.CreateReporterText(
            tenant.Id,
            report.Id,
            "encrypted:repository-detail",
            EventReportEvidenceClassification.Sensitive,
            retentionUntil: null,
            user.Id));
        setupContext.EventReportCases.Add(reportCase);
        await setupContext.SaveChangesAsync();

        await using var filteredContext = fixture.CreateTenantFilteredDbContext(new StaticTenantContext(Guid.CreateVersion7()));
        var repository = new EventReportRepository(filteredContext);

        var result = await repository.GetByIdAsync(tenant.Id, report.Id, CancellationToken.None);
        var wrongTenantResult = await repository.GetByIdAsync(Guid.CreateVersion7(), report.Id, CancellationToken.None);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Targets.Count).IsEqualTo(1);
        await Assert.That(result.Cases.Count).IsEqualTo(1);
        await Assert.That(result.EvidenceItems.Count).IsEqualTo(0);
        await Assert.That(wrongTenantResult).IsNull();
        await Assert.That(filteredContext.ChangeTracker.Entries().Count()).IsEqualTo(0);
    }

    [Test]
    public async Task GetByIdWithEvidenceAsync_IncludesEvidenceOnlyOnExplicitDetailRead()
    {
        await fixture.ResetAsync();
        await using var setupContext = fixture.CreateDbContext();
        var (tenant, @event, user, actor) = await SetupEventAsync(setupContext, "report-repository-detail");
        var report = CreateReport(tenant.Id, @event.Id, user.Id, actor.Id, reasonCode: "spam");
        setupContext.EventReports.Add(report);
        setupContext.EventReportTargets.Add(EventReportTarget.CreateEventTarget(tenant.Id, report.Id, @event.Id));
        setupContext.EventReportEvidenceItems.Add(EventReportEvidence.CreateReporterText(
            tenant.Id,
            report.Id,
            "encrypted:explicit-detail",
            EventReportEvidenceClassification.Sensitive,
            retentionUntil: null,
            user.Id));
        setupContext.EventReportCases.Add(EventReportCase.Create(
            tenant.Id,
            report.Id,
            "trust-safety",
            EventReportPriority.Normal,
            slaDueAt: null));
        await setupContext.SaveChangesAsync();

        await using var filteredContext = fixture.CreateTenantFilteredDbContext(new StaticTenantContext(Guid.CreateVersion7()));
        var repository = new EventReportRepository(filteredContext);

        var result = await repository.GetByIdWithEvidenceAsync(tenant.Id, report.Id, CancellationToken.None);
        var wrongTenantResult = await repository.GetByIdWithEvidenceAsync(Guid.CreateVersion7(), report.Id, CancellationToken.None);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Targets.Count).IsEqualTo(1);
        await Assert.That(result.Cases.Count).IsEqualTo(1);
        await Assert.That(result.EvidenceItems.Count).IsEqualTo(1);
        await Assert.That(result.EvidenceItems.Single().TextBodyEncrypted).IsEqualTo("encrypted:explicit-detail");
        await Assert.That(wrongTenantResult).IsNull();
        await Assert.That(filteredContext.ChangeTracker.Entries().Count()).IsEqualTo(0);
    }

    [Test]
    public async Task GetByEventAsync_ReturnsNewestReportsFirst_ForCurrentTenant()
    {
        await fixture.ResetAsync();
        await using var setupContext = fixture.CreateDbContext();
        var (tenant, @event, user, actor) = await SetupEventAsync(setupContext, "report-repository-event");
        var now = DateTime.UtcNow;
        var olderReport = CreateReport(
            tenant.Id,
            @event.Id,
            user.Id,
            actor.Id,
            reasonCode: "spam",
            createdAt: now.AddHours(-3));
        var newerReport = CreateReport(
            tenant.Id,
            @event.Id,
            user.Id,
            actor.Id,
            reasonCode: "harassment",
            createdAt: now.AddHours(-1));
        setupContext.EventReports.AddRange(olderReport, newerReport);
        await setupContext.SaveChangesAsync();
        await SetReportCreatedAtAsync(setupContext, olderReport.Id, now.AddHours(-3));
        await SetReportCreatedAtAsync(setupContext, newerReport.Id, now.AddHours(-1));

        await using var filteredContext = fixture.CreateTenantFilteredDbContext(new StaticTenantContext(tenant.Id));
        var repository = new EventReportRepository(filteredContext);

        var results = await repository.GetByEventAsync(
            tenant.Id,
            @event.Id,
            limit: 10,
            cancellationToken: CancellationToken.None);

        await Assert.That(results.Count).IsEqualTo(2);
        await Assert.That(results[0].Id).IsEqualTo(newerReport.Id);
        await Assert.That(results[1].Id).IsEqualTo(olderReport.Id);
        await Assert.That(filteredContext.ChangeTracker.Entries().Count()).IsEqualTo(0);
    }

    [Test]
    public async Task GetByReporterAsync_ReturnsPagedReporterReportsOnly()
    {
        await fixture.ResetAsync();
        await using var setupContext = fixture.CreateDbContext();
        var (tenant, @event, user, actor) = await SetupEventAsync(setupContext, "report-repository-reporter");
        var (otherUser, otherActor) = await CreateUserActorAsync(setupContext, tenant.Id, "report-repository-other-reporter");
        var now = DateTime.UtcNow;
        var firstReport = CreateReport(
            tenant.Id,
            @event.Id,
            user.Id,
            actor.Id,
            reasonCode: "spam",
            createdAt: now.AddHours(-2));
        var secondReport = CreateReport(
            tenant.Id,
            @event.Id,
            user.Id,
            actor.Id,
            reasonCode: "harassment",
            createdAt: now.AddHours(-1));
        var otherReporterReport = CreateReport(tenant.Id, @event.Id, otherUser.Id, otherActor.Id, reasonCode: "spam");
        setupContext.EventReports.AddRange(firstReport, secondReport, otherReporterReport);
        await setupContext.SaveChangesAsync();
        await SetReportCreatedAtAsync(setupContext, firstReport.Id, now.AddHours(-2));
        await SetReportCreatedAtAsync(setupContext, secondReport.Id, now.AddHours(-1));

        await using var filteredContext = fixture.CreateTenantFilteredDbContext(new StaticTenantContext(tenant.Id));
        var repository = new EventReportRepository(filteredContext);

        var (items, totalCount) = await repository.GetByReporterAsync(
            tenant.Id,
            user.Id,
            pageNumber: 1,
            pageSize: 1,
            cancellationToken: CancellationToken.None);

        await Assert.That(totalCount).IsEqualTo(2);
        await Assert.That(items.Count).IsEqualTo(1);
        await Assert.That(items[0].Id).IsEqualTo(secondReport.Id);
    }

    [Test]
    public async Task GetReportQueueAsync_AppliesSpecificationFiltersAndDefaultQueueOrdering()
    {
        await fixture.ResetAsync();
        await using var setupContext = fixture.CreateDbContext();
        var (tenant, @event, user, actor) = await SetupEventAsync(setupContext, "report-repository-queue");
        var now = DateTime.UtcNow;
        var normalReport = CreateReport(
            tenant.Id,
            @event.Id,
            user.Id,
            actor.Id,
            reasonCode: "spam",
            createdAt: now.AddHours(-2),
            priority: EventReportPriority.Normal);
        var urgentReport = CreateReport(
            tenant.Id,
            @event.Id,
            user.Id,
            actor.Id,
            reasonCode: "illegal_content",
            createdAt: now.AddHours(-1),
            priority: EventReportPriority.Urgent);
        var otherQueueReport = CreateReport(
            tenant.Id,
            @event.Id,
            user.Id,
            actor.Id,
            reasonCode: "off_topic",
            createdAt: now,
            priority: EventReportPriority.High);
        setupContext.EventReports.AddRange(normalReport, urgentReport, otherQueueReport);
        setupContext.EventReportTargets.AddRange(
            EventReportTarget.CreateEventTarget(tenant.Id, normalReport.Id, @event.Id),
            EventReportTarget.CreateEventTarget(tenant.Id, urgentReport.Id, @event.Id),
            EventReportTarget.CreateEventTarget(tenant.Id, otherQueueReport.Id, @event.Id));
        setupContext.EventReportCases.AddRange(
            EventReportCase.Create(tenant.Id, normalReport.Id, "trust-safety", EventReportPriority.Normal, slaDueAt: null),
            EventReportCase.Create(tenant.Id, urgentReport.Id, "trust-safety", EventReportPriority.Urgent, slaDueAt: null),
            EventReportCase.Create(tenant.Id, otherQueueReport.Id, "organizer-support", EventReportPriority.High, slaDueAt: null));
        await setupContext.SaveChangesAsync();
        await SetReportCreatedAtAsync(setupContext, normalReport.Id, now.AddHours(-2));
        await SetReportCreatedAtAsync(setupContext, urgentReport.Id, now.AddHours(-1));
        await SetReportCreatedAtAsync(setupContext, otherQueueReport.Id, now);

        await using var filteredContext = fixture.CreateTenantFilteredDbContext(new StaticTenantContext(tenant.Id));
        var repository = new EventReportRepository(filteredContext);
        var specification = new EventReportQuerySpecification()
            .And(EventReportFilter.QueueCode("trust-safety"))
            .And(EventReportFilter.OpenQueueItems());

        var (items, totalCount) = await repository.GetReportQueueAsync(
            tenant.Id,
            pageNumber: 1,
            pageSize: 10,
            specification: specification,
            cancellationToken: CancellationToken.None);

        await Assert.That(totalCount).IsEqualTo(2);
        await Assert.That(items.Count).IsEqualTo(2);
        await Assert.That(items[0].Id).IsEqualTo(urgentReport.Id);
        await Assert.That(items[0].Targets.Count).IsEqualTo(1);
        await Assert.That(items[0].Cases.Count).IsEqualTo(1);
        await Assert.That(items[1].Id).IsEqualTo(normalReport.Id);
    }

    [Test]
    public async Task DuplicateAndRateLimitLookups_UseReporterEventAndTimeWindow()
    {
        await fixture.ResetAsync();
        await using var setupContext = fixture.CreateDbContext();
        var (tenant, @event, user, actor) = await SetupEventAsync(setupContext, "report-repository-duplicates");
        var now = DateTime.UtcNow;
        var recentSpamReport = CreateReport(
            tenant.Id,
            @event.Id,
            user.Id,
            actor.Id,
            reasonCode: "spam",
            createdAt: now.AddHours(-2));
        var recentHarassmentReport = CreateReport(
            tenant.Id,
            @event.Id,
            user.Id,
            actor.Id,
            reasonCode: "harassment",
            createdAt: now.AddHours(-1));
        var oldSpamReport = CreateReport(
            tenant.Id,
            @event.Id,
            user.Id,
            actor.Id,
            reasonCode: "spam",
            createdAt: now.AddDays(-2));
        setupContext.EventReports.AddRange(recentSpamReport, recentHarassmentReport, oldSpamReport);
        await setupContext.SaveChangesAsync();
        await SetReportCreatedAtAsync(setupContext, recentSpamReport.Id, now.AddHours(-2));
        await SetReportCreatedAtAsync(setupContext, recentHarassmentReport.Id, now.AddHours(-1));
        await SetReportCreatedAtAsync(setupContext, oldSpamReport.Id, now.AddDays(-2));

        await using var filteredContext = fixture.CreateTenantFilteredDbContext(new StaticTenantContext(Guid.CreateVersion7()));
        var repository = new EventReportRepository(filteredContext);
        var twentyFourHoursAgo = now.AddHours(-24);

        var duplicateExists = await repository.ExistsByReporterAndEventAsync(
            tenant.Id,
            @event.Id,
            user.Id,
            actor.Id,
            reporterIpHash: null,
            reporterUserAgentHash: null,
            "spam",
            "misleading",
            twentyFourHoursAgo,
            CancellationToken.None);
        var differentRemedyExists = await repository
            .ExistsByReporterAndEventAsync(
                tenant.Id,
                @event.Id,
                user.Id,
                actor.Id,
                reporterIpHash: null,
                reporterUserAgentHash: null,
                "spam",
                "legal_or_copyright_complaint",
                twentyFourHoursAgo,
                CancellationToken.None);
        var outsideWindowExists = await repository.ExistsByReporterAndEventAsync(
            tenant.Id,
            @event.Id,
            user.Id,
            actor.Id,
            reporterIpHash: null,
            reporterUserAgentHash: null,
            "spam",
            "misleading",
            now.AddMinutes(-30),
            CancellationToken.None);
        var wrongTenantExists = await repository.ExistsByReporterAndEventAsync(
            Guid.CreateVersion7(),
            @event.Id,
            user.Id,
            actor.Id,
            reporterIpHash: null,
            reporterUserAgentHash: null,
            "spam",
            "misleading",
            twentyFourHoursAgo,
            CancellationToken.None);
        var reporterCount = await repository.CountByReporterSinceAsync(
            tenant.Id,
            user.Id,
            actor.Id,
            reporterIpHash: null,
            reporterUserAgentHash: null,
            twentyFourHoursAgo,
            CancellationToken.None);
        var reporterEventCount = await repository.CountByReporterAndEventSinceAsync(
            tenant.Id,
            @event.Id,
            user.Id,
            actor.Id,
            reporterIpHash: null,
            reporterUserAgentHash: null,
            twentyFourHoursAgo,
            CancellationToken.None);
        var eventCount = await repository.CountByEventSinceAsync(
            tenant.Id,
            @event.Id,
            twentyFourHoursAgo,
            CancellationToken.None);

        await Assert.That(duplicateExists).IsTrue();
        await Assert.That(differentRemedyExists).IsFalse();
        await Assert.That(outsideWindowExists).IsFalse();
        await Assert.That(wrongTenantExists).IsFalse();
        await Assert.That(reporterCount).IsEqualTo(2);
        await Assert.That(reporterEventCount).IsEqualTo(2);
        await Assert.That(eventCount).IsEqualTo(2);
    }

    private static EventReport CreateReport(
        Guid tenantId,
        Guid eventId,
        Guid? userId,
        Guid? actorId,
        string reasonCode,
        DateTime? createdAt = null,
        EventReportPriority priority = EventReportPriority.Normal)
    {
        return EventReport.Create(
            tenantId,
            eventId,
            userId,
            actorId,
            userId.HasValue ? EventReporterKind.AuthenticatedUser : EventReporterKind.Anonymous,
            EventReportSourceKind.UserReport,
            reasonCode,
            "misleading",
            priority,
            EventReportSeverityHint.Medium,
            reportCaseUpdatesConsent: false,
            reportFollowUpContactConsent: userId.HasValue,
            "en",
            "iphash-" + Guid.NewGuid().ToString("N")[..16],
            "uahash-" + Guid.NewGuid().ToString("N")[..16],
            createdAt);
    }

    private static async Task<(Tenant Tenant, Explore.Domain.Event Event, User User, Actor Actor)> SetupEventAsync(
        ExploreDbContext context,
        string slugPrefix)
    {
        var tenant = new Tenant
        {
            FullName = "Report Repository Tenant " + slugPrefix,
            Slug = slugPrefix + "-" + Guid.NewGuid().ToString("N")[..8],
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        var (user, actor) = await CreateUserActorAsync(context, tenant.Id, slugPrefix);

        var @event = new Explore.Domain.Event(EventStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            Title = "Report Repository Event " + slugPrefix,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            ActorId = actor.Id,
            Actor = null!,
            TenantId = tenant.Id,
            Tenant = null!,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormatId = (int)EventFormatEnum.Digital,
            EventFormat = null!
        };
        context.Events.Add(@event);
        await context.SaveChangesAsync();

        return (tenant, @event, user, actor);
    }

    private static async Task<(User User, Actor Actor)> CreateUserActorAsync(
        ExploreDbContext context,
        Guid tenantId,
        string slugPrefix)
    {
        var user = new User
        {
            Pii = new UserPii
            {
                Email = $"{slugPrefix}-{Guid.NewGuid():N}@example.com",
                FirstName = "Report",
                LastName = "Reporter"
            }
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var actor = new Actor
        {
            Pii = new ActorPii { DisplayName = "Report Repository Actor " + slugPrefix },
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            UserId = user.Id
        };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();

        return (user, actor);
    }

    private static async Task SetReportCreatedAtAsync(ExploreDbContext context, Guid reportId, DateTime createdAt)
    {
        await context.EventReports
            .Where(report => report.Id == reportId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(report => report.CreatedAt, createdAt));
    }

    private sealed record StaticTenantContext(Guid TenantId) : ITenantContext;
}
