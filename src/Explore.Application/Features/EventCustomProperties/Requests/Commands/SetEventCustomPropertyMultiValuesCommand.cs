using Explore.Application.Authorization;
using Explore.Application.DTOs.EventCustomProperty;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventCustomProperties.Requests.Commands;

[AuthorizeResource(ResourceKinds.Tenant, AuthorizationActions.Update)]
public sealed record SetEventCustomPropertyMultiValuesCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid DefinitionId { get; init; }
    public Guid EventId { get; init; }
    private IReadOnlyList<SetEventCustomPropertyValueDto> _values = Array.AsReadOnly(Array.Empty<SetEventCustomPropertyValueDto>());

    public required IReadOnlyList<SetEventCustomPropertyValueDto> Values
    {
        get => _values;
        init => _values = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }

    string? ISecureRequest.ResourceId => null;
}
