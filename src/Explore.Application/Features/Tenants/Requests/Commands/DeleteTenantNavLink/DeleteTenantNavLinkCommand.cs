using System;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Tenants.Requests.Commands.DeleteTenantNavLink;

/// <summary>
/// Command to delete a tenant navigation link.
/// Returns a boolean indicating success or failure.
/// </summary>
public sealed record DeleteTenantNavLinkCommand(Guid Id = default) : IRequest<BaseCommandResponse<bool>>;
