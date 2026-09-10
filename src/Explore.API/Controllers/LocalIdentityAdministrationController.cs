
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Identity;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Features.Authentication.Local.Requests.Commands;
using Explore.Application.Features.Authentication.Local.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/instance")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
[PrivateNoStore]
[Produces(HateoasConstants.JsonMediaType)]
public sealed class LocalIdentityAdministrationController(
    ISender sender,
    IResourceAssembler<LocalIdentitySummary, LocalIdentitySummary> identityAssembler,
    IResourceAssembler<LocalCredentialOperationStatus, LocalCredentialOperationStatus> operationAssembler,
    IResourceAssembler<LocalCredentialIssueDto, LocalCredentialIssueDto> issueAssembler) : ControllerBase
{
    private static readonly ApiNotFoundProblemDescriptor MissingOperation = new(
        Title: "Local credential operation not found",
        Detail: "The requested Local credential operation is unavailable.");

    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new ApiValidationProblemDescriptor(
            ErrorKey: "request", Title: "Local identity administration failed",
            FallbackDetail: "The requested credential operation could not be accepted."))
        .Forbidden(title: "Instance administrator required",
            detail: "Current instance administration authority is required.",
            failureCodes: [FailureCodes.AdminRequired])
        .NotFound(descriptor: MissingOperation, failureCodes: [FailureCodes.NotFound])
        .Conflict(title: "Local credential operation conflict",
            fallbackDetail: "The credential state or binding changed.",
            failureCodes: [FailureCodes.ConcurrencyConflict]);

    [HttpGet("local-identities", Name = RouteNames.ListLocalIdentities)]
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [ProducesResponseType(typeof(HalCollectionResource<LocalIdentitySummary>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [EndpointSummary("List Local identities for instance administration")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult> List(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        LocalIdentityPage page = await sender.Send(
            new ListLocalIdentitiesQuery(pageNumber: pageNumber, pageSize: pageSize), cancellationToken);
        var paginated = new PaginatedResult<LocalIdentitySummary>(
            items: page.Items.ToList(), totalCount: page.TotalCount,
            pageNumber: page.PageNumber, pageSize: page.PageSize);
        return Ok(await identityAssembler.ToCollectionResource(
            paginatedResult: paginated, routeName: RouteNames.ListLocalIdentities,
            additionalRouteValues: null, httpContext: HttpContext));
    }

    [HttpPost("local-identities", Name = RouteNames.CreateLocalIdentity)]
    [SuppressIdempotencyResponseStorage]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(HalResource<LocalCredentialIssueDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(HalResource<LocalCredentialIssueDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [EndpointSummary("Create a Local identity with one-time temporary credential disclosure")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult> Create(
        [FromBody] CreateLocalIdentityRequestDto body,
        CancellationToken cancellationToken = default)
    {
        LocalCredentialIssueCommandResponse response = await sender.Send(
            new CreateLocalIdentityCommand(operationId: body.OperationId, email: body.Email,
                firstName: body.FirstName, lastName: body.LastName), cancellationToken);
        if (!response.IsSuccess)
        {
            return Failures.Map(this, response);
        }
        if (response.Issue is not { } issue)
        {
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Local credential operation unavailable");
        }
        HalResource<LocalCredentialIssueDto> resource = await issueAssembler.ToResource(
            dto: issue, httpContext: HttpContext);
        return issue.Outcome == LocalCredentialIssueOutcome.Issued
            ? CreatedAtRoute(routeName: RouteNames.GetLocalCredentialOperation,
                routeValues: new { operationId = issue.Operation.Receipt.OperationId }, value: resource)
            : Ok(resource);
    }

    [HttpPost("local-identities/{userId:guid}/temporary-credential", Name = RouteNames.ResetLocalCredential)]
    [SuppressIdempotencyResponseStorage]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(HalResource<LocalCredentialIssueDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [EndpointSummary("Issue a replacement temporary credential for a Local identity")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult> Reset(
        Guid userId, [FromBody] ResetLocalCredentialRequestDto body,
        CancellationToken cancellationToken = default)
    {
        LocalCredentialIssueCommandResponse response = await sender.Send(
            new ResetLocalCredentialCommand(operationId: body.OperationId, localSubjectId: userId,
                expectedCurrentOperationId: body.ExpectedCurrentOperationId,
                expectedCurrentOperationConcurrencyStamp: body.ExpectedCurrentOperationConcurrencyStamp,
                reason: body.Reason), cancellationToken);
        if (!response.IsSuccess)
        {
            return Failures.Map(this, response);
        }
        if (response.Issue is not { } issue)
        {
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Local credential operation unavailable");
        }
        return Ok(await issueAssembler.ToResource(dto: issue, httpContext: HttpContext));
    }

    [HttpGet("local-identity-operations/{operationId:guid}", Name = RouteNames.GetLocalCredentialOperation)]
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [ProducesResponseType(typeof(HalResource<LocalCredentialOperationStatus>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [EndpointSummary("Read safe Local credential operation status")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult> GetOperation(Guid operationId, CancellationToken cancellationToken = default)
    {
        LocalCredentialOperationStatus? operation = await sender.Send(
            new GetLocalCredentialOperationQuery(operationId: operationId), cancellationToken);
        return operation is null
            ? this.ToNotFoundProblem(MissingOperation)
            : Ok(await operationAssembler.ToResource(dto: operation, httpContext: HttpContext));
    }

    [HttpPost("local-identity-operations/{operationId:guid}/reconcile", Name = RouteNames.ReconcileLocalCredentialOperation)]
    [SuppressIdempotencyResponseStorage]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(HalResource<LocalCredentialOperationStatus>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [EndpointSummary("Reconcile a pending Local credential operation without issuing a credential")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult> Reconcile(Guid operationId, CancellationToken cancellationToken = default)
    {
        BaseCommandResponse<Guid> response = await sender.Send(
            new ReconcileLocalCredentialOperationCommand(operationId: operationId), cancellationToken);
        if (!response.IsSuccess)
        {
            return Failures.Map(this, response);
        }
        LocalCredentialOperationStatus? operation = await sender.Send(
            new GetLocalCredentialOperationQuery(operationId: operationId), cancellationToken);
        return operation is null
            ? this.ToNotFoundProblem(MissingOperation)
            : Ok(await operationAssembler.ToResource(dto: operation, httpContext: HttpContext));
    }
}
