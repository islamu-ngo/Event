using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Constants;
using Explore.Domain.Services.Scheduling;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed class EventDiscoveryIdentitySurfaceTests
{
    [Test]
    public async Task Independent_reviewer_commits_alias_and_reversal_with_audit_and_durable_intent()
    {
        await using var factory = new NativeEventTagsFactory(relational: true);
        using var client = factory.CreateClient();
        var seed = await SeedAsync(factory, VisibilityTypeEnum.Public, createRelationship: false);
        Guid reviewerId = Guid.CreateVersion7();
        Guid tenantId;
        Guid[] sessionsBefore;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            tenantId = (await context.Events.SingleAsync(entity => entity.Id == seed.SourceId)).TenantId;
            var reviewer = new User
            {
                Id = reviewerId, Pii = new UserPii
                {
                    Email = $"reviewer-{reviewerId:N}@example.test", FirstName = "Independent", LastName = "Reviewer"
                }
            };
            context.Users.Add(reviewer);
            context.TenantUsers.Add(new TenantUser
            {
                Id = Guid.CreateVersion7(), TenantId = tenantId, Tenant = null!,
                UserId = reviewerId, User = reviewer, StatusId = (int)TenantUserStatusEnum.Active
            });
            foreach (Guid id in new[] { seed.SourceId, seed.TargetId })
                context.EventRoleAssignments.Add(EventRoleAssignment.Create(
                    tenantId, id, reviewerId, (int)RoleEnum.EventManager, EventRoleAssignmentStatus.Active,
                    new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, seed.UserId));
            foreach (string code in new[] { PermissionCodes.EventReviewDiscoveryIdentity, PermissionCodes.EventReverseDiscoveryIdentity })
            {
                var permission = await context.Permissions.SingleAsync(row => row.MasterCode == code);
                context.RolePermissions.Add(new RolePermission
                {
                    RoleId = (int)RoleEnum.EventManager, Role = null!, PermissionId = permission.Id, Permission = permission
                });
            }
            await context.SaveChangesAsync();
            sessionsBefore = await context.EventSessions.Where(session =>
                session.EventId == seed.SourceId || session.EventId == seed.TargetId)
                .OrderBy(session => session.Id).Select(session => session.Id).ToArrayAsync();
        }
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(reviewerId));
        using var reviewed = await client.PostAsJsonAsync($"{Route(seed.SourceId)}/review",
            new { PrimaryEventId = seed.TargetId, ExpectedRevision = 0, Decision = "same-offering", ReasonCode = "same_offering" });
        await Assert.That(reviewed.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var stale = await client.PostAsJsonAsync($"{Route(seed.SourceId)}/review",
            new { PrimaryEventId = seed.TargetId, ExpectedRevision = 0, Decision = "same-offering", ReasonCode = "same_offering" });
        await Assert.That(stale.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        using var reversed = await client.PostAsJsonAsync($"{Route(seed.SourceId)}/review",
            new { PrimaryEventId = seed.TargetId, ExpectedRevision = 1, Decision = "reverse", ReasonCode = "reconsidered" });
        await Assert.That(reversed.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await using var verification = factory.Services.CreateAsyncScope();
        var persisted = verification.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var bindings = await persisted.Set<EventDiscoveryIdentity>().Include(row => row.Alias)
            .Where(row => row.TenantId == tenantId).ToArrayAsync();
        await Assert.That(bindings.Length).IsEqualTo(2);
        await Assert.That(bindings.All(row => row.Alias is null)).IsTrue();
        await Assert.That((await persisted.Set<EventDiscoveryRevision>().SingleAsync(row => row.TenantId == tenantId))
            .IdentityEpoch).IsEqualTo(2);
        await Assert.That(await persisted.AuditLogs.CountAsync(row =>
            row.EntityType == nameof(EventDiscoveryIdentity) && row.EntityId == seed.SourceId.ToString("D")
            && row.ActorId == reviewerId)).IsEqualTo(2);
        await Assert.That(await persisted.OutboxMessages.CountAsync(row =>
            row.EventType == "EventDiscoveryIdentityCorrectionRequested" && row.AggregateId == seed.SourceId)).IsEqualTo(2);
        var sessionsAfter = await persisted.EventSessions.Where(session =>
                session.EventId == seed.SourceId || session.EventId == seed.TargetId)
            .OrderBy(session => session.Id).Select(session => session.Id).ToArrayAsync();
        await Assert.That(sessionsAfter).IsEquivalentTo(sessionsBefore);
    }

    [Test]
    [Arguments(VisibilityTypeEnum.Public)]
    [Arguments(VisibilityTypeEnum.Private)]
    public async Task Discovery_selects_one_eligible_whole_member_without_exposing_a_private_primary(
        VisibilityTypeEnum primaryVisibility)
    {
        await using var factory = new NativeEventTagsFactory(relational: true);
        using var client = factory.CreateClient();
        var seed = await SeedAsync(factory, primaryVisibility);
        using var response = await client.GetAsync($"/api/event?searchTerm={Uri.EscapeDataString(seed.Title)}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        string body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var items = document.RootElement.GetProperty("_embedded").GetProperty("items").EnumerateArray().ToArray();
        await Assert.That(items.Length).IsEqualTo(1);
        await Assert.That(items[0].GetProperty("event").GetProperty("id").GetGuid()).IsEqualTo(
            primaryVisibility == VisibilityTypeEnum.Public ? seed.TargetId : seed.SourceId);
        if (primaryVisibility == VisibilityTypeEnum.Private)
        {
            await Assert.That(body).DoesNotContain(seed.TargetId.ToString("D"));
            await Assert.That(body).DoesNotContain("private_primary_reason");
        }
    }

    [Test]
    public async Task Malformed_alias_cycle_fails_closed_instead_of_releasing_cards()
    {
        await using var factory = new NativeEventTagsFactory(relational: true);
        using var client = factory.CreateClient();
        var seed = await SeedAsync(factory, VisibilityTypeEnum.Public);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var source = await context.Set<EventDiscoveryIdentity>()
                .SingleAsync(row => row.SourceKey == seed.SourceId.ToString("D"));
            var primary = await context.Set<EventDiscoveryIdentity>()
                .SingleAsync(row => row.SourceKey == seed.TargetId.ToString("D"));
            var invalidAlias = new EventDiscoveryAlias
            {
                Id = Guid.CreateVersion7(), TenantId = primary.TenantId, MemberIdentityId = primary.Id,
                Member = primary, PrimaryIdentityId = source.Id, Primary = source,
                RelationshipRevision = 1, ReviewerId = seed.UserId, ReasonCode = "invalid_cycle",
                ReviewedAtUtc = DateTime.UtcNow
            };
            context.Set<EventDiscoveryAlias>().Add(invalidAlias);
            primary.Alias = invalidAlias;
            await context.SaveChangesAsync();
        }
        using var response = await client.GetAsync($"/api/event?searchTerm={Uri.EscapeDataString(seed.Title)}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        string body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).DoesNotContain(seed.SourceId.ToString("D"));
        await Assert.That(body).DoesNotContain(seed.TargetId.ToString("D"));
    }

    [Test]
    public async Task PublicRelationshipLinksPreserveOriginalRecordAndNeverOfferAnonymousDecisions()
    {
        await using var factory = new NativeEventTagsFactory(relational: true);
        using var client = factory.CreateClient();
        var seed = await SeedAsync(factory, VisibilityTypeEnum.Public);
        using var response = await client.GetAsync(Route(seed.SourceId));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var links = root.GetProperty("_links");
        await Assert.That(root.GetProperty("eventId").GetGuid()).IsEqualTo(seed.SourceId);
        await Assert.That(root.GetProperty("publicPrimaryEventId").GetGuid()).IsEqualTo(seed.TargetId);
        await Assert.That(links.GetProperty("original-event").GetProperty("href").GetString()).Contains(seed.SourceId.ToString());
        await Assert.That(links.GetProperty("canonical").GetProperty("href").GetString()).Contains(seed.TargetId.ToString());
        await Assert.That(links.TryGetProperty("review", out _)).IsFalse();
        await Assert.That(links.TryGetProperty("reverse", out _)).IsFalse();
        await Assert.That(links.TryGetProperty("candidates", out _)).IsFalse();
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PrivatePrimaryHasNoIdentifierReasonOrRelationshipLinkEvenForSourceOwner(bool authenticated)
    {
        await using var factory = new NativeEventTagsFactory(relational: true);
        using var client = factory.CreateClient();
        var seed = await SeedAsync(factory, VisibilityTypeEnum.Private);
        if (authenticated)
            client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName,
                TestAuthHandler.CreateAuthHeaderValue(seed.UserId));
        using var response = await client.GetAsync($"{Route(seed.SourceId)}?candidateEventId={seed.TargetId:D}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        string body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).DoesNotContain(seed.TargetId.ToString());
        await Assert.That(body).DoesNotContain("private_primary_reason");
        using var document = JsonDocument.Parse(body);
        await Assert.That(document.RootElement.GetProperty("_links").TryGetProperty("canonical", out _)).IsFalse();
        await Assert.That(document.RootElement.GetProperty("_links").TryGetProperty("reverse", out _)).IsFalse();
    }

    [Test]
    public async Task PrivateOriginalGuidanceRequiresManagementAndKeepsItsManagementDetailTarget()
    {
        await using var factory = new NativeEventTagsFactory(relational: true);
        using var client = factory.CreateClient();
        var seed = await SeedAsync(factory, VisibilityTypeEnum.Public, VisibilityTypeEnum.Private);
        using var anonymous = await client.GetAsync(Route(seed.SourceId));
        await Assert.That(anonymous.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(seed.UserId));
        using var authorized = await client.GetAsync(Route(seed.SourceId));
        await Assert.That(authorized.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await authorized.Content.ReadAsStringAsync());
        string? original = document.RootElement.GetProperty("_links")
            .GetProperty("original-event").GetProperty("href").GetString();
        await Assert.That(original).Contains($"{seed.SourceId:D}/management-detail");
    }

    [Test]
    public async Task OwnerGetsFlattenedCandidateHalButNoSelfReviewAuthority()
    {
        await using var factory = new NativeEventTagsFactory(relational: true);
        using var client = factory.CreateClient();
        var seed = await SeedAsync(factory, VisibilityTypeEnum.Public);
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(seed.UserId));
        using var candidates = await client.GetAsync($"{Route(seed.SourceId)}/candidates");
        await Assert.That(candidates.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var list = JsonDocument.Parse(await candidates.Content.ReadAsStringAsync());
        await Assert.That(list.RootElement.GetProperty("eventId").GetGuid()).IsEqualTo(seed.SourceId);
        await Assert.That(list.RootElement.GetProperty("candidates").EnumerateArray().Any(
            item => item.GetProperty("id").GetGuid() == seed.TargetId)).IsTrue();
        await Assert.That(list.RootElement.GetProperty("_links").TryGetProperty("discovery-identity", out _)).IsTrue();

        using var status = await client.GetAsync($"{Route(seed.SourceId)}?candidateEventId={seed.TargetId:D}");
        await Assert.That(status.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var detail = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        await Assert.That(detail.RootElement.GetProperty("_links").TryGetProperty("review", out _)).IsFalse();
        await Assert.That(detail.RootElement.GetProperty("_links").TryGetProperty("reverse", out _)).IsFalse();
        using var denied = await client.PostAsJsonAsync($"{Route(seed.SourceId)}/review",
            new { PrimaryEventId = seed.TargetId, ExpectedRevision = 0, Decision = "same-offering", ReasonCode = "duplicate_listing" });
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    private static string Route(Guid id) => $"/api/event/{id:D}/discovery-identity";

    private static async Task<Seed> SeedAsync(NativeEventTagsFactory factory, VisibilityTypeEnum targetVisibility,
        VisibilityTypeEnum sourceVisibility = VisibilityTypeEnum.Public, bool createRelationship = true)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenant = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context);
        string title = $"identity-surface-{Guid.CreateVersion7():N}";
        var source = AddEvent(sourceVisibility);
        var target = AddEvent(targetVisibility);
        if (createRelationship)
        {
            var member = EventDiscoveryIdentity.Create(tenant.TenantId, EventDiscoverySourceKind.LocalEvent, source.Id.ToString("D"));
            var primary = EventDiscoveryIdentity.Create(tenant.TenantId, EventDiscoverySourceKind.LocalEvent, target.Id.ToString("D"));
            member.Alias = new EventDiscoveryAlias
            {
                Id = Guid.CreateVersion7(), TenantId = tenant.TenantId,
                MemberIdentityId = member.Id, Member = member, PrimaryIdentityId = primary.Id, Primary = primary,
                ReviewerId = tenant.UserId, ReviewedAtUtc = DateTime.UtcNow,
                ReasonCode = "private_primary_reason", RelationshipRevision = 1
            };
            context.Set<EventDiscoveryIdentity>().AddRange(member, primary);
            context.Set<EventDiscoveryRevision>().Add(new EventDiscoveryRevision
            {
                Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, IdentityEpoch = 1
            });
        }
        await context.SaveChangesAsync();
        return new(tenant.UserId, source.Id, target.Id, title);

        Explore.Domain.Event AddEvent(VisibilityTypeEnum visibility)
        {
            var entity = new EventBuilder().WithId(Guid.CreateVersion7()).WithTitle(title)
                .WithActorId(tenant.ActorId).WithTenantId(tenant.TenantId)
                .WithStatus(EventStatusEnum.Published).WithFormat(EventFormatEnum.Digital).Build();
            entity.VisibilityTypeId = (int)visibility;
            var start = new DateTimeOffset(2100, 1, 1, 12, 0, 0, TimeSpan.Zero);
            var session = new EventSession(EventSessionStatusEnum.Published)
            {
                Id = Guid.CreateVersion7(), EventId = entity.Id, Event = entity,
                TenantId = tenant.TenantId, Tenant = null!, StartTime = start, EndTime = start.AddHours(1)
            };
            session.ReprojectLocalTimes("UTC", new EventScheduleProjectionCalculator());
            entity.Sessions.Add(session);
            entity.RecalculateScheduleSummaryFromSessions();
            context.Events.Add(entity);
            context.EventRoleAssignments.Add(EventRoleAssignment.Create(
                tenant.TenantId, entity.Id, tenant.UserId, (int)RoleEnum.EventOwner,
                EventRoleAssignmentStatus.Active, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                null, tenant.UserId));
            return entity;
        }
    }

    private sealed record Seed(Guid UserId, Guid SourceId, Guid TargetId, string Title);
}
