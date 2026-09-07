using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Features.EventReporting.Requests.Commands;
using Explore.Application.Features.EventReporting.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Application.Settings;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[ApiController]
[Route("api/tenant/settings/reporting-intake")]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public sealed class TenantReportingIntakeSettingsController(
    IMediator mediator,
    ITenantContext tenantContext,
    IResourceAssembler<TenantReportingIntakePolicyDto, TenantReportingIntakePolicyDto> assembler)
    : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor PolicyValidationProblem = new(
        "reportingIntakePolicy",
        "Reporting-intake policy validation failed",
        "The reporting-intake policy update failed.");

    private static readonly CommandFailurePolicy UpdateFailures = CommandFailurePolicy
        .ValidatedBy(PolicyValidationProblem)
        .Forbidden(
            "Reporting-intake policy access denied",
            "The requested tenant does not match the current tenant.",
            "tenant_context_mismatch")
        .Conflict(
            "Reporting-intake policy conflict",
            "The reporting-intake policy cannot be changed in the current policy state.",
            PublicationPolicyMutationFailureCodes.LockedPolicy,
            ReportingIntakePolicyReasonCodes.UnsafePublicationPolicy);

    [HttpGet("", Name = RouteNames.GetTenantReportingIntakePolicy)]
    [PrivateNoStore]
    [RequestTimeout(RequestTimeoutExtensions.LookupPolicy)]
    [EndpointSummary("Get tenant reporting-intake policy")]
    [EndpointDescription("Returns the current tenant's effective reporting-intake value, source, instance lock, and safe-disablement decision.")]
    [ProducesResponseType(typeof(HalResource<TenantReportingIntakePolicyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalResource<TenantReportingIntakePolicyDto>>> Get(
        CancellationToken cancellationToken)
    {
        TenantReportingIntakePolicyDto policy = await mediator.Send(
            new GetTenantReportingIntakePolicyQuery(tenantContext.TenantId),
            cancellationToken);
        var response = new ObjectResult(await assembler.ToResource(policy, HttpContext))
        {
            StatusCode = StatusCodes.Status200OK
        };
        response.ContentTypes.Add(HateoasConstants.HalJsonMediaType);
        return response;
    }

    [HttpPut("", Name = RouteNames.UpdateTenantReportingIntakePolicy)]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    [EndpointSummary("Update tenant reporting-intake policy")]
    [EndpointDescription("Updates the current tenant's reporting-intake state after fresh instance-lock and publication-safety evaluation.")]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Update(
        [FromBody] UpdateTenantReportingIntakePolicyDto policy,
        CancellationToken cancellationToken)
    {
        BaseCommandResponse<Guid> response = await mediator.Send(
            new UpdateTenantReportingIntakePolicyCommand(
                tenantContext.TenantId,
                RequiredUserId,
                policy),
            cancellationToken);
        return response.IsSuccess ? Ok(response) : UpdateFailures.Map(this, response);
    }
}
