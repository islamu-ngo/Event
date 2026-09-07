using System.Reflection;
using Explore.Application.Authorization;
using Explore.Application.Behaviors;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventRoleAssignments.Requests.Commands;
using Explore.Application.Features.EventRoleAssignments.Requests.Queries;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Event.Application.UnitTests.Features.EventRoleAssignments;

public sealed class EventTeamAuthorizationBoundaryTests
{
    [Test]
    public async Task ExternallyCallableEventTeamRequests_DeclareTheCanonicalManageTeamBoundary()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        object[] requests =
        [
            new GetEventTeamListRequest { TenantId = tenantId, EventId = eventId },
            new GetAssignableEventRolePresetsRequest { TenantId = tenantId, EventId = eventId },
            new AssignEventRoleByEmailCommand { TenantId = tenantId, EventId = eventId },
            new AssignEventRoleCommand { TenantId = tenantId, EventId = eventId },
            new RevokeEventRoleAssignmentCommand { TenantId = tenantId, EventId = eventId },
            new UpdateEventRoleAssignmentWindowCommand { TenantId = tenantId, EventId = eventId }
        ];

        foreach (object request in requests)
        {
            var attribute = request.GetType().GetCustomAttribute<AuthorizeResourceAttribute>();

            await Assert.That(attribute?.Resource).IsEqualTo(ResourceKinds.Event);
            await Assert.That(attribute?.Action).IsEqualTo(AuthorizationActions.Events.ManageTeam);
            var secureRequest = (ISecureRequest)request;
            await Assert.That(secureRequest.ResourceId).IsEqualTo(eventId.ToString("D"));
            await Assert.That(secureRequest.AuthorizationFacts)
                .IsEqualTo(new EventScopedAuthorizationFacts(tenantId, eventId));
        }
    }

    [Test]
    public async Task DeniedManageTeamDecision_UsesTrustedEventFactsAndDoesNotRunHandler()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        IEventRepository eventRepository = Substitute.For<IEventRepository>();
        eventRepository.GetEventWithDetails(eventId).Returns(AuthorizationEvent(tenantId, eventId));
        ITenantContext tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);
        IAuthorizationProvider provider = Substitute.For<IAuthorizationProvider>();
        AuthorizationRequest? captured = null;
        provider.AuthorizeAsync(Arg.Any<AuthorizationRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                captured = call.Arg<AuthorizationRequest>();
                return AuthorizationDecision.Deny(AuthorizationProviderMetadata.Runtime);
            });
        var behavior = new AuthorizationBehavior<AssignEventRoleCommand, BaseCommandResponse<Guid>>(
            provider,
            Substitute.For<ILogger<AuthorizationBehavior<AssignEventRoleCommand, BaseCommandResponse<Guid>>>>(),
            new AuthorizationResourceContextResolver(eventRepository, tenantContext: tenantContext));
        var request = new AssignEventRoleCommand { TenantId = tenantId, EventId = eventId };
        var handlerRan = false;

        await Assert.ThrowsAsync<AuthorizationException>(() => behavior.Handle(
            request,
            _ =>
            {
                handlerRan = true;
                return Task.FromResult(BaseCommandResponse.Validation<Guid>(["Handler should not run."]));
            },
            CancellationToken.None));

        await Assert.That(handlerRan).IsFalse();
        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.ResourceKind).IsEqualTo(ResourceKinds.Event);
        await Assert.That(captured.Action).IsEqualTo(AuthorizationActions.Events.ManageTeam);
        await Assert.That(captured.ResourceId).IsEqualTo(eventId.ToString("D"));
        await Assert.That(captured.Facts).IsTypeOf<EventAuthorizationFacts>();
        var facts = (EventAuthorizationFacts)captured.Facts!;
        await Assert.That(facts.TenantId).IsEqualTo(tenantId);
        await Assert.That(facts.EventId).IsEqualTo(eventId);
    }

    private static Explore.Domain.Event AuthorizationEvent(Guid tenantId, Guid eventId)
    {
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            Pii = new ActorPii { DisplayName = "Contributor" },
            ActorTypeId = 1,
            ActorType = null!,
            UserId = Guid.CreateVersion7()
        };
        return new Explore.Domain.Event
        {
            Id = eventId,
            TenantId = tenantId,
            Tenant = null!,
            Title = "Authorization target",
            ActorId = actor.Id,
            Actor = actor,
            EventStatus = null!,
            EventFormat = null!,
            VisibilityType = null!
        };
    }
}
