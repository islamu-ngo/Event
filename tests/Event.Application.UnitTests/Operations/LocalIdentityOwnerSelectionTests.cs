using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Authentication.Local.Handlers.Commands;
using Explore.Application.Features.Authentication.Local.Handlers.Queries;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Features.Authentication.Local.Requests.Commands;
using Explore.Application.Features.Authentication.Local.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Application.UnitTests.Operations;

/// <summary>Checks dispatch custody only; the test scope factory does not supply identity authority.</summary>
public sealed class LocalIdentityOwnerSelectionTests
{
    [Test]
    public async Task LocalLoginCannotConstructOnAmbientOwnerWhileSelectionWaits()
    {
        await using var selected = Services(true, typeof(LocalLoginCommand), typeof(LoginProbe)).BuildServiceProvider();
        var factory = new SelectedScopeFactory(selected);
        var services = Services(false, typeof(LocalLoginCommand), typeof(LoginProbe));
        services.AddSingleton<ILocalIdentityExecutionScopeFactory>(factory);
        await using var ambient = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var request = ambient.CreateAsyncScope();
        var ambientOwner = request.ServiceProvider.GetRequiredService<OwnerEvidence>();
        var port = request.ServiceProvider.GetRequiredService<ICommandHandler<LocalLoginCommand, LocalAuthResponseDto>>();
        Task<LocalAuthResponseDto> execution = port.ExecuteAsync(new(new(string.Empty, string.Empty)), CancellationToken.None);
        try
        {
            await Task.WhenAny(factory.Requested.Task, execution).WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(factory.Requested.Task.IsCompleted).IsTrue();
            await Assert.That(execution.IsCompleted).IsFalse();
            await Assert.That(ambientOwner.Constructed).IsFalse();
            await Assert.That(factory.Owner).IsNull();
            factory.Release.TrySetResult();
            var result = await execution;
            await Assert.That(result.Failure).IsEqualTo(LocalAuthFailure.ProviderInactive);
            await Assert.That(factory.Owner!.Constructed).IsTrue();
            await Assert.That(factory.Owner.Executed).IsTrue();
            await Assert.That(factory.Owner.Disposed).IsTrue();
            await Assert.That(ambientOwner.Constructed).IsFalse();
        }
        finally
        {
            factory.Release.TrySetResult();
            try { await execution; }
            catch { }
        }
    }

    [Test]
    public async Task CapabilitiesSelectsOriginalOwnerWithoutInventingHumanAdministratorPreparation()
    {
        await using var selected = Services(true, typeof(GetLocalIdentityLifecycleCapabilitiesQuery), typeof(CapabilitiesProbe))
            .BuildServiceProvider();
        var factory = new SelectedScopeFactory(selected);
        factory.Release.TrySetResult();
        var services = Services(false, typeof(GetLocalIdentityLifecycleCapabilitiesQuery), typeof(CapabilitiesProbe));
        services.AddSingleton<ILocalIdentityExecutionScopeFactory>(factory);
        await using var ambient = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var request = ambient.CreateAsyncScope();
        var ambientOwner = request.ServiceProvider.GetRequiredService<OwnerEvidence>();

        var result = await request.ServiceProvider
            .GetRequiredService<IQueryHandler<GetLocalIdentityLifecycleCapabilitiesQuery, LocalIdentityLifecycleCapabilities>>()
            .QueryAsync(new(PublicDiscovery: true), CancellationToken.None);

        await Assert.That(result).IsEqualTo(new LocalIdentityLifecycleCapabilities(true, true, false));
        await Assert.That(factory.Owner!.Executed).IsTrue();
        await Assert.That(factory.Owner.Disposed).IsTrue();
        await Assert.That(ambientOwner.Constructed).IsFalse();
        await Assert.That(request.ServiceProvider.GetService<IHumanCommitScopePreparation>()).IsNull();
    }

    [Test]
    public async Task MissingLocalOwnerRejectsBeforeRealLoginCredentialDependenciesAreConstructed()
    {
        var services = Services(false, typeof(LocalLoginCommand), typeof(LocalLoginCommandHandler));
        await using var ambient = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var request = ambient.CreateAsyncScope();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await request.ServiceProvider.GetRequiredService<ICommandHandler<LocalLoginCommand, LocalAuthResponseDto>>()
                .ExecuteAsync(new(new(string.Empty, string.Empty)), CancellationToken.None));

        await Assert.That(failure.Message.Contains(
            typeof(ILocalIdentityExecutionScopeFactory).FullName!, StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task MissingLocalOwnerRejectsBeforeRealAdministrativeQueryDependenciesAreConstructed()
    {
        var services = Services(false, typeof(ListLocalIdentitiesQuery), typeof(ListLocalIdentitiesQueryHandler));
        await using var ambient = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var request = ambient.CreateAsyncScope();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await request.ServiceProvider.GetRequiredService<IQueryHandler<ListLocalIdentitiesQuery, LocalIdentityPage>>()
                .QueryAsync(new(1, 10), CancellationToken.None));

        await Assert.That(failure.Message.Contains(
            typeof(ILocalIdentityExecutionScopeFactory).FullName!, StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task UnlistedOperationDoesNotBorrowLocalIdentitySelection()
    {
        await using var selected = Services(true, typeof(OrdinaryQuery), typeof(OrdinaryProbe)).BuildServiceProvider();
        var factory = new SelectedScopeFactory(selected);
        var services = Services(false, typeof(OrdinaryQuery), typeof(OrdinaryProbe));
        services.AddSingleton<ILocalIdentityExecutionScopeFactory>(factory);
        await using var ambient = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var request = ambient.CreateAsyncScope();

        int result = await request.ServiceProvider.GetRequiredService<IQueryHandler<OrdinaryQuery, int>>()
            .QueryAsync(new(), CancellationToken.None);

        await Assert.That(result).IsEqualTo(7);
        await Assert.That(factory.Requested.Task.IsCompleted).IsFalse();
        await Assert.That(factory.Owner).IsNull();
    }

    private static IServiceCollection Services(bool selected, params Type[] types)
    {
        var services = OperationCompositionTests.Services(types);
        services.RemoveAll<IHumanCommitScopePreparation>();
        services.AddScoped<OwnerEvidence>(_ => new(selected));
        return services;
    }

    public sealed class OwnerEvidence(bool selected) : IAsyncDisposable
    {
        public bool Constructed { get; private set; }
        public bool Executed { get; set; }
        public bool Disposed { get; private set; }

        public void ConstructSelected()
        {
            if (!selected)
                throw new InvalidOperationException("A Local handler was constructed on the ambient owner.");
            Constructed = true;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    public sealed class LoginProbe : ICommandHandler<LocalLoginCommand, LocalAuthResponseDto>
    {
        private readonly OwnerEvidence owner;
        public LoginProbe(OwnerEvidence owner) { owner.ConstructSelected(); this.owner = owner; }
        public Task<LocalAuthResponseDto> ExecuteAsync(LocalLoginCommand command, CancellationToken cancellationToken)
        {
            owner.Executed = true;
            return Task.FromResult(LocalAuthResponseDto.Failed(LocalAuthFailure.ProviderInactive));
        }
    }

    public sealed class CapabilitiesProbe : IQueryHandler<GetLocalIdentityLifecycleCapabilitiesQuery, LocalIdentityLifecycleCapabilities>
    {
        private readonly OwnerEvidence owner;
        public CapabilitiesProbe(OwnerEvidence owner) { owner.ConstructSelected(); this.owner = owner; }
        public Task<LocalIdentityLifecycleCapabilities> QueryAsync(
            GetLocalIdentityLifecycleCapabilitiesQuery query, CancellationToken cancellationToken)
        {
            owner.Executed = true;
            return Task.FromResult(new LocalIdentityLifecycleCapabilities(true, true, false));
        }
    }

    public sealed record OrdinaryQuery : IQuery<int>;
    public sealed class OrdinaryProbe : IQueryHandler<OrdinaryQuery, int>
    {
        public Task<int> QueryAsync(OrdinaryQuery query, CancellationToken cancellationToken) => Task.FromResult(7);
    }

    private sealed class SelectedScopeFactory(ServiceProvider selected) : ILocalIdentityExecutionScopeFactory
    {
        internal TaskCompletionSource Requested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal OwnerEvidence? Owner { get; private set; }

        public async ValueTask<ILocalIdentityExecutionScope> CreateAsync(CancellationToken cancellationToken)
        {
            Requested.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            var scope = selected.CreateAsyncScope();
            Owner = scope.ServiceProvider.GetRequiredService<OwnerEvidence>();
            return new SelectedScope(scope);
        }
    }

    private sealed class SelectedScope(AsyncServiceScope scope) : ILocalIdentityExecutionScope
    {
        public IServiceProvider Services => scope.ServiceProvider;
        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }
}
