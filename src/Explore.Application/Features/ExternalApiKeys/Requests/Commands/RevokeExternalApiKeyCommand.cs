using MediatR;

namespace Explore.Application.Features.ExternalApiKeys.Requests.Commands;

public sealed record RevokeExternalApiKeyCommand(Guid Id = default) : IRequest<bool>;
