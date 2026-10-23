using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventRoleAssignments.Handlers.Commands;
using Explore.Application.Features.EventRoleAssignments.Requests.Commands;
using Explore.Application.Responses;
using Explore.Infrastructure.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
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
        var provider = new DenyingProvider(failure == "unavailable");
        var tenantId = failure == "foreign-tenant" ? Guid.CreateVersion7() : database.TenantId;
        var eventId = Guid.CreateVersion7();
        foreach (string mutation in new[] { "assign", "email", "revoke", "window", "transfer" })
        {
            await using var human = new CurrentHumanPrincipalFixture(database.SubjectId, database.TenantId, borrowedContext: context,
                configureServices: services =>
                {
                    services.AddScoped<IAuthorizationProvider>(_ => provider);
                    services.AddScoped<IPrivacyIdentityFenceAuthority>(_ => database.Privacy.Authority(context, TimeProvider.System));
                });
            await human.InitializeAsync();
            if (failure == "anonymous") human.SignOut();
            await human.PrepareAsync();
            Func<Task<BaseCommandResponse<Guid>>> operation = mutation switch
            {
                "assign" => () => ActivatorUtilities.CreateInstance<AssignEventRoleCommandHandler>(human.Services)
                    .ExecuteAsync(new() { TenantId = tenantId, EventId = eventId }),
                "email" => () => ActivatorUtilities.CreateInstance<AssignEventRoleByEmailCommandHandler>(human.Services)
                    .ExecuteAsync(new() { TenantId = tenantId, EventId = eventId }),
                "revoke" => () => ActivatorUtilities.CreateInstance<RevokeEventRoleAssignmentCommandHandler>(human.Services)
                    .ExecuteAsync(new() { TenantId = tenantId, EventId = eventId }),
                "window" => () => ActivatorUtilities.CreateInstance<UpdateEventRoleAssignmentWindowCommandHandler>(human.Services)
                    .ExecuteAsync(new() { TenantId = tenantId, EventId = eventId }),
                _ => () => ActivatorUtilities.CreateInstance<TransferEventOwnershipCommandHandler>(human.Services)
                    .ExecuteAsync(new() { TenantId = tenantId, EventId = eventId })
            };
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
