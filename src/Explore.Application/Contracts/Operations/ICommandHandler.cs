namespace Explore.Application.Contracts.Operations;

public interface ICommandHandler<in TCommand> where TCommand : ICommand
{
    Task ExecuteAsync(TCommand command, CancellationToken cancellationToken = default);
}

public interface ICommandHandler<in TCommand, TResult> where TCommand : ICommand<TResult>
{
    Task<TResult> ExecuteAsync(TCommand command, CancellationToken cancellationToken = default);
}
