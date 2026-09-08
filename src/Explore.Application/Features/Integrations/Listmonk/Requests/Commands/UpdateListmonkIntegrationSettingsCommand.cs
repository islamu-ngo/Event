using Explore.Application.DTOs.Integrations;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Integrations.Listmonk.Requests.Commands;

public sealed record UpdateListmonkIntegrationSettingsCommand : IRequest<BaseCommandResponse<Guid>>
{
    public UpdateListmonkIntegrationSettingsDto Dto { get; init; } = new();
}
