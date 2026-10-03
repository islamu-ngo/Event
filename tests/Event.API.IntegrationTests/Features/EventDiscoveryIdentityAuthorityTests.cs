using System.Net;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Scheduling;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed class EventDiscoveryIdentityAuthorityTests
{
    [Test]
    public async Task CandidateEvidenceExcludesPrivateRecordsBeforeReturningManagedMatches()
    {
        await using var factory = new NativeEventTagsFactory(relational: true);
        using var client = factory.CreateClient();
        var seeded = await SeedAsync(factory);
        client.DefaultRequestHeaders.Add(
            TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(seeded.UserId));

        using var response = await client.GetAsync(CandidateRoute(seeded.SourceId));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        string body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).Contains(seeded.PublicId.ToString());
        await Assert.That(body).DoesNotContain(seeded.PrivateId.ToString());
        await Assert.That(body).DoesNotContain("private identity evidence");
    }

    [Test]
    public async Task MembershipRevocationAfterOpeningCandidatesDeniesTheNextEvidenceRead()
    {
        await using var factory = new NativeEventTagsFactory(relational: true);
        using var client = factory.CreateClient();
        var seeded = await SeedAsync(factory);
        client.DefaultRequestHeaders.Add(
            TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(seeded.UserId));
        string route = CandidateRoute(seeded.SourceId);
        using var opened = await client.GetAsync(route);
        await Assert.That(opened.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await opened.Content.ReadAsStringAsync()).Contains(seeded.PublicId.ToString());

        var committed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task<HttpResponseMessage> heldRead = ReadAfterCommitAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var membership = await context.TenantUsers.SingleAsync(
                value => value.UserId == seeded.UserId, deadline.Token);
            membership.StatusId = (int)TenantUserStatusEnum.Removed;
            membership.RemovedAt = DateTime.UtcNow;
            await context.SaveChangesAsync(deadline.Token);
        }
        committed.SetResult();

        using var response = await heldRead;
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        string body = await response.Content.ReadAsStringAsync(deadline.Token);
        await Assert.That(body).DoesNotContain(seeded.PublicId.ToString());
        await Assert.That(body).DoesNotContain(seeded.PrivateId.ToString());

        async Task<HttpResponseMessage> ReadAfterCommitAsync()
        {
            await committed.Task.WaitAsync(deadline.Token);
            return await client.GetAsync(route, deadline.Token);
        }
    }

    private static string CandidateRoute(Guid eventId) =>
        $"/api/event/{eventId:D}/discovery-identity/candidates";

    private static async Task<SeededIdentity> SeedAsync(NativeEventTagsFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenant = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context);
        string title = $"identity-{Guid.CreateVersion7():N}";
        var source = AddEvent(VisibilityTypeEnum.Public);
        var publicMatch = AddEvent(VisibilityTypeEnum.Public);
        var privateMatch = AddEvent(VisibilityTypeEnum.Private);
        privateMatch.Description = "private identity evidence";
        await context.SaveChangesAsync();
        return new SeededIdentity(tenant.UserId, source.Id, publicMatch.Id, privateMatch.Id);

        Explore.Domain.Event AddEvent(VisibilityTypeEnum visibility)
        {
            var entity = new EventBuilder()
                .WithId(Guid.CreateVersion7())
                .WithTitle(title)
                .WithActorId(tenant.ActorId)
                .WithTenantId(tenant.TenantId)
                .WithStatus(EventStatusEnum.Published)
                .WithFormat(EventFormatEnum.Digital)
                .Build();
            entity.VisibilityTypeId = (int)visibility;
            var start = new DateTimeOffset(2100, 1, 1, 12, 0, 0, TimeSpan.Zero);
            var session = new EventSession(EventSessionStatusEnum.Published)
            {
                Id = Guid.CreateVersion7(),
                EventId = entity.Id,
                Event = entity,
                TenantId = tenant.TenantId,
                Tenant = null!,
                StartTime = start,
                EndTime = start.AddHours(1)
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

    private sealed record SeededIdentity(Guid UserId, Guid SourceId, Guid PublicId, Guid PrivateId);
}
