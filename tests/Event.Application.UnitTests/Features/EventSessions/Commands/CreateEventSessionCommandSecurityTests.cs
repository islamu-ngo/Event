using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSession;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventSessions.Requests.Commands;
using Explore.Application.Operations.Decorators;
using Explore.Application.Responses;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Event.Application.UnitTests.Features.EventSessions.Commands;

public class CreateEventSessionCommandSecurityTests
{
    [Test]
    public async Task AuthorizationFacts_ShouldCarryPreCreateTenantAndEventContext()
    {
        var eventId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        ISecureRequest command = new CreateEventSessionCommand
        {
            TenantId = tenantId,
            EventSessionDto = new CreateEventSessionDto
            {
                EventId = eventId
            }
        };

        await Assert.That(command.ResourceId).IsEqualTo(eventId.ToString());
        await Assert.That(command.AuthorizationFacts)
            .IsEqualTo(new PreCreateAuthorizationFacts(tenantId, eventId));
    }

    [Test]
    public async Task ClassCandidateEquivalentOneFactVariantsFailClosedWhileLegitimateTargetPasses()
    {
        var tenantId = Guid.Parse("00000000-0000-0000-0000-000000000101");
        var eventId = Guid.Parse("00000000-0000-0000-0000-000000000102");
        var command = CreateCommand(tenantId, eventId);
        var innerHandler = Substitute.For<ICommandHandler<CreateEventSessionCommand, BaseCommandResponse<Guid>>>();
        innerHandler.ExecuteAsync(Arg.Any<CreateEventSessionCommand>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(BaseCommandResponse.Success(call.Arg<CreateEventSessionCommand>().EventSessionDto.EventId)));
        var authorization = new RequestAuthorization<CreateEventSessionCommand>(
            new ExactPreCreateAuthorizationProvider(tenantId, eventId));
        var decorator = new AuthorizationCommandHandlerDecorator<CreateEventSessionCommand, BaseCommandResponse<Guid>>(
            innerHandler,
            authorization,
            NullLogger<RequestAuthorization<CreateEventSessionCommand>>.Instance);

        BaseCommandResponse<Guid> allowed = await decorator.ExecuteAsync(
            command,
            CancellationToken.None);

        await Assert.That(allowed.IsSuccess).IsTrue();
        await Assert.That(((ISecureRequest)command).ResourceId).IsEqualTo(eventId.ToString("D"));
        await Assert.That(((PreCreateAuthorizationFacts)((ISecureRequest)command).AuthorizationFacts!).TenantId)
            .IsEqualTo(tenantId);
        await Assert.That(eventId).IsNotEqualTo(tenantId);

        CreateEventSessionCommand[] forgedVariants =
        [
            command with { TenantId = Guid.Parse("00000000-0000-0000-0000-000000000103") },
            command with { EventSessionDto = new CreateEventSessionDto { EventId = Guid.Empty } },
            command with
            {
                EventSessionDto = new CreateEventSessionDto
                {
                    EventId = Guid.Parse("00000000-0000-0000-0000-000000000104")
                }
            }
        ];

        foreach (CreateEventSessionCommand forged in forgedVariants)
        {
            await Assert.ThrowsAsync<AuthorizationException>(() => decorator.ExecuteAsync(
                forged,
                CancellationToken.None));
        }

        await innerHandler.Received(1).ExecuteAsync(Arg.Any<CreateEventSessionCommand>(), Arg.Any<CancellationToken>());
    }

    private static CreateEventSessionCommand CreateCommand(Guid tenantId, Guid eventId) => new()
    {
        TenantId = tenantId,
        EventSessionDto = new CreateEventSessionDto { EventId = eventId }
    };

    private sealed class ExactPreCreateAuthorizationProvider(Guid tenantId, Guid eventId) : IAuthorizationProvider
    {
        private readonly PreCreateAuthorizationFacts _expectedFacts = new(tenantId, eventId);

        public Task<AuthorizationDecision> AuthorizeAsync(
            AuthorizationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Decide(request));

        public Task<IReadOnlyList<AuthorizationDecision>> AuthorizeBatchAsync(
            IReadOnlyList<AuthorizationRequest> requests,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuthorizationDecision>>(requests.Select(Decide).ToArray());

        private AuthorizationDecision Decide(AuthorizationRequest request) =>
            request.ResourceKind == ResourceKinds.EventSession
            && request.Action == AuthorizationActions.Create
            && request.ResourceId == eventId.ToString("D")
            && Equals(request.Facts, _expectedFacts)
                ? AuthorizationDecision.Allow(AuthorizationProviderMetadata.Runtime)
                : AuthorizationDecision.Deny(AuthorizationProviderMetadata.Runtime);
    }
}
