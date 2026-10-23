using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventRoleAssignments.Handlers.Commands;
using Explore.Application.Features.EventRoleAssignments.Requests.Commands;
using Explore.Application.Responses;
using Explore.Infrastructure.Identity;
using Explore.Persistence.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed class EventTeamMutationIdentityTests
{
    [Test]
    [Arguments("anonymous")]
    [Arguments("denied")]
    [Arguments("unavailable")]
    [Arguments("foreign-tenant")]
    public async Task EveryMutationQualifiesTheAmbientHumanBeforeReadingTargetsOrWritingAudits(string failure)
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var context = database.Context();
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, database.SubjectId.ToString("D"))],
                failure == "anonymous" ? null : "event-team-native-witness")) } };
        var identity = new AdminContext(http, new PlatformUserRoleRepository(context), new TenantUserRoleGrantRepository(context),
            new OrganizationMemberRepository(context), new GroupMemberRepository(context), new UserExternalLoginRepository(context),
            NullLogger<AdminContext>.Instance);
        var machine = new MachinePrincipalAccessor(http);
        var provider = new DenyingProvider(failure == "unavailable");
        var tenantId = failure == "foreign-tenant" ? Guid.CreateVersion7() : database.TenantId;
        var eventId = Guid.CreateVersion7();
        var assignment = new AssignEventRoleCommandHandler(null!, null!, null!, null!, null!, null!, null!,
            identity, machine, database, provider, null!);
        var email = new AssignEventRoleByEmailCommandHandler(null!, assignment, identity, machine, database, provider);
        var revoke = new RevokeEventRoleAssignmentCommandHandler(null!, null!, null!, null!, null!, null!,
            identity, machine, database, provider, null!);
        var window = new UpdateEventRoleAssignmentWindowCommandHandler(null!, null!, null!, null!, null!, null!,
            identity, machine, database, provider, null!);
        var transfer = new TransferEventOwnershipCommandHandler(null!, null!, null!, null!, null!, null!, null!,
            identity, machine, database, provider, null!);
        Func<Task<BaseCommandResponse<Guid>>>[] operations =
        [
            () => assignment.ExecuteAsync(new() { TenantId = tenantId, EventId = eventId }),
            () => email.ExecuteAsync(new() { TenantId = tenantId, EventId = eventId }),
            () => revoke.ExecuteAsync(new() { TenantId = tenantId, EventId = eventId }),
            () => window.ExecuteAsync(new() { TenantId = tenantId, EventId = eventId }),
            () => transfer.ExecuteAsync(new() { TenantId = tenantId, EventId = eventId })
        ];
        foreach (var operation in operations)
        {
            if (failure == "unavailable")
                await Assert.ThrowsAsync<AuthorizationProviderUnavailableException>(async () => { await operation(); });
            else
                await Assert.ThrowsAsync<AuthorizationException>(async () => { await operation(); });
        }
        if (failure is "anonymous" or "foreign-tenant")
            await Assert.That(provider.Requests).IsEmpty();
        else
        {
            await Assert.That(provider.Requests.Select(request => request.Subject!.UserId).Distinct().Single())
                .IsEqualTo(database.SubjectId);
            await Assert.That(provider.Requests.All(request => request.ResourceId == eventId.ToString("D") &&
                request.Facts is EventScopedAuthorizationFacts facts && facts.TenantId == database.TenantId &&
                request.Tenant!.TenantId == database.TenantId)).IsTrue();
            await Assert.That(provider.Requests.Select(request => request.Action)).IsEquivalentTo(new[]
            {
                AuthorizationActions.Events.ManageTeam, AuthorizationActions.Events.ManageTeam,
                AuthorizationActions.Events.ManageTeam, AuthorizationActions.Events.ManageTeam,
                AuthorizationActions.Events.TransferOwnership
            });
        }
        await Assert.That(context.ChangeTracker.HasChanges()).IsFalse();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    private sealed class DenyingProvider(bool unavailable) : IAuthorizationProvider
    {
        public List<AuthorizationRequest> Requests { get; } = [];
        public Task<AuthorizationDecision> AuthorizeAsync(AuthorizationRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(AuthorizationDecision.Deny(AuthorizationProviderMetadata.Local,
                unavailable ? AuthorizationDecisionReasonCodes.ProviderUnavailable : AuthorizationDecisionReasonCodes.Denied));
        }
        public async Task<IReadOnlyList<AuthorizationDecision>> AuthorizeBatchAsync(
            IReadOnlyList<AuthorizationRequest> requests, CancellationToken cancellationToken = default)
        {
            var results = new List<AuthorizationDecision>();
            foreach (var request in requests) results.Add(await AuthorizeAsync(request, cancellationToken));
            return results;
        }
    }
}
