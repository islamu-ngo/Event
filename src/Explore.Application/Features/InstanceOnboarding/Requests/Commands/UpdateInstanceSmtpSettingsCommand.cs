using Explore.Application.DTOs.Instance;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Commands;

public sealed record UpdateInstanceSmtpSettingsCommand : IRequest<BaseCommandResponse<Guid>>
{
    public Guid UserId { get; init; }
    public required PatchInstanceSmtpSettingsDto Patch { get; init; } = new();
}
