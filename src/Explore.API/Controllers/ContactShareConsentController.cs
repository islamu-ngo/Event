using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Models;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.ContactShareConsent;
using Explore.Application.Features.ContactShareConsents.Requests.Commands;
using Explore.Application.Features.ContactShareConsents.Requests.Queries;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
[EndpointClassification(EndpointClass.Authenticated)]
public class ContactShareConsentController : EventControllerBase
{
    private static readonly ApiNotFoundProblemDescriptor OrganizationNotFoundProblem = new(
        "Contact share recipient organization not found",
        "The requested contact-share recipient organization could not be resolved.");

    private static readonly ApiValidationProblemDescriptor ExportValidationProblem = new(
        "contactShareExport",
        "Contact share export validation failed",
        "Shared contact export failed.");

    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;
    private readonly IContactShareConsentService _consentService;
    private readonly ILogger<ContactShareConsentController> _logger;

    public ContactShareConsentController(
        IMediator mediator,
        ITenantContext tenantContext,
        IContactShareConsentService consentService,
        ILogger<ContactShareConsentController> logger)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
        _consentService = consentService;
        _logger = logger;
    }

    // GET: api/contactshareconsent/my
    [HttpGet("my", Name = Hateoas.RouteNames.GetUserContactShareConsents)]
    [EndpointSummary("Get my contact sharing consents")]
    [EndpointDescription("Retrieve all contact sharing consents for the current user (Connected Apps page).")]
    [Authorize]
    [ProducesResponseType(typeof(List<UserContactShareConsentDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<UserContactShareConsentDto>>> GetMyConsents(CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserId;
        if (userId == null)
            return this.ToAuthenticationRequiredProblem();

        var result = await _mediator.Send(new GetUserContactShareConsentsQuery
        {
            UserId = userId.Value,
            TenantId = _tenantContext.TenantId
        }, cancellationToken);

        return Ok(result);
    }

    // GET: api/contactshareconsent/check/{recipientActorId}
    [HttpGet("check/{recipientActorId:guid}", Name = Hateoas.RouteNames.CheckConsentForOrganizer)]
    [EndpointSummary("Check if consent exists for organizer")]
    [EndpointDescription("Check whether the current user has a granted consent for a specific organizer. Used by registration UI.")]
    [Authorize]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    public async Task<ActionResult<bool>> CheckConsentForOrganizer(Guid recipientActorId)
    {
        var userId = CurrentUserId;
        if (userId == null)
            return this.ToAuthenticationRequiredProblem();

        var hasConsent = await _consentService.HasGrantedConsentForOrganizer(
            _tenantContext.TenantId, userId.Value, recipientActorId);

        return Ok(hasConsent);
    }

    // POST: api/contactshareconsent/withdraw/{id}
    [HttpPost("withdraw/{id:guid}", Name = Hateoas.RouteNames.WithdrawContactShareConsent)]
    [EndpointSummary("Withdraw contact sharing consent")]
    [EndpointDescription("Withdraw a previously granted contact sharing consent. The consent is marked as withdrawn, not deleted.")]
    [Authorize]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> WithdrawConsent(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserId;
        if (userId == null)
            return this.ToAuthenticationRequiredProblem();

        var result = await _mediator.Send(new WithdrawContactShareConsentCommand
        {
            ConsentId = id,
            UserId = userId.Value,
            TenantId = _tenantContext.TenantId
        }, cancellationToken);

        return Ok(result);
    }

    // GET: api/contactshareconsent/organization/{recipientActorId}
    [HttpGet("organization/{recipientActorId:guid}", Name = Hateoas.RouteNames.GetOrganizationSharedContacts)]
    [EndpointSummary("Get shared contacts for organization")]
    [EndpointDescription("Retrieve paginated list of shared contacts for an organization. Requires ViewSharedContacts permission.")]
    [Authorize]
    [ProducesResponseType(typeof(PaginatedResult<SharedContactDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PaginatedResult<SharedContactDto>>> GetOrganizationSharedContacts(
        Guid recipientActorId,
        [FromQuery] ContactShareConsentListQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var organizationId = await ResolveOrganizationId(recipientActorId);
        if (organizationId is null)
            return this.ToNotFoundProblem(OrganizationNotFoundProblem);

        var result = await _mediator.Send(new GetOrganizationSharedContactsQuery
        {
            RecipientActorId = recipientActorId,
            OrganizationId = organizationId.Value,
            EventId = query.EventId,
            EmailSearch = query.EmailSearch,
            PageNumber = query.PageNumber,
            PageSize = query.PageSize,
            TenantId = _tenantContext.TenantId
        }, cancellationToken);

        return Ok(result);
    }

    // POST: api/contactshareconsent/organization/{recipientActorId}/export
    [HttpPost("organization/{recipientActorId:guid}/export", Name = Hateoas.RouteNames.ExportOrganizationSharedContacts)]
    [EndpointSummary("Export shared contacts")]
    [EndpointDescription("Export shared contacts as CSV or TSV. Records an audit trail of the export.")]
    [Authorize]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> ExportSharedContacts(
        Guid recipientActorId,
        [FromQuery] ContactShareConsentExportQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserId;
        if (userId == null)
            return this.ToAuthenticationRequiredProblem();

        var organizationId = await ResolveOrganizationId(recipientActorId);
        if (organizationId is null)
            return this.ToNotFoundProblem(OrganizationNotFoundProblem);

        var result = await _mediator.Send(new ExportSharedContactsCommand
        {
            RecipientActorId = recipientActorId,
            OrganizationId = organizationId.Value,
            EventId = query.EventId,
            Format = query.GetNormalizedFormat(),
            TenantId = _tenantContext.TenantId,
            ExportedByUserId = userId.Value
        }, cancellationToken);

        if (!result.IsSuccess || result.Id is not { FileContent: { } fileContent } export)
            return this.ToCommandValidationProblem(result, ExportValidationProblem);

        return File(fileContent.ToArray(), export.ContentType, export.FileName);
    }

    private async Task<Guid?> ResolveOrganizationId(Guid recipientActorId)
    {
        return await _consentService.ResolveRecipientOrganizationId(recipientActorId);
    }

}
