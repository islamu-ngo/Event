using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Federation.Atproto.Models;
using Explore.Application.Features.Federation.Atproto.Requests.Commands;
using Explore.Infrastructure.Services.Federation;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Federation;

public sealed class AtprotoJetstreamRuntimeStoreTests
{
    [Test]
    public async Task ReconcilePdsSnapshotsDispatchesExactCommandAndCancellationThroughFreshAsyncScope()
    {
        var handler = Substitute.For<ICommandHandler<ReconcileAtprotoPdsSnapshotsCommand, AtprotoPdsRecoveryResult>>();
        IAtprotoDiscoveryCacheInvalidator invalidator = Substitute.For<IAtprotoDiscoveryCacheInvalidator>();
        var command = RecoveryCommand();
        using var cancellation = new CancellationTokenSource();
        var expected = new AtprotoPdsRecoveryResult(AtprotoPdsRecoveryOutcome.Unchanged, new string('a', 64));
        handler.ExecuteAsync(command, cancellation.Token).Returns(expected);
        IAsyncDisposable scopeProbe = Substitute.For<IAsyncDisposable>();
        (AtprotoJetstreamRuntimeStore Store, ServiceProvider Provider) fixture =
            CreateRecoveryStore(handler, invalidator, scopeProbe);
        await using ServiceProvider provider = fixture.Provider;

        AtprotoPdsRecoveryResult result = await fixture.Store.ReconcilePdsSnapshotsAsync(
            command,
            cancellation.Token);

        await Assert.That(result).IsEqualTo(expected);
        await handler.Received(1).ExecuteAsync(
            Arg.Is<ReconcileAtprotoPdsSnapshotsCommand>(actual => ReferenceEquals(actual, command)),
            cancellation.Token);
        await scopeProbe.Received(1).DisposeAsync();
    }

    [Test]
    public async Task CompletedRecoveryInvalidatesDiscoveryCacheOnceAfterMediatorCompletes()
    {
        var handler = Substitute.For<ICommandHandler<ReconcileAtprotoPdsSnapshotsCommand, AtprotoPdsRecoveryResult>>();
        IAtprotoDiscoveryCacheInvalidator invalidator = Substitute.For<IAtprotoDiscoveryCacheInvalidator>();
        var command = RecoveryCommand();
        using var cancellation = new CancellationTokenSource();
        var completion = new TaskCompletionSource<AtprotoPdsRecoveryResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        handler.ExecuteAsync(command, cancellation.Token).Returns(completion.Task);
        (AtprotoJetstreamRuntimeStore Store, ServiceProvider Provider) fixture =
            CreateRecoveryStore(handler, invalidator);
        await using ServiceProvider provider = fixture.Provider;

        Task<AtprotoPdsRecoveryResult> recovery = fixture.Store.ReconcilePdsSnapshotsAsync(
            command,
            cancellation.Token);
        await invalidator.DidNotReceiveWithAnyArgs().InvalidateAsync(default);
        var expected = new AtprotoPdsRecoveryResult(
            AtprotoPdsRecoveryOutcome.Completed,
            new string('c', 64),
            AppliedDids: 1);
        completion.SetResult(expected);

        AtprotoPdsRecoveryResult result = await recovery;

        await Assert.That(result).IsEqualTo(expected);
        await invalidator.Received(1).InvalidateAsync(cancellation.Token);
    }

    [Test]
    [Arguments(AtprotoPdsRecoveryOutcome.Disabled)]
    [Arguments(AtprotoPdsRecoveryOutcome.DowntimeOnly)]
    [Arguments(AtprotoPdsRecoveryOutcome.ScopeRejected)]
    [Arguments(AtprotoPdsRecoveryOutcome.Unchanged)]
    [Arguments(AtprotoPdsRecoveryOutcome.PartialFailure)]
    [Arguments(AtprotoPdsRecoveryOutcome.FenceRejected)]
    public async Task NonCompletedRecoveryDoesNotInvalidateDiscoveryCache(
        AtprotoPdsRecoveryOutcome outcome)
    {
        var handler = Substitute.For<ICommandHandler<ReconcileAtprotoPdsSnapshotsCommand, AtprotoPdsRecoveryResult>>();
        IAtprotoDiscoveryCacheInvalidator invalidator = Substitute.For<IAtprotoDiscoveryCacheInvalidator>();
        var command = RecoveryCommand();
        var expected = new AtprotoPdsRecoveryResult(outcome, new string('d', 64));
        handler.ExecuteAsync(command, CancellationToken.None).Returns(expected);
        (AtprotoJetstreamRuntimeStore Store, ServiceProvider Provider) fixture =
            CreateRecoveryStore(handler, invalidator);
        await using ServiceProvider provider = fixture.Provider;

        AtprotoPdsRecoveryResult result = await fixture.Store.ReconcilePdsSnapshotsAsync(
            command,
            CancellationToken.None);

        await Assert.That(result).IsEqualTo(expected);
        await invalidator.DidNotReceiveWithAnyArgs().InvalidateAsync(default);
    }

    [Test]
    public async Task CompletedRecoveryWithoutRegisteredInvalidatorReturnsResult()
    {
        var handler = Substitute.For<ICommandHandler<ReconcileAtprotoPdsSnapshotsCommand, AtprotoPdsRecoveryResult>>();
        var command = RecoveryCommand();
        var expected = new AtprotoPdsRecoveryResult(
            AtprotoPdsRecoveryOutcome.Completed,
            new string('e', 64));
        handler.ExecuteAsync(command, CancellationToken.None).Returns(expected);
        (AtprotoJetstreamRuntimeStore Store, ServiceProvider Provider) fixture =
            CreateRecoveryStore(handler);
        await using ServiceProvider provider = fixture.Provider;

        AtprotoPdsRecoveryResult result = await fixture.Store.ReconcilePdsSnapshotsAsync(
            command,
            CancellationToken.None);

        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task MediatorFailureDoesNotInvalidateDiscoveryCache()
    {
        var handler = Substitute.For<ICommandHandler<ReconcileAtprotoPdsSnapshotsCommand, AtprotoPdsRecoveryResult>>();
        IAtprotoDiscoveryCacheInvalidator invalidator = Substitute.For<IAtprotoDiscoveryCacheInvalidator>();
        var command = RecoveryCommand();
        var expected = new InvalidOperationException("simulated_mediator_failure");
        handler.ExecuteAsync(command, CancellationToken.None)
            .Returns(Task.FromException<AtprotoPdsRecoveryResult>(expected));
        (AtprotoJetstreamRuntimeStore Store, ServiceProvider Provider) fixture =
            CreateRecoveryStore(handler, invalidator);
        await using ServiceProvider provider = fixture.Provider;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Store.ReconcilePdsSnapshotsAsync(command, CancellationToken.None));

        await Assert.That(exception).IsSameReferenceAs(expected);
        await invalidator.DidNotReceiveWithAnyArgs().InvalidateAsync(default);
    }

    [Test]
    public async Task CanceledMediatorSendDoesNotInvalidateDiscoveryCache()
    {
        var handler = Substitute.For<ICommandHandler<ReconcileAtprotoPdsSnapshotsCommand, AtprotoPdsRecoveryResult>>();
        IAtprotoDiscoveryCacheInvalidator invalidator = Substitute.For<IAtprotoDiscoveryCacheInvalidator>();
        var command = RecoveryCommand();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        handler.ExecuteAsync(command, cancellation.Token)
            .Returns(Task.FromCanceled<AtprotoPdsRecoveryResult>(cancellation.Token));
        (AtprotoJetstreamRuntimeStore Store, ServiceProvider Provider) fixture =
            CreateRecoveryStore(handler, invalidator);
        await using ServiceProvider provider = fixture.Provider;

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => fixture.Store.ReconcilePdsSnapshotsAsync(command, cancellation.Token));

        await handler.Received(1).ExecuteAsync(command, cancellation.Token);
        await invalidator.DidNotReceiveWithAnyArgs().InvalidateAsync(default);
    }

    [Test]
    public async Task ApplyDispatchesImportCommandWithExactRequestCancellationAndResult()
    {
        var handler = Substitute.For<ICommandHandler<ImportAtprotoFederatedEventCommand, bool>>();
        IAtprotoJetstreamRepository repository = Substitute.For<IAtprotoJetstreamRepository>();
        IAtprotoDiscoveryCacheInvalidator invalidator = Substitute.For<IAtprotoDiscoveryCacheInvalidator>();
        AtprotoJetstreamApplyRequest request = Request(affectsDiscovery: false);
        using var cancellation = new CancellationTokenSource();
        handler.ExecuteAsync(
                Arg.Any<ImportAtprotoFederatedEventCommand>(),
                cancellation.Token)
            .Returns(true);
        repository.TryApplyAndAdvanceAsync(request, cancellation.Token).Returns(false);
        var services = new ServiceCollection();
        services.AddScoped(_ => handler);
        services.AddScoped(_ => repository);
        services.AddScoped(_ => invalidator);
        await using ServiceProvider provider = services.BuildServiceProvider();
        var store = new AtprotoJetstreamRuntimeStore(
            provider.GetRequiredService<IServiceScopeFactory>());

        await Assert.That(request.EventImports).IsEmpty();
        bool applied = await store.TryApplyAndAdvanceAsync(request, cancellation.Token);

        await Assert.That(applied).IsTrue();
        await handler.Received(1).ExecuteAsync(
            Arg.Is<ImportAtprotoFederatedEventCommand>(
                command => command != null
                    && ReferenceEquals(command.ApplyRequest, request)),
            cancellation.Token);
        await repository.DidNotReceiveWithAnyArgs()
            .TryApplyAndAdvanceAsync(default!, default);
    }

    [Test]
    public async Task SuccessfulApplyInvalidatesDiscoveryCacheAfterImportCommandCompletes()
    {
        var handler = Substitute.For<ICommandHandler<ImportAtprotoFederatedEventCommand, bool>>();
        IAtprotoDiscoveryCacheInvalidator invalidator = Substitute.For<IAtprotoDiscoveryCacheInvalidator>();
        handler.ExecuteAsync(Arg.Any<ImportAtprotoFederatedEventCommand>(), Arg.Any<CancellationToken>())
            .Returns(true);
        AtprotoJetstreamRuntimeStore store = CreateStore(handler, invalidator);

        bool applied = await store.TryApplyAndAdvanceAsync(Request(affectsDiscovery: true), CancellationToken.None);

        await Assert.That(applied).IsTrue();
        await invalidator.Received(1).InvalidateAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RejectedApplyDoesNotInvalidateDiscoveryCache()
    {
        var handler = Substitute.For<ICommandHandler<ImportAtprotoFederatedEventCommand, bool>>();
        IAtprotoDiscoveryCacheInvalidator invalidator = Substitute.For<IAtprotoDiscoveryCacheInvalidator>();
        handler.ExecuteAsync(Arg.Any<ImportAtprotoFederatedEventCommand>(), Arg.Any<CancellationToken>())
            .Returns(false);
        AtprotoJetstreamRuntimeStore store = CreateStore(handler, invalidator);

        bool applied = await store.TryApplyAndAdvanceAsync(Request(affectsDiscovery: true), CancellationToken.None);

        await Assert.That(applied).IsFalse();
        await invalidator.DidNotReceiveWithAnyArgs().InvalidateAsync(default);
    }

    [Test]
    public async Task SuccessfulUnrelatedApplyDoesNotInvalidateDiscoveryCache()
    {
        var handler = Substitute.For<ICommandHandler<ImportAtprotoFederatedEventCommand, bool>>();
        IAtprotoDiscoveryCacheInvalidator invalidator = Substitute.For<IAtprotoDiscoveryCacheInvalidator>();
        handler.ExecuteAsync(Arg.Any<ImportAtprotoFederatedEventCommand>(), Arg.Any<CancellationToken>())
            .Returns(true);
        AtprotoJetstreamRuntimeStore store = CreateStore(handler, invalidator);

        bool applied = await store.TryApplyAndAdvanceAsync(Request(affectsDiscovery: false), CancellationToken.None);

        await Assert.That(applied).IsTrue();
        await invalidator.DidNotReceiveWithAnyArgs().InvalidateAsync(default);
    }

    [Test]
    public async Task CanceledApplyCommandDoesNotInvalidateDiscoveryCache()
    {
        var handler = Substitute.For<ICommandHandler<ImportAtprotoFederatedEventCommand, bool>>();
        IAtprotoDiscoveryCacheInvalidator invalidator = Substitute.For<IAtprotoDiscoveryCacheInvalidator>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        handler.ExecuteAsync(
                Arg.Any<ImportAtprotoFederatedEventCommand>(),
                cancellation.Token)
            .Returns(Task.FromCanceled<bool>(cancellation.Token));
        AtprotoJetstreamRuntimeStore store = CreateStore(handler, invalidator);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => store.TryApplyAndAdvanceAsync(Request(affectsDiscovery: true), cancellation.Token));

        await handler.Received(1).ExecuteAsync(
            Arg.Any<ImportAtprotoFederatedEventCommand>(),
            cancellation.Token);
        await invalidator.DidNotReceiveWithAnyArgs().InvalidateAsync(default);
    }

    private static AtprotoJetstreamRuntimeStore CreateStore(
        ICommandHandler<ImportAtprotoFederatedEventCommand, bool> handler,
        IAtprotoDiscoveryCacheInvalidator invalidator)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => handler);
        services.AddScoped(_ => invalidator);
        ServiceProvider provider = services.BuildServiceProvider();
        return new AtprotoJetstreamRuntimeStore(provider.GetRequiredService<IServiceScopeFactory>());
    }

    private static (AtprotoJetstreamRuntimeStore Store, ServiceProvider Provider) CreateRecoveryStore(
        ICommandHandler<ReconcileAtprotoPdsSnapshotsCommand, AtprotoPdsRecoveryResult> handler,
        IAtprotoDiscoveryCacheInvalidator? invalidator = null,
        IAsyncDisposable? scopeProbe = null)
    {
        var services = new ServiceCollection();
        if (invalidator is not null)
        {
            services.AddScoped(_ => invalidator);
        }

        if (scopeProbe is not null)
        {
            services.AddScoped(_ => scopeProbe);
        }

        services.AddScoped<ICommandHandler<ReconcileAtprotoPdsSnapshotsCommand, AtprotoPdsRecoveryResult>>(provider =>
        {
            _ = provider.GetService<IAsyncDisposable>();
            return handler;
        });
        ServiceProvider provider = services.BuildServiceProvider();
        return (
            new AtprotoJetstreamRuntimeStore(provider.GetRequiredService<IServiceScopeFactory>()),
            provider);
    }

    private static ReconcileAtprotoPdsSnapshotsCommand RecoveryCommand() =>
        new(
            new AtprotoJetstreamClaim(
                Guid.CreateVersion7(),
                "wss://jetstream.example/subscribe",
                41,
                Guid.CreateVersion7(),
                7),
            ["did:plc:recovery"],
            DateTime.UtcNow,
            new string('b', 64));

    private static AtprotoJetstreamApplyRequest Request(bool affectsDiscovery)
    {
        var claim = new AtprotoJetstreamClaim(
            Guid.CreateVersion7(),
            "wss://jetstream.example/subscribe",
            41,
            Guid.CreateVersion7(),
            7);
        return new AtprotoJetstreamApplyRequest(
            claim,
            41,
            42,
            null,
            [],
            null,
            DateTime.UtcNow,
            EventProjectionInvalidation: affectsDiscovery
                ? new AtprotoEventProjectionInvalidation(
                    "did:plc:discovery",
                    AtprotoJetstreamConstants.EventCollection,
                    "event-record",
                    42)
                : null);
    }

}
