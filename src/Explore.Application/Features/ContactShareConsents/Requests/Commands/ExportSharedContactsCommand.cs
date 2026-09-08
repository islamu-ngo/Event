using Explore.Application.Authorization;
using Explore.Application.DTOs.ContactShareConsent;
using Explore.Application.Responses;
using Explore.Domain.Constants;
using MediatR;

namespace Explore.Application.Features.ContactShareConsents.Requests.Commands;

[AuthorizeResource(ResourceKinds.EventContactShareConsent, AuthorizationActions.ExportSharedContacts)]
public sealed record ExportSharedContactsCommand : IRequest<BaseCommandResponse<SharedContactExportResultDto>>, ISecureRequest
{
    public Guid RecipientActorId { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid? EventId { get; init; }
    public Guid TenantId { get; init; }
    public Guid ExportedByUserId { get; init; }
    public string PurposeCode { get; init; } = "ORGANIZER_CONTACT_SHARE_EXPORT";
    public string ConsentPurposeCode { get; init; } = ConsentPurposeCodes.OrganizerFutureCommunications;
    public string PolicyVersion { get; init; } = "phase13.v1";

    /// <summary>
    /// Export format: "csv" or "tsv".
    /// </summary>
    public string Format { get; init; } = "csv";

    string? ISecureRequest.ResourceId => OrganizationId.ToString();

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new ContactShareAuthorizationFacts(TenantId, OrganizationId);
}
