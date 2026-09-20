using System;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Users.Requests.Commands;

public sealed record UpdateUserLastActiveTenantCommand(
    Guid UserId = default,
    Guid TenantId = default
) : ICommand<bool>;
