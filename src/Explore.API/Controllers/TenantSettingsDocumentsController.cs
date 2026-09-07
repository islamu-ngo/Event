using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.TenantSettingsDocuments;
using Explore.Application.Features.TenantSettingsDocuments.Requests.Commands;
using Explore.Application.Features.TenantSettingsDocuments.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/tenant/settings/documents")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
public sealed class TenantSettingsDocumentsController(
    IMediator mediator,
    ITenantContext tenantContext,
    ITenantBrandingSettingsDocumentLockService lockService,
    IResourceAssembler<TenantBrandingSettingsDocumentDto, TenantBrandingSettingsDocumentDto>
        brandingResourceAssembler,
    IResourceAssembler<
        TenantDirectoryOperatorIdentityDocumentDto,
        TenantDirectoryOperatorIdentityDocumentDto> directoryIdentityResourceAssembler)
    : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor PatchBrandingValidationProblem = new(
        "tenantBrandingSettingsDocument",
        "Tenant branding settings document validation failed",
        "Tenant branding settings document patch failed.");

    private static readonly ApiNotFoundProblemDescriptor BrandingDocumentNotFoundProblem = new(
        "Tenant branding settings document not found",
        "Tenant branding settings document not found.");

    private static readonly ApiValidationProblemDescriptor PatchDirectoryIdentityValidationProblem = new(
        "tenantDirectoryOperatorIdentityDocument",
        "Tenant directory operator identity validation failed",
        "Tenant directory operator identity patch failed.");

    private static readonly ApiNotFoundProblemDescriptor DirectoryIdentityNotFoundProblem = new(
        "Tenant directory operator identity not found",
        "Tenant directory operator identity not found.");

    [HttpGet("branding", Name = RouteNames.GetTenantBrandingSettingsDocument)]
    [EndpointSummary("Get Tenant Branding Settings Document")]
    [EndpointDescription("Returns the current tenant branding typed settings document, provisioning the default typed row when it is missing.")]
    [ProducesResponseType(typeof(HalResource<TenantBrandingSettingsDocumentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<TenantBrandingSettingsDocumentDto>>> GetBranding(
        CancellationToken cancellationToken = default)
    {
        var document = await mediator.Send(new GetTenantBrandingSettingsDocumentQuery(), cancellationToken);

        if (document is null)
        {
            return this.ToNotFoundProblem(BrandingDocumentNotFoundProblem);
        }

        var resource = await brandingResourceAssembler.ToResource(document, HttpContext);
        return Ok(resource);
    }

    [HttpPatch("branding", Name = RouteNames.PatchTenantBrandingSettingsDocument)]
    [EndpointSummary("Patch Tenant Branding Settings Document")]
    [EndpointDescription("Patches supplied tenant branding groups with optimistic concurrency while preserving omitted JSONB settings leaves.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(HalResource<TenantBrandingSettingsDocumentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<TenantBrandingSettingsDocumentDto>>> PatchBranding(
        [FromBody] PatchTenantBrandingSettingsDocumentDto patch,
        [FromServices] IOutputCacheStore cacheStore,
        CancellationToken cancellationToken = default)
    {
        var lockState = await lockService.GetLockStateAsync(cancellationToken);
        var response = await mediator.Send(
            new PatchTenantBrandingSettingsDocumentCommand
            {
                TenantId = tenantContext.TenantId,
                Patch = patch,
                IsLockedByInstance = lockState.IsLockedByInstance
            },
            cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.FailureCode == FailureCodes.NotFound)
            {
                return this.ToNotFoundProblem(BrandingDocumentNotFoundProblem);
            }

            return this.ToCommandValidationProblem(response, PatchBrandingValidationProblem);
        }

        var updated = await mediator.Send(new GetTenantBrandingSettingsDocumentQuery(), cancellationToken);
        if (updated is null)
        {
            return this.ToNotFoundProblem(BrandingDocumentNotFoundProblem);
        }

        await cacheStore.EvictByTagAsync("public-experience-shell", cancellationToken);

        var resource = await brandingResourceAssembler.ToResource(updated, HttpContext);
        return Ok(resource);
    }

    [HttpGet(
        "directory-operator-identity",
        Name = RouteNames.GetTenantDirectoryOperatorIdentityDocument)]
    [EndpointSummary("Get Tenant Directory Operator Identity")]
    [EndpointDescription(
        "Returns the current tenant-owned directory operator identity without provisioning or fallback.")]
    [ProducesResponseType(
        typeof(HalResource<TenantDirectoryOperatorIdentityDocumentDto>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<TenantDirectoryOperatorIdentityDocumentDto>>>
        GetDirectoryOperatorIdentity(CancellationToken cancellationToken = default)
    {
        TenantDirectoryOperatorIdentityDocumentDto? document = await mediator.Send(
            new GetTenantDirectoryOperatorIdentityDocumentQuery(tenantContext.TenantId),
            cancellationToken);
        if (document is null)
        {
            return this.ToNotFoundProblem(DirectoryIdentityNotFoundProblem);
        }

        HalResource<TenantDirectoryOperatorIdentityDocumentDto> resource =
            await directoryIdentityResourceAssembler.ToResource(document, HttpContext);
        return Ok(resource);
    }

    [HttpPatch(
        "directory-operator-identity",
        Name = RouteNames.PatchTenantDirectoryOperatorIdentityDocument)]
    [EndpointSummary("Patch Tenant Directory Operator Identity")]
    [EndpointDescription(
        "Patches supplied legal-identity groups with optimistic concurrency while preserving omitted fields.")]
    [Consumes("application/json")]
    [ProducesResponseType(
        typeof(HalResource<TenantDirectoryOperatorIdentityDocumentDto>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<TenantDirectoryOperatorIdentityDocumentDto>>>
        PatchDirectoryOperatorIdentity(
            [FromBody] PatchTenantDirectoryOperatorIdentityDocumentDto patch,
            [FromServices] IOutputCacheStore cacheStore,
            CancellationToken cancellationToken = default)
    {
        BaseCommandResponse<TenantDirectoryOperatorIdentityDocumentDto> response =
            await mediator.Send(
                new PatchTenantDirectoryOperatorIdentityDocumentCommand
                {
                    TenantId = tenantContext.TenantId,
                    Patch = patch
                },
                cancellationToken);
        if (!response.IsSuccess)
        {
            if (response.FailureCode == FailureCodes.NotFound)
            {
                return this.ToNotFoundProblem(DirectoryIdentityNotFoundProblem);
            }

            return this.ToCommandValidationProblem(
                response,
                PatchDirectoryIdentityValidationProblem);
        }

        TenantDirectoryOperatorIdentityDocumentDto? updated = await mediator.Send(
            new GetTenantDirectoryOperatorIdentityDocumentQuery(tenantContext.TenantId),
            cancellationToken);
        if (updated is null)
        {
            return this.ToNotFoundProblem(DirectoryIdentityNotFoundProblem);
        }

        await cacheStore.EvictByTagAsync("public-experience-shell", cancellationToken);
        HalResource<TenantDirectoryOperatorIdentityDocumentDto> resource =
            await directoryIdentityResourceAssembler.ToResource(updated, HttpContext);
        return Ok(resource);
    }
}
