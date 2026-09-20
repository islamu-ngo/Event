using Explore.Application.Authorization;
using Explore.Application.DTOs.EventSessionLanguage;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventSessionLanguages.Requests.Commands;

[AuthorizeResource(ResourceKinds.EventSession, AuthorizationActions.Update)]
public sealed record CreateEventSessionLanguageCommand : ICommand<BaseCommandResponse<int>>, ISecureRequest
{
    public required CreateEventSessionLanguageDto EventSessionLanguageDto { get; init; }

    string? ISecureRequest.ResourceId => EventSessionLanguageDto.EventSessionId.ToString();

}
