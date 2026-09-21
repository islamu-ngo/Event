namespace Explore.API.Controllers;

using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Commands;
using Explore.Application.Features.InstanceOnboarding.Queries;
using Explore.Application.Onboarding;
using Explore.Application.Responses;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Hateoas;
using Explore.Application.Contracts.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

/// <summary>
/// Dedicated management controller for the persisted instance operator identity document.
/// Accessible during pending first-run setup via active setup secret or by an authenticated
/// platform administrator.
/// </summary>
[ApiVersion("0.1")]
[Route("api/instance-operator-identity")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Admin)]
public sealed class InstanceOperatorIdentityController : EventControllerBase
{
    private const string SetupSecretHeader = "X-Setup-Secret";

    private static readonly ApiValidationProblemDescriptor OperatorIdentityValidationProblem = new(
        "instanceOperatorIdentity",
        "Instance operator identity validation failed",
        "Instance operator identity update failed.");

    private readonly IQueryHandler<GetInstanceOperatorIdentityQuery, InstanceOperatorIdentityDocumentDto> _identityQuery;
    private readonly ICommandHandler<SaveInstanceOperatorIdentityCommand, BaseCommandResponse<InstanceOperatorIdentitySavedDocumentDto>> _saveIdentity;
    private readonly ISetupSecretProvider _setupSecretProvider;
    private readonly IAdminContext _adminContext;
    private readonly IResourceAssembler<InstanceOperatorIdentityDocumentDto, InstanceOperatorIdentityDocumentDto> _assembler;

    public InstanceOperatorIdentityController(
        IQueryHandler<GetInstanceOperatorIdentityQuery, InstanceOperatorIdentityDocumentDto> identityQuery,
        ICommandHandler<SaveInstanceOperatorIdentityCommand, BaseCommandResponse<InstanceOperatorIdentitySavedDocumentDto>> saveIdentity,
        ISetupSecretProvider setupSecretProvider,
        IAdminContext adminContext,
        IResourceAssembler<InstanceOperatorIdentityDocumentDto, InstanceOperatorIdentityDocumentDto> assembler)
    {
        _identityQuery = identityQuery;
        _saveIdentity = saveIdentity;
        _setupSecretProvider = setupSecretProvider;
        _adminContext = adminContext;
        _assembler = assembler;
    }

    /// <summary>
    /// Retrieves current instance operator identity document and readiness assessment.
    /// </summary>
    [InstanceManagement]
    [HttpGet(Name = RouteNames.GetInstanceOperatorIdentity)]
    [PrivateNoStore]
    [EndpointSummary("Get Instance Operator Identity")]
    [EndpointDescription("Returns the current instance operator identity with separate public-disclosure and paid-commerce readiness assessments.")]
    [Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
    [ProducesResponseType(typeof(HalResource<InstanceOperatorIdentityDocumentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status410Gone)]
    public async Task<ActionResult<HalResource<InstanceOperatorIdentityDocumentDto>>> Get(
        CancellationToken cancellationToken = default)
    {
        var authResult = await EvaluateAuthorizationAsync(cancellationToken);
        if (authResult != OperatorIdentityAuthResult.Authorized)
        {
            return HandleAuthResult(authResult)!;
        }

        var dto = await _identityQuery.QueryAsync(new GetInstanceOperatorIdentityQuery(), cancellationToken);
        var resource = await _assembler.ToResource(dto, HttpContext);
        return Ok(resource);
    }

    /// <summary>
    /// Saves candidate instance operator identity settings.
    /// </summary>
    [InstanceManagement]
    [HttpPut(Name = RouteNames.SaveInstanceOperatorIdentity)]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [EndpointSummary("Update Instance Operator Identity")]
    [EndpointDescription("Saves a syntactically valid instance operator identity draft and returns separate public-disclosure and paid-commerce readiness assessments.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<InstanceOperatorIdentitySavedDocumentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status410Gone)]
    public async Task<ActionResult<BaseCommandResponse<InstanceOperatorIdentitySavedDocumentDto>>> Put(
        [FromBody] SaveInstanceOperatorIdentityRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var authResult = await EvaluateAuthorizationAsync(cancellationToken);
        if (authResult != OperatorIdentityAuthResult.Authorized)
        {
            return HandleAuthResult(authResult)!;
        }

        var command = new SaveInstanceOperatorIdentityCommand { Request = request };
        var response = await _saveIdentity.ExecuteAsync(command, cancellationToken);

        if (response.IsSuccess)
        {
            return Ok(response);
        }

        return this.ToCommandValidationProblem(response, OperatorIdentityValidationProblem);
    }

    private enum OperatorIdentityAuthResult
    {
        Authorized,
        Unauthorized,
        Forbidden,
        Gone
    }

    private async Task<OperatorIdentityAuthResult> EvaluateAuthorizationAsync(CancellationToken cancellationToken)
    {
        var secret = Request.Headers[SetupSecretHeader].FirstOrDefault();
        bool hasSetupSecretHeader = !string.IsNullOrWhiteSpace(secret);
        bool hasSetupSecretAuth = User.Identities.Any(i =>
            i.IsAuthenticated && i.AuthenticationType == ApiAuthenticationSchemeNames.SetupSecret);

        if (hasSetupSecretHeader || hasSetupSecretAuth)
        {
            SetupSecretValidationOutcome validation = await _setupSecretProvider.ValidateSecretAsync(
                secret,
                cancellationToken);

            if (validation == SetupSecretValidationOutcome.SetupCompleted)
            {
                return OperatorIdentityAuthResult.Gone;
            }

            if (validation == SetupSecretValidationOutcome.Accepted)
            {
                return OperatorIdentityAuthResult.Authorized;
            }

            return OperatorIdentityAuthResult.Forbidden;
        }

        if (User?.Identity?.IsAuthenticated != true)
        {
            return OperatorIdentityAuthResult.Unauthorized;
        }

        if (await _adminContext.IsInstanceAdminAsync(cancellationToken))
        {
            return OperatorIdentityAuthResult.Authorized;
        }

        return OperatorIdentityAuthResult.Forbidden;
    }

    private ActionResult? HandleAuthResult(OperatorIdentityAuthResult result) => result switch
    {
        OperatorIdentityAuthResult.Gone => ApiProblemFactory.ToProblemResult(
            ApiProblemFactory.CreateGoneProblem(
                HttpContext,
                "Setup already completed",
                "Setup mode is no longer active for this instance.",
                ApiProblemCodes.SetupAlreadyCompleted)),
        OperatorIdentityAuthResult.Unauthorized => this.ToAuthenticationRequiredProblem(
            detail: "Authentication or a valid setup secret is required."),
        OperatorIdentityAuthResult.Forbidden => this.ToForbiddenProblem(
            detail: "Instance administrator or active setup secret authority is required for this operation."),
        _ => null
    };
}
