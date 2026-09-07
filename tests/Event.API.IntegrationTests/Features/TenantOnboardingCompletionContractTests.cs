using System.Security.Claims;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.TenantPolicy;
using Explore.Application.DTOs.TenantSettings;
using Explore.Application.Features.TenantOnboarding.Requests.Commands;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using NSubstitute;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

public sealed class TenantOnboardingCompletionContractTests
{
    [Test]
    public async Task Complete_WithIdentityRequest_MapsDedicatedContractToCommand()
    {
        Guid userId = Guid.CreateVersion7();
        Guid expectedStamp = Guid.CreateVersion7();
        var mediator = new CapturingMediator();
        var controller = new TenantOnboardingController(
            mediator,
            Substitute.For<IResourceAssembler<TenantOnboardingStatusDto, TenantOnboardingStatusDto>>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim("internal_user_id", userId.ToString())],
                        "test"))
                }
            }
        };
        var cache = Substitute.For<IOutputCacheStore>();
        var request = new CompleteTenantOnboardingRequest
        {
            Settings = new UpdateTenantPolicyRequest(),
            DirectoryOperatorIdentity = new TenantDirectoryOperatorIdentityInputDto
            {
                PublicName = "HTTP Operator",
                LegalName = "HTTP Operator ASBL",
                OperatorKindCode = "registered_organization",
                JurisdictionCountryCode = "BE",
                PublicContactEmail = "legal@example.test",
                LegalNoticeUrl = "https://example.test/legal",
                PrivacyUrl = "https://example.test/privacy"
            },
            ExpectedDirectoryOperatorIdentityConcurrencyStamp = expectedStamp
        };

        ActionResult<BaseCommandResponse<Guid>> response =
            await controller.Complete(request, cache, CancellationToken.None);

        await Assert.That(response.Result).IsTypeOf<OkObjectResult>();
        await Assert.That(mediator.Command).IsNotNull();
        await Assert.That(mediator.Command!.UserId).IsEqualTo(userId);
        await Assert.That(mediator.Command.DirectoryOperatorIdentity.PublicName).IsEqualTo("HTTP Operator");
        await Assert.That(mediator.Command.ExpectedDirectoryOperatorIdentityConcurrencyStamp)
            .IsEqualTo(expectedStamp);
    }

    private sealed class CapturingMediator : IMediator
    {
        public CompleteTenantOnboardingCommand? Command { get; private set; }
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is CompleteTenantOnboardingCommand command)
            {
                Command = command;
                object result = BaseCommandResponse.Success(Guid.CreateVersion7(), "Completed.");
                return Task.FromResult((TResponse)result);
            }
            throw new InvalidOperationException($"Unexpected request {request.GetType().Name}.");
        }
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => Task.CompletedTask;
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
