using Explore.Application.Authentication;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Users.Requests.Queries;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Instance;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

/// <summary>
/// Instance-wide object storage configuration, connectivity checks, and usage recalculation.
/// </summary>
/// <remarks>
/// Split out of InstanceSettingsController by route capability. The route template and every
/// <c>Name = RouteNames.*</c> are carried over verbatim, so URLs, operationIds, and the generated
/// client are unchanged by the split.
/// </remarks>
[ApiVersion("0.1")]
[Route("api/instance/settings")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
public sealed class InstanceStorageSettingsController : InstanceSettingsControllerBase
{
    private readonly IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?> _identityQuery;
    private readonly IResourceAssembler<InstanceStorageSettingsDto, InstanceStorageSettingsDto> _storageSettingsAssembler;
    private readonly IQueryHandler<GetInstanceStorageSettingsQuery, InstanceStorageSettingsDto> _instanceStorageQuery;
    private readonly ICommandHandler<UpdateInstanceStorageSettingsCommand, BaseCommandResponse<Guid>> _updateStorageHandler;
    private readonly IQueryHandler<TestInstanceStorageProviderQuery, InstanceStorageProviderStatusDto> _testStorageHandler;
    private readonly IQueryHandler<TestInstanceStorageProviderDuringSetupQuery, InstanceStorageProviderStatusDto> _testStorageDuringSetupHandler;
    private readonly ICommandHandler<RecalculateInstanceStorageUsageCommand, InstanceStorageUsageDto> _recalculateStorageHandler;
    private readonly ICommandHandler<RecalculateInstanceStorageUsageDuringSetupCommand, InstanceStorageUsageDto> _recalculateStorageDuringSetupHandler;
    private readonly ISetupSecretProvider _accountingSetupSecretProvider;

    public InstanceStorageSettingsController(
        IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?> identityQuery,
        IResourceAssembler<InstanceStorageSettingsDto, InstanceStorageSettingsDto> storageSettingsAssembler,
        IQueryHandler<GetInstanceStorageSettingsQuery, InstanceStorageSettingsDto> instanceStorageQuery,
        ICommandHandler<UpdateInstanceStorageSettingsCommand, BaseCommandResponse<Guid>> updateStorageHandler,
        IQueryHandler<TestInstanceStorageProviderQuery, InstanceStorageProviderStatusDto> testStorageHandler,
        IQueryHandler<TestInstanceStorageProviderDuringSetupQuery, InstanceStorageProviderStatusDto> testStorageDuringSetupHandler,
        ICommandHandler<RecalculateInstanceStorageUsageCommand, InstanceStorageUsageDto> recalculateStorageHandler,
        ICommandHandler<RecalculateInstanceStorageUsageDuringSetupCommand, InstanceStorageUsageDto> recalculateStorageDuringSetupHandler,
        IAdminContext adminContext,
        ISetupSecretProvider setupSecretProvider)
        : base(adminContext, setupSecretProvider)
    {
        _identityQuery = identityQuery;
        _storageSettingsAssembler = storageSettingsAssembler;
        _instanceStorageQuery = instanceStorageQuery;
        _updateStorageHandler = updateStorageHandler;
        _testStorageHandler = testStorageHandler;
        _testStorageDuringSetupHandler = testStorageDuringSetupHandler;
        _recalculateStorageHandler = recalculateStorageHandler;
        _recalculateStorageDuringSetupHandler = recalculateStorageDuringSetupHandler;
        _accountingSetupSecretProvider = setupSecretProvider;
    }

    [HttpGet("storage", Name = RouteNames.GetInstanceStorageSettings)]
    [EndpointSummary("Get Instance Storage Settings")]
    [EndpointDescription("Returns provider policy, quotas, usage, health, and redacted optional S3 settings. Only instance admins can access.")]
    [Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
    [ProducesResponseType(typeof(HalResource<InstanceStorageSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalResource<InstanceStorageSettingsDto>>> GetStorageSettings(CancellationToken cancellationToken = default)
    {
        if (!await IsInstanceAdminOrSetupAuthenticated(cancellationToken)) return this.ToForbiddenProblem(detail: "Instance administrator or active setup secret authority is required for this operation.");
        var settings = await _instanceStorageQuery.QueryAsync(new GetInstanceStorageSettingsQuery(), cancellationToken);
        var halResource = await _storageSettingsAssembler.ToResource(settings, HttpContext);
        return Ok(halResource);
    }

    [HttpPatch("storage", Name = RouteNames.UpdateInstanceStorageSettings)]
    [EndpointSummary("Update Instance Storage Settings")]
    [EndpointDescription("Updates instance storage provider policy, quotas, delegation lock, and optional S3 settings. Requires instance administrator.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateStorageSettings(
        [FromBody] PatchInstanceStorageSettingsDto settings, CancellationToken cancellationToken = default)
    {
        var userId = await _identityQuery.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (!userId.HasValue) return this.ToAuthenticationRequiredProblem(detail: "The authenticated principal could not be resolved to an application user.");

        var response = await _updateStorageHandler.ExecuteAsync(new UpdateInstanceStorageSettingsCommand { UserId = userId.Value, Patch = settings }, cancellationToken);
        return HandleCommandResponse(response);
    }

    [HttpPost("storage/test", Name = RouteNames.TestInstanceStorageConnection)]
    [EndpointSummary("Test Storage Connection")]
    [EndpointDescription("Tests the currently selected storage provider using current instance settings.")]
    [ProducesResponseType(typeof(InstanceStorageProviderStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<InstanceStorageProviderStatusDto>> TestStorageConnection(CancellationToken cancellationToken = default)
    {
        if (!await AdminContext.IsInstanceAdminAsync(cancellationToken))
        {
            if (!await IsInstanceAdminOrSetupAuthenticated(cancellationToken))
                return this.ToForbiddenProblem(detail: "Instance administrator or active setup secret authority is required for this operation.");
            return Ok(await _testStorageDuringSetupHandler.QueryAsync(
                new TestInstanceStorageProviderDuringSetupQuery(), cancellationToken));
        }
        var actor = await _identityQuery.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (!actor.HasValue)
            return this.ToAuthenticationRequiredProblem(detail: "The authenticated principal could not be resolved to an application user.");
        var status = await _testStorageHandler.QueryAsync(new TestInstanceStorageProviderQuery { UserId = actor.Value }, cancellationToken);
        return Ok(status);
    }

    [HttpPost("storage/usage/recalculate", Name = RouteNames.RecalculateInstanceStorageUsage)]
    [EndpointSummary("Recalculate Storage Usage")]
    [EndpointDescription("Reconciles instance-wide storage usage counters from storage metadata. Requires instance administrator.")]
    [ProducesResponseType(typeof(InstanceStorageUsageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<InstanceStorageUsageDto>> RecalculateStorageUsage(CancellationToken cancellationToken = default)
    {
        if (!await AdminContext.IsInstanceAdminAsync(cancellationToken))
        {
            var secret = Request.Headers.TryGetValue("X-Setup-Secret", out var value) ? value.ToString() : null;
            if (await _accountingSetupSecretProvider.ValidateSecretAsync(secret, cancellationToken)
                != SetupSecretValidationOutcome.Accepted)
                return this.ToForbiddenProblem(detail: "Instance administrator or active setup secret authority is required for this operation.");
            var setupUsage = await _recalculateStorageDuringSetupHandler.ExecuteAsync(
                new RecalculateInstanceStorageUsageDuringSetupCommand(), cancellationToken);
            return Ok(setupUsage);
        }

        var userId = await _identityQuery.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (!userId.HasValue)
            return this.ToAuthenticationRequiredProblem(detail: "The authenticated principal could not be resolved to an application user.");
        var usage = await _recalculateStorageHandler.ExecuteAsync(
            new RecalculateInstanceStorageUsageCommand { UserId = userId.Value }, cancellationToken);
        return Ok(usage);
    }
}
