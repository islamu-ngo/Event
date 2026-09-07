using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Authentication;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.TenantOnboarding.Requests.Commands;
using Explore.Application.Features.TenantOnboarding.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
[EndpointClassification(EndpointClass.Authenticated)]
[RequireMultiTenant]
public class TenantOnboardingController : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor CompleteValidationProblem = new(
        "tenantOnboarding",
        "Tenant onboarding validation failed",
        "Tenant onboarding completion failed.");

    private static readonly ApiValidationProblemDescriptor SaveStepValidationProblem = new(
        "tenantOnboarding",
        "Tenant onboarding validation failed",
        "Tenant onboarding step progress save failed.");

    private readonly IMediator _mediator;
    private readonly IResourceAssembler<TenantOnboardingStatusDto, TenantOnboardingStatusDto> _statusAssembler;

    public TenantOnboardingController(
        IMediator mediator,
        IResourceAssembler<TenantOnboardingStatusDto, TenantOnboardingStatusDto> statusAssembler)
    {
        _mediator = mediator;
        _statusAssembler = statusAssembler;
    }

    [HttpGet("status", Name = RouteNames.GetTenantOnboardingStatus)]
    [Authorize]
    [EndpointSummary("Get Tenant Onboarding Status")]
    [EndpointDescription("Returns whether the current tenant onboarding has been completed and whether the current user can complete it.")]
    [Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
    [ProducesResponseType(typeof(HalResource<TenantOnboardingStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalResource<TenantOnboardingStatusDto>>> GetStatus(CancellationToken cancellationToken = default)
    {
        var status = await _mediator.Send(new GetTenantOnboardingStatusQuery(), cancellationToken);
        var resource = await _statusAssembler.ToResource(status, HttpContext);
        return Ok(resource);
    }

    [HttpGet("settings", Name = RouteNames.GetTenantOnboardingPolicySettings)]
    [Authorize]
    [EndpointSummary("Get Tenant Policy Settings")]
    [EndpointDescription("Returns effective tenant policy settings used for tenant onboarding and runtime settings management.")]
    [ProducesResponseType(typeof(TenantPolicySettingsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<TenantPolicySettingsDto>> GetSettings(CancellationToken cancellationToken = default)
    {
        var settings = await _mediator.Send(new GetTenantPolicySettingsQuery(), cancellationToken);
        return Ok(settings);
    }

    [HttpPost("complete", Name = RouteNames.CompleteTenantOnboarding)]
    [Authorize]
    [EndpointSummary("Complete Tenant Onboarding")]
    [EndpointDescription("Completes tenant onboarding and atomically persists tenant policy answers, branding, and directory-operator identity.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Complete(
        [FromBody] CompleteTenantOnboardingRequest request,
        [FromServices] IOutputCacheStore cacheStore,
        CancellationToken cancellationToken = default)
    {
        var currentUserId = await _mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (!currentUserId.HasValue)
        {
            return this.ToAuthenticationRequiredProblem(
                detail: "The authenticated principal could not be resolved to an application user.");
        }

        var response = await _mediator.Send(new CompleteTenantOnboardingCommand
        {
            UserId = currentUserId.Value,
            Settings = request.Settings,
            DirectoryOperatorIdentity = request.DirectoryOperatorIdentity,
            ExpectedDirectoryOperatorIdentityConcurrencyStamp =
                request.ExpectedDirectoryOperatorIdentityConcurrencyStamp
        }, cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.FailureCode == FailureCodes.AdminRequired)
            {
                return this.ToForbiddenProblem(detail: response.Message);
            }

            return this.ToCommandValidationProblem(response, CompleteValidationProblem);
        }

        await cacheStore.EvictByTagAsync("public-experience-shell", cancellationToken);
        return Ok(response);
    }

    [HttpPut("steps", Name = RouteNames.SaveTenantOnboardingStepProgress)]
    [Authorize]
    [EndpointSummary("Save Tenant Onboarding Step Progress")]
    [EndpointDescription("Persists tenant onboarding step progress without completing onboarding.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> SaveStep([FromBody] SaveTenantOnboardingStepDto dto, CancellationToken cancellationToken = default)
    {
        var currentUserId = await _mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (!currentUserId.HasValue)
        {
            return this.ToAuthenticationRequiredProblem(
                detail: "The authenticated principal could not be resolved to an application user.");
        }

        var command = new SaveTenantOnboardingStepCommand
        {
            UserId = currentUserId.Value,
            CurrentStep = dto.CurrentStep,
            TotalSteps = dto.TotalSteps,
            CompletedSteps = dto.CompletedSteps
        };

        var response = await _mediator.Send(command, cancellationToken);
        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, SaveStepValidationProblem);
        }

        return Ok(response);
    }

    public sealed record SaveTenantOnboardingStepDto
    {
        private IReadOnlyList<string> _completedSteps = Array.AsReadOnly(Array.Empty<string>());

        public SaveTenantOnboardingStepDto(
            int CurrentStep,
            int TotalSteps,
            IReadOnlyList<string> CompletedSteps)
        {
            this.CurrentStep = CurrentStep;
            this.TotalSteps = TotalSteps;
            this.CompletedSteps = CompletedSteps;
        }

        public int CurrentStep { get; init; }
        public int TotalSteps { get; init; }

        public IReadOnlyList<string> CompletedSteps
        {
            get => _completedSteps;
            init => _completedSteps = Array.AsReadOnly(value.ToArray());
        }
    }

}
