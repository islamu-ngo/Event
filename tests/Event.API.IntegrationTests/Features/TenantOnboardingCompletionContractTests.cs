using System.Security.Claims;
using Event.Api.IntegrationTests.Helpers;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.TenantPolicy;
using Explore.Application.DTOs.TenantSettings;
using Explore.Application.Features.TenantOnboarding.Requests.Commands;
using Explore.Application.Features.TenantOnboarding.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
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
        using var identity = new IdentityQueryTestScope();
        Guid userId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000081");
        Guid expectedStamp = Guid.Parse("018e4e5c-7f00-7000-8000-000000000082");
        CompleteTenantOnboardingCommand? capturedCommand = null;
        var completeHandler = Substitute.For<ICommandHandler<CompleteTenantOnboardingCommand, BaseCommandResponse<Guid>>>();
        completeHandler.ExecuteAsync(Arg.Do<CompleteTenantOnboardingCommand>(c => capturedCommand = c), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Success(Guid.Parse("018e4e5c-7f00-7000-8000-000000000083"), "Completed."));
        var controller = new TenantOnboardingController(
            Substitute.For<IQueryHandler<GetTenantOnboardingStatusQuery, TenantOnboardingStatusDto>>(),
            Substitute.For<IQueryHandler<GetTenantPolicySettingsQuery, TenantPolicySettingsDto>>(),
            completeHandler,
            Substitute.For<ICommandHandler<SaveTenantOnboardingStepCommand, BaseCommandResponse<Guid>>>(),
            identity.Query,
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
        await Assert.That(capturedCommand).IsNotNull();
        await Assert.That(capturedCommand!.UserId).IsEqualTo(userId);
        await Assert.That(capturedCommand.DirectoryOperatorIdentity.PublicName).IsEqualTo("HTTP Operator");
        await Assert.That(capturedCommand.ExpectedDirectoryOperatorIdentityConcurrencyStamp)
            .IsEqualTo(expectedStamp);
    }
}
