using Explore.Application.DTOs.Integrations;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Integrations.Listmonk.Requests.Commands;

public sealed record UpdateListmonkIntegrationSettingsCommand : ICommand<BaseCommandResponse<Guid>>
{
    public UpdateListmonkIntegrationSettingsDto Dto { get; init; } = new();
}
