using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Features.StorageObjects.Requests.Commands;
using Explore.Application.Features.StorageObjects.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Models.Storage;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public class StorageObjectController : ControllerBase
{
    private static readonly ApiValidationProblemDescriptor UpdateValidationProblem = new(
        "storageObject",
        "Storage object validation failed",
        "Storage object update failed.");

    private static readonly ApiNotFoundProblemDescriptor StorageObjectNotFoundProblem = new(
        "Storage object not found",
        "Storage object not found.");

    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;
    private readonly IResourceAssembler<StorageObjectDto, StorageObjectListDto> _resourceAssembler;

    public StorageObjectController(
        IMediator mediator,
        ITenantContext tenantContext,
        IResourceAssembler<StorageObjectDto, StorageObjectListDto> resourceAssembler)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
        _resourceAssembler = resourceAssembler;
    }

    // GET: api/storageobject
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpGet(Name = RouteNames.GetStorageObjects)]
    [EndpointSummary("Get all Storage Objects")]
    [EndpointDescription("Retrieve a paginated list of all storage objects (files, images, documents, etc.). Default page size is 20, max is 100.")]
    [ProducesResponseType(typeof(HalCollectionResource<StorageObjectListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [OutputCache(PolicyName = "ListData")]
    public async Task<ActionResult<HalCollectionResource<StorageObjectListDto>>> GetAll(
        [FromQuery] PaginationQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var storageObjects = await _mediator.Send(new GetStorageObjectListRequest
        {
            TenantId = _tenantContext.TenantId,
            PageNumber = query.PageNumber,
            PageSize = query.PageSize
        }, cancellationToken);
        var halResource = await _resourceAssembler.ToCollectionResource(
            storageObjects,
            RouteNames.GetStorageObjects,
            additionalRouteValues: null,
            HttpContext);

        return Ok(halResource);
    }

    // GET: api/storageobject/{id}
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpGet("{id:guid}", Name = RouteNames.GetStorageObjectById)]
    [EndpointSummary("Get Storage Object by ID")]
    [EndpointDescription("Retrieve details of a specific storage object")]
    [ProducesResponseType(typeof(HalResource<StorageObjectDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [OutputCache(PolicyName = "DetailData")]
    public async Task<ActionResult<HalResource<StorageObjectDto>>> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var storageObject = await _mediator.Send(
            new GetStorageObjectDetailsRequest
            {
                Id = id,
                TenantId = _tenantContext.TenantId
            },
            cancellationToken);
        if (storageObject is null)
        {
            return this.ToNotFoundProblem(StorageObjectNotFoundProblem);
        }

        var halResource = await _resourceAssembler.ToResource(storageObject, HttpContext);
        return Ok(halResource);
    }

    // GET: api/storageobject/{id}/content
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpGet("{id:guid}/content", Name = RouteNames.GetStorageObjectContent)]
    [EndpointSummary("Get Storage Object Content")]
    [EndpointDescription("Streams stored file content by stable storage object ID. Provider keys and local paths are never accepted from the browser.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetContent(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetStorageObjectContentRequest
            {
                StorageObjectId = id,
                TenantId = _tenantContext.TenantId
            },
            cancellationToken);

        return result is null ? this.ToNotFoundProblem(StorageObjectNotFoundProblem) : ToFileResult(result);
    }

    // GET: api/storageobject/{id}/public
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("{id:guid}/public", Name = RouteNames.GetPublicStorageObjectImage)]
    [EndpointSummary("Get Public Image")]
    [EndpointDescription("Serves an image directly by storage object ID. Returns a stable, non-expiring URL " +
        "suitable for OG image tags and social media preview cards. Images are cached for 7 days.")]
    [ResponseCache(Duration = 604800, Location = ResponseCacheLocation.Any)] // 7 days
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPublicImage(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetPublicImageRequest(id), cancellationToken);

        if (result is null)
        {
            return this.ToNotFoundProblem(StorageObjectNotFoundProblem);
        }

        return ToFileResult(result);
    }

    // GET: api/storageobject/{id}/presigned-url
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpGet("{id:guid}/presigned-url", Name = RouteNames.GetStorageObjectPresignedDownloadUrl)]
    [EndpointSummary("Get Presigned Download URL")]
    [EndpointDescription("Generate a time-limited presigned URL for viewing/downloading a file from S3-compatible storage")]
    [ProducesResponseType(typeof(PresignedDownloadUrlResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<PresignedDownloadUrlResponseDto>> GetPresignedDownloadUrl(Guid id, [FromQuery] int expirationMinutes = 60, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetPresignedDownloadUrlRequest
        {
            Id = id,
            ExpirationMinutes = expirationMinutes,
            TenantId = _tenantContext.TenantId
        }, cancellationToken);

        return result is null ? this.ToNotFoundProblem(StorageObjectNotFoundProblem) : Ok(result);
    }

    // POST: api/storageobject/upload-sessions
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost("upload-sessions", Name = RouteNames.CreateStorageUploadSession)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    [EndpointSummary("Create provider-neutral upload session")]
    [EndpointDescription("Reserve tenant storage quota and return a server-bound upload session. The browser does not choose provider, object key, local path, or destination URL.")]
    [ProducesResponseType(typeof(BaseCommandResponse<StorageUploadSessionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<BaseCommandResponse<StorageUploadSessionDto>>> CreateUploadSession(
        [FromBody] CreateStorageUploadSessionDto dto,
        CancellationToken cancellationToken = default)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return this.ToAuthenticationRequiredProblem();
        }

        var command = new CreateStorageUploadSessionCommand
        {
            UploadSessionDto = dto,
            TenantId = _tenantContext.TenantId
        };
        var response = await _mediator.Send(command, cancellationToken);

        return response.IsSuccess ? Ok(response) : this.ToStorageUploadProblem(response);
    }

    // PUT: api/storageobject/upload-sessions/{uploadSessionId}/content
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPut("upload-sessions/{uploadSessionId:guid}/content", Name = RouteNames.UploadStorageUploadSessionContent)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ComplexPolicy)]
    [RequestSizeLimit(536_870_912)]
    [EndpointSummary("Upload bytes for a reserved storage session")]
    [EndpointDescription("Streams request bytes into the server-selected storage provider and finalizes the upload session metadata.")]
    [ProducesResponseType(typeof(BaseCommandResponse<StorageUploadSessionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<BaseCommandResponse<StorageUploadSessionDto>>> UploadSessionContent(
        Guid uploadSessionId,
        CancellationToken cancellationToken = default)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return this.ToAuthenticationRequiredProblem();
        }

        var command = new FinalizeStorageUploadSessionCommand
        {
            UploadSessionId = uploadSessionId,
            Content = Request.Body,
            ContentType = string.Equals(
                Request.ContentType,
                "application/octet-stream",
                StringComparison.OrdinalIgnoreCase)
                ? null
                : Request.ContentType,
            ContentLength = Request.ContentLength,
            TenantId = _tenantContext.TenantId
        };
        var response = await _mediator.Send(command, cancellationToken);

        return response.IsSuccess ? Ok(response) : this.ToStorageUploadProblem(response);
    }

    // DELETE: api/storageobject/upload-sessions/{uploadSessionId}
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpDelete("upload-sessions/{uploadSessionId:guid}", Name = RouteNames.CancelStorageUploadSession)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    [EndpointSummary("Cancel a provider-neutral upload session")]
    [EndpointDescription("Cancel a pending upload session and release its reserved tenant storage quota.")]
    [ProducesResponseType(typeof(BaseCommandResponse<StorageUploadSessionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<StorageUploadSessionDto>>> CancelUploadSession(
        Guid uploadSessionId,
        CancellationToken cancellationToken = default)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return this.ToAuthenticationRequiredProblem();
        }

        var command = new CancelStorageUploadSessionCommand
        {
            UploadSessionId = uploadSessionId,
            TenantId = _tenantContext.TenantId
        };
        var response = await _mediator.Send(command, cancellationToken);

        return response.IsSuccess ? Ok(response) : this.ToStorageUploadProblem(response);
    }

    // PATCH: api/storageobject/{id}
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPatch("{id:guid}", Name = RouteNames.UpdateStorageObject)]
    [EndpointSummary("Update Storage Object")]
    [EndpointDescription("Update an existing storage object")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Update(Guid id, [FromBody] UpdateStorageObjectDto dto, CancellationToken cancellationToken = default)
    {
        var command = new UpdateStorageObjectCommand
        {
            StorageObjectId = id,
            StorageObjectDto = dto
        };
        var response = await _mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, UpdateValidationProblem);
        }

        return Ok(response);
    }

    // DELETE: api/storageobject/{id}
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpDelete("{id:guid}", Name = RouteNames.DeleteStorageObject)]
    [EndpointSummary("Delete Storage Object")]
    [EndpointDescription("Delete a storage object")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var command = new DeleteStorageObjectCommand { Id = id };
        await _mediator.Send(command, cancellationToken);

        return NoContent();
    }

    private FileStreamResult ToFileResult(StorageObjectContentResult result)
    {
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";

        var fileResult = result.ShouldDownloadAsAttachment
            ? File(
                result.Content,
                result.ContentType,
                ResolveSafeDownloadName(result.SafeDisplayName),
                enableRangeProcessing: true)
            : File(result.Content, result.ContentType, enableRangeProcessing: true);
        fileResult.LastModified = result.LastModified;

        if (!string.IsNullOrWhiteSpace(result.Sha256Checksum))
        {
            fileResult.EntityTag = new EntityTagHeaderValue($"\"{result.Sha256Checksum}\"");
        }

        return fileResult;
    }

    private static string ResolveSafeDownloadName(string value)
        => value.Length is > 0 and <= 255
            && value is not "." and not ".."
            && !value.Any(char.IsControl)
            && !value.Contains('/', StringComparison.Ordinal)
            && !value.Contains('\\', StringComparison.Ordinal)
                ? value
                : "download";
}
