using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Roles.Requests.Commands;

public sealed record DeleteCustomRoleCommand(int RoleId = default) : IRequest<BaseCommandResponse<int>>;
