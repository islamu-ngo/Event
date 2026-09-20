using Event.Architecture.Tests.Fixtures;
using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.API.Services;

public sealed class NativeConsumerService(NativeConsumerHandler handler)
{
    public NativeConsumerHandler Handler { get; } = handler;
}

public sealed class ProtectedNativeConsumerService(
    ICommandHandler<NativeConsumerCommand> handler,
    IEnumerable<ICommandHandler<NativeConsumerCommand>> handlers)
{
    public ICommandHandler<NativeConsumerCommand> Handler { get; } = handler;
    public IEnumerable<ICommandHandler<NativeConsumerCommand>> Handlers { get; } = handlers;
}

public sealed class NativeScopedConsumerService(IServiceScopeFactory scopes)
{
    public async Task ExecuteAsync()
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<NativeConsumerHandler>()
            .ExecuteAsync(new NativeConsumerCommand(), default);
    }
}

public sealed class NativeCollectionConsumerService(IEnumerable<NativeConsumerHandler> handlers)
{
    public IEnumerable<NativeConsumerHandler> Handlers { get; } = handlers;
}

public sealed class NativePluralResolutionService(IServiceScopeFactory scopes)
{
    public async Task ExecuteAsync()
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetServices<NativeConsumerHandler>().Single()
            .ExecuteAsync(new NativeConsumerCommand(), default);
    }
}

public sealed class NativeEnumerableResolutionService(IServiceScopeFactory scopes)
{
    public async Task ExecuteAsync()
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IEnumerable<NativeConsumerHandler>>().Single()
            .ExecuteAsync(new NativeConsumerCommand(), default);
    }
}
