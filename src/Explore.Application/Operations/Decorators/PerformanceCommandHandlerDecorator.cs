using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Operations.Decorators;

internal sealed class PerformanceCommandHandlerDecorator<TCommand>(
    ICommandHandler<TCommand> inner,
    TimeProvider clock,
    ILogger<PerformanceCommandHandlerDecorator<TCommand>> logger) : ICommandHandler<TCommand>
    where TCommand : ICommand
{
    public async Task ExecuteAsync(TCommand command, CancellationToken cancellationToken)
    {
        var started = clock.GetTimestamp();
        await inner.ExecuteAsync(command, cancellationToken);
        var elapsedMilliseconds = (long)clock.GetElapsedTime(started).TotalMilliseconds;
        if (elapsedMilliseconds > 500)
            logger.LogWarning("Long Running Request: {RequestType} ({ElapsedMilliseconds}ms)", typeof(TCommand).Name, elapsedMilliseconds);
    }
}

internal sealed class PerformanceCommandHandlerDecorator<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner,
    TimeProvider clock,
    ILogger<PerformanceCommandHandlerDecorator<TCommand, TResult>> logger) : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    public async Task<TResult> ExecuteAsync(TCommand command, CancellationToken cancellationToken)
    {
        var started = clock.GetTimestamp();
        var result = await inner.ExecuteAsync(command, cancellationToken);
        var elapsedMilliseconds = (long)clock.GetElapsedTime(started).TotalMilliseconds;
        if (elapsedMilliseconds > 500)
            logger.LogWarning("Long Running Request: {RequestType} ({ElapsedMilliseconds}ms)", typeof(TCommand).Name, elapsedMilliseconds);
        return result;
    }
}
