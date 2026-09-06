// ABOUTME: Proves the catalogued sensitive collections disclose no rows, counts, or existence out of scope.
// ABOUTME: Constraints must land before Count/Skip/Take, so an unauthorized caller cannot even read a total.

using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests;

/// <summary>
/// Phase 4 acceptance, asserted where it is actually decided.
/// <para>
/// A paginated read that filters after paging still leaks: the caller learns the true total, and the
/// short pages tell them where the withheld rows sit. So these tests assert the count as well as the
/// rows — a repository that returned an empty page with a non-zero total would pass a rows-only test
/// while still disclosing exactly what Phase 4 forbids.
/// </para>
/// <para>
/// Coverage is deliberately concentrated on the collections whose scope the ambient tenant filter does
/// <em>not</em> supply: the support-access pair keys off the session's target tenant, which is by
/// definition not the caller's tenant, and the shared-contacts collection keys off the consented
/// recipient actor. Collections whose catalogued scope is
/// <see cref="SensitiveCollectionScope.Tenant"/> are covered by the existing global-query-filter tests.
/// </para>
/// </summary>
public sealed class SensitiveCollectionDisclosureTests
{
    private static readonly DateTime Now = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Support-access sessions are scoped by the tenant the session targets, not by the ambient tenant,
    /// so the global filter cannot protect them. One tenant learning that another was under support
    /// access — even just the count — is a disclosure about that customer's relationship with the operator.
    /// </summary>
    [Test]
    public async Task SupportAccessSessions_DiscloseNoRowsOrCountForAnotherTenant()
    {
        string database = $"support-access-sessions-{Guid.NewGuid():N}";
        var root = new InMemoryDatabaseRoot();
        Guid targetTenantId = Guid.CreateVersion7();
        Guid otherTenantId = Guid.CreateVersion7();

        await using (ExploreDbContext seed = CreateContext(database, root, targetTenantId))
        {
            seed.SupportAccessSessions.AddRange(
                CreateSupportAccessSession(targetTenantId),
                CreateSupportAccessSession(targetTenantId),
                CreateSupportAccessSession(otherTenantId));
            await seed.SaveChangesAsync();
        }

        await using ExploreDbContext context = CreateContext(database, root, targetTenantId);
        var repository = new SupportAccessSessionRepository(context);

        var visible = await repository.ListForTargetTenantAsync(targetTenantId, limit: 100);
        var foreign = await repository.ListForTargetTenantAsync(otherTenantId, limit: 100);

        await Assert.That(visible.Count).IsEqualTo(2);
        await Assert.That(visible.All(session => session.TargetTenantId == targetTenantId)).IsTrue();

        // The other tenant's single session must not appear, and asking for it must not confirm it exists.
        await Assert.That(foreign.All(session => session.TargetTenantId == otherTenantId)).IsTrue();
        await Assert.That(visible.Any(session => session.TargetTenantId == otherTenantId)).IsFalse();
    }

    /// <summary>
    /// The limit is applied after the scope predicate, never before. If the order were reversed, a caller
    /// could infer how many rows were filtered out by watching the page shrink below the limit.
    /// </summary>
    [Test]
    public async Task SupportAccessSessions_ApplyScopeBeforeTheLimitNotAfter()
    {
        string database = $"support-access-limit-{Guid.NewGuid():N}";
        var root = new InMemoryDatabaseRoot();
        Guid targetTenantId = Guid.CreateVersion7();
        Guid otherTenantId = Guid.CreateVersion7();

        await using (ExploreDbContext seed = CreateContext(database, root, targetTenantId))
        {
            // Ten foreign rows ordered ahead of the two in-scope rows. A limit applied before the scope
            // predicate would return an empty page here while the in-scope rows genuinely exist.
            for (var i = 0; i < 10; i++)
            {
                seed.SupportAccessSessions.Add(
                    CreateSupportAccessSession(otherTenantId, startedAt: Now.AddMinutes(i + 10)));
            }

            seed.SupportAccessSessions.AddRange(
                CreateSupportAccessSession(targetTenantId, startedAt: Now.AddMinutes(1)),
                CreateSupportAccessSession(targetTenantId, startedAt: Now.AddMinutes(2)));
            await seed.SaveChangesAsync();
        }

        await using ExploreDbContext context = CreateContext(database, root, targetTenantId);
        var repository = new SupportAccessSessionRepository(context);

        var page = await repository.ListForTargetTenantAsync(targetTenantId, limit: 5);

        await Assert.That(page.Count).IsEqualTo(2);
        await Assert.That(page.All(session => session.TargetTenantId == targetTenantId)).IsTrue();
    }

    /// <summary>
    /// Shared contacts are released to one recipient actor under explicit consent. The count alone
    /// discloses how many people consented to share with a given organization, so it has to be computed
    /// from the constrained query rather than the table.
    /// </summary>
    [Test]
    public async Task SharedContacts_DiscloseNoRowsOrCountToANonRecipientActor()
    {
        string database = $"shared-contacts-{Guid.NewGuid():N}";
        var root = new InMemoryDatabaseRoot();
        Guid tenantId = Guid.CreateVersion7();
        Guid recipientActorId = Guid.CreateVersion7();
        Guid otherActorId = Guid.CreateVersion7();

        await using (ExploreDbContext seed = CreateContext(database, root, tenantId))
        {
            seed.EventContactShareConsents.AddRange(
                CreateConsent(tenantId, recipientActorId, "granted-one@example.test"),
                CreateConsent(tenantId, recipientActorId, "granted-two@example.test"),
                CreateConsent(tenantId, otherActorId, "not-yours@example.test"));
            await seed.SaveChangesAsync();
        }

        await using ExploreDbContext context = CreateContext(database, root, tenantId);
        var repository = new EventContactShareConsentRepository(context);

        var (mine, myTotal) = await repository.GetGrantedForRecipient(
            tenantId, recipientActorId, eventId: null, emailSearch: null, pageNumber: 1, pageSize: 20);

        await Assert.That(myTotal).IsEqualTo(2);
        await Assert.That(mine.Count).IsEqualTo(2);
        await Assert.That(mine.Any(consent => consent.EmailSnapshot == "not-yours@example.test")).IsFalse();
    }

    /// <summary>
    /// The count must come from the constrained query, not the table. A total larger than the caller's
    /// own rows would tell them how many additional contacts exist that they are not allowed to see.
    /// </summary>
    [Test]
    public async Task SharedContacts_ReportTotalFromTheConstrainedQueryNotTheTable()
    {
        string database = $"shared-contacts-count-{Guid.NewGuid():N}";
        var root = new InMemoryDatabaseRoot();
        Guid tenantId = Guid.CreateVersion7();
        Guid recipientActorId = Guid.CreateVersion7();
        Guid otherActorId = Guid.CreateVersion7();

        await using (ExploreDbContext seed = CreateContext(database, root, tenantId))
        {
            seed.EventContactShareConsents.Add(
                CreateConsent(tenantId, recipientActorId, "mine@example.test"));

            for (var i = 0; i < 9; i++)
            {
                seed.EventContactShareConsents.Add(
                    CreateConsent(tenantId, otherActorId, $"theirs-{i}@example.test"));
            }

            await seed.SaveChangesAsync();
        }

        await using ExploreDbContext context = CreateContext(database, root, tenantId);
        var repository = new EventContactShareConsentRepository(context);

        var (items, totalCount) = await repository.GetGrantedForRecipient(
            tenantId, recipientActorId, eventId: null, emailSearch: null, pageNumber: 1, pageSize: 20);

        // Ten rows exist for this tenant; exactly one is this recipient's. The total must say one.
        await Assert.That(totalCount).IsEqualTo(1);
        await Assert.That(items.Count).IsEqualTo(1);
    }

    /// <summary>
    /// A revoked consent is a withdrawal of permission, so it must drop out of both the rows and the
    /// count. Leaving it in the total would disclose that someone had once consented and then withdrawn.
    /// </summary>
    [Test]
    public async Task SharedContacts_ExcludeRevokedConsentsFromRowsAndCount()
    {
        string database = $"shared-contacts-revoked-{Guid.NewGuid():N}";
        var root = new InMemoryDatabaseRoot();
        Guid tenantId = Guid.CreateVersion7();
        Guid recipientActorId = Guid.CreateVersion7();

        await using (ExploreDbContext seed = CreateContext(database, root, tenantId))
        {
            seed.EventContactShareConsents.AddRange(
                CreateConsent(tenantId, recipientActorId, "still-granted@example.test"),
                CreateConsent(tenantId, recipientActorId, "withdrawn@example.test", withdrawn: true));
            await seed.SaveChangesAsync();
        }

        await using ExploreDbContext context = CreateContext(database, root, tenantId);
        var repository = new EventContactShareConsentRepository(context);

        var (items, totalCount) = await repository.GetGrantedForRecipient(
            tenantId, recipientActorId, eventId: null, emailSearch: null, pageNumber: 1, pageSize: 20);

        await Assert.That(totalCount).IsEqualTo(1);
        await Assert.That(items.Single().EmailSnapshot).IsEqualTo("still-granted@example.test");
    }

    /// <summary>
    /// Cross-tenant isolation for the same recipient actor id. The tenant predicate has to be part of the
    /// constrained query rather than assumed from ambient context.
    /// </summary>
    [Test]
    public async Task SharedContacts_DoNotCrossTenantsForTheSameRecipientActor()
    {
        string database = $"shared-contacts-tenant-{Guid.NewGuid():N}";
        var root = new InMemoryDatabaseRoot();
        Guid tenantId = Guid.CreateVersion7();
        Guid otherTenantId = Guid.CreateVersion7();
        Guid recipientActorId = Guid.CreateVersion7();

        await using (ExploreDbContext seed = CreateContext(database, root, tenantId))
        {
            seed.EventContactShareConsents.Add(
                CreateConsent(tenantId, recipientActorId, "this-tenant@example.test"));
            await seed.SaveChangesAsync();
        }

        await using (ExploreDbContext otherSeed = CreateContext(database, root, otherTenantId))
        {
            otherSeed.EventContactShareConsents.Add(
                CreateConsent(otherTenantId, recipientActorId, "other-tenant@example.test"));
            await otherSeed.SaveChangesAsync();
        }

        await using ExploreDbContext context = CreateContext(database, root, tenantId);
        var repository = new EventContactShareConsentRepository(context);

        var (items, totalCount) = await repository.GetGrantedForRecipient(
            tenantId, recipientActorId, eventId: null, emailSearch: null, pageNumber: 1, pageSize: 20);

        await Assert.That(totalCount).IsEqualTo(1);
        await Assert.That(items.Single().EmailSnapshot).IsEqualTo("this-tenant@example.test");
    }

    /// <summary>
    /// Actor subscriptions are the one catalogued collection whose read <em>deliberately disables the
    /// global tenant filter</em> (`IgnoreTenantFilter` with an exact-predicate bypass reason). That removes
    /// the safety net every other tenant-scoped query leans on, so the explicit tenant and subscriber
    /// predicates are the entire boundary.
    /// <para>
    /// The fixture makes the target actor discoverable in <em>both</em> tenants on purpose. The query also
    /// joins through `WhereLocallyDiscoverable`, and if the actor were discoverable only in the caller's
    /// tenant, that join alone would exclude the foreign row — the test would pass while proving nothing
    /// about the tenant predicate it exists to check. Discoverable in both, only
    /// <c>subscription.TenantId == tenantId</c> can exclude it.
    /// </para>
    /// </summary>
    [Test]
    public async Task ActorSubscriptions_DiscloseNoRowsOrCountForAnotherSubscriberOrTenant()
    {
        string database = $"actor-subscriptions-{Guid.NewGuid():N}";
        var root = new InMemoryDatabaseRoot();
        Guid tenantId = Guid.CreateVersion7();
        Guid otherTenantId = Guid.CreateVersion7();
        Guid subscriberId = Guid.CreateVersion7();
        Guid otherSubscriberId = Guid.CreateVersion7();
        Guid organizationId = Guid.CreateVersion7();
        Guid targetActorId = Guid.CreateVersion7();

        await using (ExploreDbContext seed = CreateContext(database, root, tenantId))
        {
            // The repository Includes these required lookups. Without them the required-navigation join
            // drops the row from the page while Count still returns it — the page and the total would
            // disagree for a reason that has nothing to do with authorization.
            seed.Set<ActorType>().Add(new ActorType
            {
                Id = (int)ActorTypeEnum.Organization,
                MasterCode = "organization",
                FullName = "Organization"
            });
            seed.Set<ActorSubscriptionStatus>().Add(new ActorSubscriptionStatus
            {
                Id = (int)ActorSubscriptionStatusEnum.Active,
                MasterCode = "active",
                FullName = "Active"
            });
            seed.Set<ActorSubscriptionNotificationLevel>().Add(new ActorSubscriptionNotificationLevel
            {
                Id = (int)ActorSubscriptionNotificationLevelEnum.All,
                MasterCode = "all",
                FullName = "All"
            });

            seed.Actors.Add(new Actor
            {
                Id = targetActorId,
                ActorTypeId = (int)ActorTypeEnum.Organization,
                ActorType = null!,
                OrganizationId = organizationId,
                Pii = new ActorPii { DisplayName = "Target" }
            });

            // Approved, visible participation in both tenants keeps discoverability out of the way.
            seed.OrganizationTenants.AddRange(
                CreateParticipation(tenantId, organizationId),
                CreateParticipation(otherTenantId, organizationId));

            seed.ActorSubscriptions.AddRange(
                CreateSubscription(tenantId, subscriberId, targetActorId),
                CreateSubscription(tenantId, otherSubscriberId, targetActorId),
                CreateSubscription(otherTenantId, subscriberId, targetActorId));

            await seed.SaveChangesAsync();
        }

        await using ExploreDbContext context = CreateContext(database, root, tenantId);
        var repository = new ActorSubscriptionRepository(context);

        var (items, totalCount) = await repository.GetBySubscriberPagedAsync(
            tenantId, subscriberId, pageNumber: 1, pageSize: 20);

        // Three rows exist and all three name a discoverable actor. Exactly one is this tenant's and this
        // subscriber's, and the count must say one — not three, and not two.
        await Assert.That(totalCount).IsEqualTo(1);
        await Assert.That(items.Count).IsEqualTo(1);
        await Assert.That(items.Single().SubscriberTenantUserId).IsEqualTo(subscriberId);
        await Assert.That(items.Single().TenantId).IsEqualTo(tenantId);
    }

    private static OrganizationTenant CreateParticipation(Guid tenantId, Guid organizationId) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            OrganizationId = organizationId,
            Organization = null!,
            ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
            ApprovalStatus = null!,
            IsVisible = true,
            IsSuspended = false
        };

    private static ActorSubscription CreateSubscription(Guid tenantId, Guid subscriberId, Guid targetActorId) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            SubscriberTenantUserId = subscriberId,
            SubscriberTenantUser = null!,
            SubscriberUserId = Guid.CreateVersion7(),
            SubscriberUser = null!,
            TargetActorId = targetActorId,
            TargetActor = null!,
            TargetActorTypeId = (int)ActorTypeEnum.Organization,
            TargetActorType = null!,
            StatusId = (int)ActorSubscriptionStatusEnum.Active,
            Status = null!,
            NotificationLevelId = (int)ActorSubscriptionNotificationLevelEnum.All,
            NotificationLevel = null!,
            SubscribedAt = Now
        };

    private static SupportAccessSession CreateSupportAccessSession(Guid targetTenantId, DateTime? startedAt = null)
    {
        DateTimeOffset start = new(startedAt ?? Now, TimeSpan.Zero);
        return SupportAccessSession.Start(
            actorUserId: Guid.CreateVersion7(),
            targetTenantId: targetTenantId,
            mode: SupportAccessModeEnum.ReadOnly,
            reasonCode: "investigation",
            reasonText: "disclosure boundary fixture",
            ticketReference: "TICKET-1",
            startedAtUtc: start,
            expiresAtUtc: start.AddHours(1));
    }

    private static EventContactShareConsent CreateConsent(
        Guid tenantId,
        Guid recipientActorId,
        string email,
        bool withdrawn = false)
    {
        EventContactShareConsent consent = EventContactShareConsent.Grant(
            tenantId: tenantId,
            subjectType: ContactShareConsentSubjectTypeEnum.User,
            subjectId: Guid.CreateVersion7(),
            recipientActorId: recipientActorId,
            purposeCode: "event_updates",
            emailSnapshot: email,
            consentTextSnapshot: "I agree to share my contact details.",
            consentUiVersion: "v1",
            grantedAt: Now);

        if (withdrawn)
        {
            consent.Withdraw(actorId: null, userId: null, withdrawnAt: Now.AddMinutes(5));
        }

        return consent;
    }

    private static ExploreDbContext CreateContext(
        string database,
        InMemoryDatabaseRoot root,
        Guid tenantId)
    {
        var context = new ExploreDbContext(TestDbContextOptions.Create<ExploreDbContext>()
            .UseTestInMemoryDatabase(database, root).Options)
        {
            TenantContext = new TestTenantContext(tenantId)
        };
        return context;
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}
