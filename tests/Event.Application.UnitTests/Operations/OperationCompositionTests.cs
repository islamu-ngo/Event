using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class OperationCompositionTests
{
    internal static IServiceCollection Services(params Type[] types)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<OwnedState>();
        services.AddSingleton<IAuthorizationProvider>(new OperationAuthorizationTests.Policy(AuthorizationDecision.Allow(AuthorizationProviderMetadata.Local)));
        services.AddNativeOperations(types);
        return services;
    }

    [Test]
    public async Task NewlyDiscoveredOperationsResolveAllShapesWithoutManualHandlerEntries()
    {
        var services = Services(typeof(Write), typeof(ResultWrite), typeof(Read), typeof(StatefulHandler));
        services.ValidateNativeOperationRegistrations();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ICommandHandler<Write>>().ExecuteAsync(new(), default);
        var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<ResultWrite, int>>().ExecuteAsync(new(), default);
        var read = await scope.ServiceProvider.GetRequiredService<IQueryHandler<Read, int>>().QueryAsync(new(), default);
        await Assert.That(result).IsEqualTo(2);
        await Assert.That(read).IsEqualTo(2);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task MissingAndDuplicateHandlersFailRegistration(int defect)
    {
        Type[] types = defect switch
        {
            0 => [typeof(Write)],
            1 => [typeof(Write), typeof(FirstHandler), typeof(SecondHandler)],
            _ => [typeof(Mixed)]
        };
        await Assert.That(() => Services(types)).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LateRawAndOpenGenericEntriesCannotReplaceProtection(bool openGeneric)
    {
        var services = Services(typeof(Write), typeof(FirstHandler));
        if (openGeneric)
            services.AddScoped(typeof(ICommandHandler<>), typeof(GenericHandler<>));
        else
            services.AddScoped<ICommandHandler<Write>, SecondHandler>();
        await Assert.That(services.ValidateNativeOperationRegistrations).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task MetadataPreflightRejectsMissingParametersWithoutConstructingHandlers()
    {
        var services = Services(typeof(Write), typeof(MissingDependencyHandler));
        await using var provider = services.BuildServiceProvider();
        await Assert.That(() => provider.ValidateNativeOperations()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task OpaqueAliasReentryFailsBoundedlyAndFinallyCleanupAllowsAcyclicRetry()
    {
        var services = Services(typeof(Write), typeof(AliasHandler));
        var recurse = true;
        var aliasEntries = 0;
        services.AddScoped<Alias>(provider =>
        {
            // Bound the adversarial fixture itself so a removed production guard fails a test,
            // rather than overflowing the test process stack.
            if (++aliasEntries > 8)
                throw new EscapedNativeCycleException();
            return recurse ? new Alias(provider.GetRequiredService<ICommandHandler<Write>>()) : new Alias(null);
        });
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var failure = await Assert.That(() => scope.ServiceProvider.GetRequiredService<ICommandHandler<Write>>())
            .Throws<InvalidOperationException>();
        await Assert.That(failure is { Message.Length: < 512 }).IsTrue();
        recurse = false;
        await scope.ServiceProvider.GetRequiredService<ICommandHandler<Write>>().ExecuteAsync(new(), default);
    }

    [Test]
    public async Task NestedOperationsAndConcreteServiceAliasShareScopeAndAreDisposedExactlyOnce()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<OwnedState>();
        services.AddSingleton<IAuthorizationProvider>(new OperationAuthorizationTests.Policy(AuthorizationDecision.Allow(AuthorizationProviderMetadata.Local)));
        services.AddScoped<StatefulHandler>();
        services.AddScoped<IState>(provider => provider.GetRequiredService<StatefulHandler>());
        services.AddScoped<IDirectState, StatefulHandler>();
        services.AddNativeOperations([typeof(Write), typeof(ResultWrite), typeof(Read), typeof(StatefulHandler), typeof(Nested), typeof(NestedHandler)]);
        services.ValidateNativeOperationRegistrations();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        StatefulHandler first;
        await using (var scope = provider.CreateAsyncScope())
        {
            first = scope.ServiceProvider.GetRequiredService<StatefulHandler>();
            await Assert.That(ReferenceEquals(first, scope.ServiceProvider.GetRequiredService<IState>())).IsTrue();
            await Assert.That(ReferenceEquals(first, scope.ServiceProvider.GetRequiredService<IDirectState>())).IsTrue();
            await scope.ServiceProvider.GetRequiredService<ICommandHandler<Nested>>().ExecuteAsync(new(), default);
            await Assert.That(await scope.ServiceProvider.GetRequiredService<IQueryHandler<Read, int>>().QueryAsync(new(), default)).IsEqualTo(1);
            await Assert.That(first.DisposeCount).IsEqualTo(0);
        }
        await Assert.That(first.DisposeCount).IsEqualTo(1);
        await using var second = provider.CreateAsyncScope();
        await Assert.That(await second.ServiceProvider.GetRequiredService<IQueryHandler<Read, int>>().QueryAsync(new(), default)).IsEqualTo(0);
    }

    [Test]
    public async Task DeepValidationUsesDisposableScopeWithoutExecutingOperations()
    {
        var services = Services(typeof(Write), typeof(DisposableHandler));
        var evidence = new DisposalEvidence();
        services.AddSingleton(evidence);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        provider.ValidateNativeOperations();
        await Assert.That(evidence.Constructed).IsEqualTo(0);
        await provider.ValidateNativeOperationsDeepAsync();
        await Assert.That(evidence.Constructed).IsEqualTo(1);
        await Assert.That(evidence.Disposed).IsEqualTo(1);
        await Assert.That(evidence.Executed).IsEqualTo(0);
    }

    [Test]
    [Arguments(false, false, false)]
    [Arguments(false, true, false)]
    [Arguments(true, false, false)]
    [Arguments(true, true, false)]
    [Arguments(false, false, true)]
    [Arguments(false, true, true)]
    [Arguments(true, false, true)]
    [Arguments(true, true, true)]
    public async Task DisposableHandlerAliasesAreRejectedBeforeAcquiringMultipleOwners(bool asynchronous, bool opaque, bool beforeDiscovery)
    {
        var handler = asynchronous ? typeof(AsyncDisposableHandler) : typeof(DisposableHandler);
        var services = new ServiceCollection();
        services.AddSingleton(new DisposalEvidence());
        if (!beforeDiscovery)
            services.AddNativeOperations([typeof(Write), handler]);
        if (opaque)
            services.AddScoped<IDisposablePort>(provider => (IDisposablePort)provider.GetRequiredService(handler));
        else
            services.AddScoped(typeof(IDisposablePort), handler);
        if (beforeDiscovery)
            services.AddNativeOperations([typeof(Write), handler]);

        await Assert.That(services.ValidateNativeOperationRegistrations).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task UnaliasedAsyncHandlerHasOneScopeDisposalOwner()
    {
        var services = Services(typeof(Write), typeof(AsyncDisposableHandler));
        var evidence = new DisposalEvidence();
        services.AddSingleton(evidence);
        services.ValidateNativeOperationRegistrations();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await provider.ValidateNativeOperationsDeepAsync();

        await Assert.That(evidence.Constructed).IsEqualTo(1);
        await Assert.That(evidence.Disposed).IsEqualTo(1);
        await Assert.That(evidence.Executed).IsEqualTo(0);
    }

    public sealed record Write : ICommand;
    public sealed record ResultWrite : ICommand<int>;
    public sealed record Read : IQuery<int>;
    public sealed record Nested : ICommand;
    public sealed record Mixed : ICommand, IQuery<int>;
    public interface IState;
    public interface IDirectState;
    public interface IMissing;
    public interface IDisposablePort;
    public sealed class EscapedNativeCycleException : Exception;
    public sealed class DisposalEvidence
    {
        public int Constructed { get; set; }
        public int Disposed { get; set; }
        public int Executed { get; set; }
    }
    public sealed class DisposableHandler : ICommandHandler<Write>, IDisposablePort, IDisposable
    {
        private readonly DisposalEvidence _evidence;
        public DisposableHandler(DisposalEvidence evidence)
        {
            _evidence = evidence;
            evidence.Constructed++;
        }
        public Task ExecuteAsync(Write command, CancellationToken cancellationToken)
        {
            _evidence.Executed++;
            return Task.CompletedTask;
        }
        public void Dispose() => _evidence.Disposed++;
    }

    public sealed class AsyncDisposableHandler : ICommandHandler<Write>, IDisposablePort, IAsyncDisposable
    {
        private readonly DisposalEvidence _evidence;

        public AsyncDisposableHandler(DisposalEvidence evidence)
        {
            _evidence = evidence;
            evidence.Constructed++;
        }

        public Task ExecuteAsync(Write command, CancellationToken cancellationToken)
        {
            _evidence.Executed++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _evidence.Disposed++;
            return ValueTask.CompletedTask;
        }
    }

    public sealed class OwnedState : IDisposable
    {
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }

    public sealed class StatefulHandler(OwnedState owned) : ICommandHandler<Write>, ICommandHandler<ResultWrite, int>, IQueryHandler<Read, int>, IState, IDirectState
    {
        private int _value;
        public int DisposeCount => owned.DisposeCount;
        public Task ExecuteAsync(Write command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _value++;
            return Task.CompletedTask;
        }
        public Task<int> ExecuteAsync(ResultWrite command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(++_value);
        }
        public Task<int> QueryAsync(Read query, CancellationToken cancellationToken) => Task.FromResult(_value);
    }

    public sealed class NestedHandler(ICommandHandler<Write> write) : ICommandHandler<Nested>
    {
        public Task ExecuteAsync(Nested command, CancellationToken cancellationToken) => write.ExecuteAsync(new(), cancellationToken);
    }
    public sealed class FirstHandler : ICommandHandler<Write>
    {
        public Task ExecuteAsync(Write command, CancellationToken cancellationToken) => Task.CompletedTask;
    }
    public sealed class SecondHandler : ICommandHandler<Write>
    {
        public Task ExecuteAsync(Write command, CancellationToken cancellationToken) => Task.CompletedTask;
    }
    public sealed class GenericHandler<T> : ICommandHandler<T> where T : ICommand
    {
        public Task ExecuteAsync(T command, CancellationToken cancellationToken) => Task.CompletedTask;
    }
    public sealed class MissingDependencyHandler(IMissing missing) : ICommandHandler<Write>
    {
        public Task ExecuteAsync(Write command, CancellationToken cancellationToken) => Task.FromResult(missing);
    }
    public sealed record Alias(ICommandHandler<Write>? Handler);
    public sealed class AliasHandler(Alias alias) : ICommandHandler<Write>
    {
        public Task ExecuteAsync(Write command, CancellationToken cancellationToken) => Task.FromResult(alias);
    }
}
