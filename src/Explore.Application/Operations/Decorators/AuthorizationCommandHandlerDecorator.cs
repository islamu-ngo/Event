using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Operations.Decorators;

internal sealed class AuthorizationCommandHandlerDecorator<TCommand>(
    ICommandHandler<TCommand> inner,
    RequestAuthorization<TCommand> authorization,
    ILogger<RequestAuthorization<TCommand>> logger) : ICommandHandler<TCommand>
    where TCommand : ICommand
{
    public async Task ExecuteAsync(TCommand command, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(command, logger, cancellationToken);
        await inner.ExecuteAsync(command, cancellationToken);
    }
}

internal sealed class AuthorizationCommandHandlerDecorator<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner,
    RequestAuthorization<TCommand> authorization,
    ILogger<RequestAuthorization<TCommand>> logger) : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    public async Task<TResult> ExecuteAsync(TCommand command, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(command, logger, cancellationToken);
        return await inner.ExecuteAsync(command, cancellationToken);
    }
}
