using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;

namespace Explore.Application.Features.Roles.Requests.Commands;

public sealed record DeleteCustomRoleCommand(int RoleId = default) : ICommand<BaseCommandResponse<int>>;
