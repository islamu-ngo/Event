using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.ExternalApiKeys.Requests.Commands;

public sealed record RevokeExternalApiKeyCommand(Guid Id = default) : ICommand<bool>;
