using Explore.Application.Contracts.Operations;

namespace Event.Architecture.Tests.Fixtures;

public sealed record NativeConsumerCommand : ICommand;

public sealed class NativeConsumerHandler : ICommandHandler<NativeConsumerCommand>
{
    public Task ExecuteAsync(NativeConsumerCommand command, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
