using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Footer;
using Explore.Application.Features.Footer.Requests.Commands;
using Explore.Application.Features.Footer.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public class FooterController : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor LinkGroupValidationProblem = new(
        "footerLinkGroup",
        "Footer link group validation failed",
        "Footer link group operation failed.");

    private static readonly ApiValidationProblemDescriptor LinkGroupReorderValidationProblem = new(
        "footerLinkGroup",
        "Footer link group validation failed",
        "Footer link group reorder failed.");

    private static readonly ApiValidationProblemDescriptor LinkValidationProblem = new(
        "footerLink",
        "Footer link validation failed",
        "Footer link operation failed.");

    private static readonly ApiValidationProblemDescriptor SettingsValidationProblem = new(
        "tenantFooterSettings",
        "Tenant footer settings validation failed",
        "Tenant footer settings patch failed.");

    private readonly IQueryHandler<GetFooterConfigQuery, FooterConfigDto> _configQuery;
    private readonly IQueryHandler<GetFooterLinkGroupListQuery, List<FooterLinkGroupListDto>> _groupsQuery;
    private readonly IQueryHandler<GetFooterLinkGroupDetailsQuery, FooterLinkGroupDetailsDto> _groupQuery;
    private readonly IQueryHandler<GetTenantFooterSettingsQuery, TenantFooterSettingsDto> _settingsQuery;
    private readonly ICommandHandler<CreateFooterLinkGroupCommand, BaseCommandResponse<Guid>> _createGroup;
    private readonly ICommandHandler<UpdateFooterLinkGroupCommand, BaseCommandResponse<Guid>> _updateGroup;
    private readonly ICommandHandler<DeleteFooterLinkGroupCommand, bool> _deleteGroup;
    private readonly ICommandHandler<ReorderFooterLinkGroupsCommand, BaseCommandResponse<Guid>> _reorderGroups;
    private readonly ICommandHandler<CreateFooterLinkCommand, BaseCommandResponse<Guid>> _createLink;
    private readonly ICommandHandler<UpdateFooterLinkCommand, BaseCommandResponse<Guid>> _updateLink;
    private readonly ICommandHandler<DeleteFooterLinkCommand, bool> _deleteLink;
    private readonly ICommandHandler<PatchTenantFooterSettingsCommand, BaseCommandResponse<Guid>> _patchSettings;
    private readonly ITenantContext _tenantContext;
    private readonly IResourceAssembler<TenantFooterSettingsDto, TenantFooterSettingsDto> _settingsResourceAssembler;

    public FooterController(
        IQueryHandler<GetFooterConfigQuery, FooterConfigDto> configQuery,
        IQueryHandler<GetFooterLinkGroupListQuery, List<FooterLinkGroupListDto>> groupsQuery,
        IQueryHandler<GetFooterLinkGroupDetailsQuery, FooterLinkGroupDetailsDto> groupQuery,
        IQueryHandler<GetTenantFooterSettingsQuery, TenantFooterSettingsDto> settingsQuery,
        ICommandHandler<CreateFooterLinkGroupCommand, BaseCommandResponse<Guid>> createGroup,
        ICommandHandler<UpdateFooterLinkGroupCommand, BaseCommandResponse<Guid>> updateGroup,
        ICommandHandler<DeleteFooterLinkGroupCommand, bool> deleteGroup,
        ICommandHandler<ReorderFooterLinkGroupsCommand, BaseCommandResponse<Guid>> reorderGroups,
        ICommandHandler<CreateFooterLinkCommand, BaseCommandResponse<Guid>> createLink,
        ICommandHandler<UpdateFooterLinkCommand, BaseCommandResponse<Guid>> updateLink,
        ICommandHandler<DeleteFooterLinkCommand, bool> deleteLink,
        ICommandHandler<PatchTenantFooterSettingsCommand, BaseCommandResponse<Guid>> patchSettings,
        ITenantContext tenantContext,
        IResourceAssembler<TenantFooterSettingsDto, TenantFooterSettingsDto> settingsResourceAssembler)
    {
        _configQuery = configQuery;
        _groupsQuery = groupsQuery;
        _groupQuery = groupQuery;
        _settingsQuery = settingsQuery;
        _createGroup = createGroup;
        _updateGroup = updateGroup;
        _deleteGroup = deleteGroup;
        _reorderGroups = reorderGroups;
        _createLink = createLink;
        _updateLink = updateLink;
        _deleteLink = deleteLink;
        _patchSettings = patchSettings;
        _tenantContext = tenantContext;
        _settingsResourceAssembler = settingsResourceAssembler;
    }

    // ── Public / tenant-read endpoints ──────────────────────────────────────

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("config", Name = RouteNames.GetFooterConfig)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<FooterConfigDto>> GetConfig(CancellationToken cancellationToken)
    {
        var result = await _configQuery.QueryAsync(new GetFooterConfigQuery(), cancellationToken);
        return Ok(result);
    }

    // ── Link group admin endpoints ───────────────────────────────────────────

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpGet("link-groups", Name = RouteNames.GetFooterLinkGroups)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<FooterLinkGroupListDto>>> GetLinkGroups(CancellationToken cancellationToken)
    {
        var result = await _groupsQuery.QueryAsync(new GetFooterLinkGroupListQuery(), cancellationToken);
        return Ok(result);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpGet("link-groups/{id:guid}", Name = RouteNames.GetFooterLinkGroupById)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FooterLinkGroupDetailsDto>> GetLinkGroupById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _groupQuery.QueryAsync(new GetFooterLinkGroupDetailsQuery(id), cancellationToken);
        return Ok(result);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost("link-groups", Name = RouteNames.CreateFooterLinkGroup)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> CreateLinkGroup(
        [FromBody] CreateFooterLinkGroupRequest request, CancellationToken cancellationToken)
    {
        if (TryGetCurrentUserId(out var userId) is { } unauthorized)
        {
            return unauthorized;
        }
        var result = await _createGroup.ExecuteAsync(new CreateFooterLinkGroupCommand
        {
            UserId = userId,
            TenantId = _tenantContext.TenantId,
            Title = request.Title,
        }, cancellationToken);

        if (!result.IsSuccess)
            return this.ToCommandValidationProblem(result, LinkGroupValidationProblem);

        return CreatedAtAction(nameof(GetLinkGroupById), new { id = result.Id }, result);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPatch("link-groups/{id:guid}", Name = RouteNames.UpdateFooterLinkGroup)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateLinkGroup(
        Guid id, [FromBody] PatchFooterLinkGroupDto request, CancellationToken cancellationToken)
    {
        var result = await _updateGroup.ExecuteAsync(new UpdateFooterLinkGroupCommand
        {
            TenantId = _tenantContext.TenantId,
            GroupId = id,
            Update = request
        }, cancellationToken);

        if (!result.IsSuccess)
            return this.ToCommandValidationProblem(result, LinkGroupValidationProblem);

        return Ok(result);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpDelete("link-groups/{id:guid}", Name = RouteNames.DeleteFooterLinkGroup)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<bool>> DeleteLinkGroup(Guid id, CancellationToken cancellationToken)
    {
        if (TryGetCurrentUserId(out var userId) is { } unauthorized)
        {
            return unauthorized;
        }
        var result = await _deleteGroup.ExecuteAsync(new DeleteFooterLinkGroupCommand
        {
            UserId = userId,
            TenantId = _tenantContext.TenantId,
            GroupId = id
        }, cancellationToken);
        return Ok(result);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost("link-groups/reorder", Name = RouteNames.ReorderFooterLinkGroups)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> ReorderLinkGroups(
        [FromBody] List<Guid> orderedGroupIds, CancellationToken cancellationToken)
    {
        if (TryGetCurrentUserId(out var userId) is { } unauthorized)
        {
            return unauthorized;
        }
        var result = await _reorderGroups.ExecuteAsync(new ReorderFooterLinkGroupsCommand
        {
            UserId = userId,
            TenantId = _tenantContext.TenantId,
            OrderedGroupIds = orderedGroupIds,
        }, cancellationToken);

        if (!result.IsSuccess)
            return this.ToCommandValidationProblem(result, LinkGroupReorderValidationProblem);

        return Ok(result);
    }

    // ── Link admin endpoints ─────────────────────────────────────────────────

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost("link-groups/{groupId:guid}/links", Name = RouteNames.CreateFooterLink)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> CreateLink(
        Guid groupId, [FromBody] CreateFooterLinkRequest request, CancellationToken cancellationToken)
    {
        if (TryGetCurrentUserId(out var userId) is { } unauthorized)
        {
            return unauthorized;
        }
        var result = await _createLink.ExecuteAsync(new CreateFooterLinkCommand
        {
            UserId = userId,
            TenantId = _tenantContext.TenantId,
            GroupId = groupId,
            Label = request.Label,
            Url = request.Url,
            OpenInNewTab = request.OpenInNewTab,
        }, cancellationToken);

        if (!result.IsSuccess)
            return this.ToCommandValidationProblem(result, LinkValidationProblem);

        return Created(string.Empty, result);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPatch("links/{id:guid}", Name = RouteNames.UpdateFooterLink)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateLink(
        Guid id, [FromBody] PatchFooterLinkDto request, CancellationToken cancellationToken)
    {
        var result = await _updateLink.ExecuteAsync(new UpdateFooterLinkCommand
        {
            TenantId = _tenantContext.TenantId,
            LinkId = id,
            Update = request
        }, cancellationToken);

        if (!result.IsSuccess)
            return this.ToCommandValidationProblem(result, LinkValidationProblem);

        return Ok(result);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpDelete("links/{id:guid}", Name = RouteNames.DeleteFooterLink)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<bool>> DeleteLink(Guid id, CancellationToken cancellationToken)
    {
        if (TryGetCurrentUserId(out var userId) is { } unauthorized)
        {
            return unauthorized;
        }
        var result = await _deleteLink.ExecuteAsync(new DeleteFooterLinkCommand
        {
            UserId = userId,
            TenantId = _tenantContext.TenantId,
            LinkId = id
        }, cancellationToken);
        return Ok(result);
    }

    // ── Tenant settings endpoints ────────────────────────────────────────────

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpGet("settings", Name = RouteNames.GetTenantFooterSettings)]
    [EndpointSummary("Get Tenant Footer Settings")]
    [EndpointDescription("Returns the current tenant footer scalar settings and governance lock states without link groups or links.")]
    [ProducesResponseType(typeof(HalResource<TenantFooterSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<HalResource<TenantFooterSettingsDto>>> GetSettings(
        CancellationToken cancellationToken)
    {
        var settings = await _settingsQuery.QueryAsync(new GetTenantFooterSettingsQuery(), cancellationToken);
        var resource = await _settingsResourceAssembler.ToResource(settings, HttpContext);
        return Ok(resource);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPatch("settings", Name = RouteNames.PatchTenantFooterSettings)]
    [EndpointSummary("Patch Tenant Footer Settings")]
    [EndpointDescription("Patches supplied tenant footer setting leaves while preserving omitted or instance-locked values.")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> PatchSettings(
        [FromBody] PatchTenantFooterSettingsDto patch,
        CancellationToken cancellationToken)
    {
        if (TryGetCurrentUserId(out var userId) is { } unauthorized)
        {
            return unauthorized;
        }

        var result = await _patchSettings.ExecuteAsync(new PatchTenantFooterSettingsCommand
        {
            UserId = userId,
            TenantId = _tenantContext.TenantId,
            Patch = patch
        }, cancellationToken);

        if (!result.IsSuccess)
            return this.ToCommandValidationProblem(result, SettingsValidationProblem);

        return Ok(result);
    }


    private ActionResult? TryGetCurrentUserId(out Guid userId)
    {
        if (CurrentUserId is { } currentUserId)
        {
            userId = currentUserId;
            return null;
        }

        userId = Guid.Empty;
        return this.ToAuthenticationRequiredProblem();
    }

    // ── Request body types ───────────────────────────────────────────────────

    public sealed record CreateFooterLinkGroupRequest(string Title);
    public sealed record CreateFooterLinkRequest(string Label, string Url, bool OpenInNewTab);
}
