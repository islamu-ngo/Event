using Explore.Application.Authorization;
using Explore.Application.DTOs.EventSessionLanguage;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventSessionLanguages.Requests.Commands;

[AuthorizeResource(ResourceKinds.EventSession, AuthorizationActions.Update)]
public sealed record UpdateEventSessionLanguageCommand : ICommand<BaseCommandResponse<int>>, ISecureRequest
{
    public int EventSessionLanguageId { get; init; }

    public Guid ExpectedConcurrencyStamp { get; init; }

    public required UpdateEventSessionLanguageDto EventSessionLanguageDto { get; init; }

    public Guid EventSessionId { get; init; }

    string? ISecureRequest.ResourceId => EventSessionId.ToString();

}
