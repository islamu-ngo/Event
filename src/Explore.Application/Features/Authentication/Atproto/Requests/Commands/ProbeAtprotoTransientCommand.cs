// ABOUTME: Requests a bounded synthetic round trip through the private transient store.
// ABOUTME: Accepts no caller-supplied tenant, locator or payload and returns no storage material.

using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Authentication.Atproto.Requests.Commands;

public sealed record ProbeAtprotoTransientCommand : IRequest<BaseCommandResponse<Guid>>;
