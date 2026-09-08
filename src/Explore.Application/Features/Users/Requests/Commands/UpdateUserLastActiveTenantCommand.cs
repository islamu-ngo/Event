using System;
using MediatR;

namespace Explore.Application.Features.Users.Requests.Commands;

public sealed record UpdateUserLastActiveTenantCommand(
    Guid UserId = default,
    Guid TenantId = default
) : IRequest<bool>;
