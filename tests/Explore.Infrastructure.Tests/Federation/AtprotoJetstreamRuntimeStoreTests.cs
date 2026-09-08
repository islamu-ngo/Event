using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Federation.Atproto.Models;
using Explore.Application.Features.Federation.Atproto.Requests.Commands;
using Explore.Infrastructure.Services.Federation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Federation;

public sealed class AtprotoJetstreamRuntimeStoreTests
{
    [Test]
    public async Task ReconcilePdsSnapshotsDispatchesExactCommandAndCancellationThroughFreshAsyncScope()
    {
        IMediator mediator = Substitute.For<IMediator>();
        IAtprotoDiscoveryCacheInvalidator invalidator = Substitute.For<IAtprotoDiscoveryCacheInvalidator>();
        var command = RecoveryCommand();
        using var cancellation = new CancellationTokenSource();
        var expected = new AtprotoPdsRecoveryResult(AtprotoPdsRecoveryOutcome.Unchanged, new string('a', 64));
        mediator.Send(command, cancellation.Token).Returns(expected);
        IAsyncDisposable scopeProbe = Substitute.For<IAsyncDisposable>();
        (AtprotoJetstreamRuntimeStore Store, ServiceProvider Provider) fixture =
            CreateRecoveryStore(mediator, invalidator, scopeProbe);
        await using ServiceProvider provider = fixture.Provider;

        AtprotoPdsRecoveryResult result = await fixture.Store.ReconcilePdsSnapshotsAsync(
            command,
            cancellation.Token);

        await Assert.That(result).IsEqualTo(expected);
        await mediator.Received(1).Send(
            Arg.Is<ReconcileAtprotoPdsSnapshotsCommand>(actual => ReferenceEquals(actual, command)),
            cancellation.Token);
        await scopeProbe.Received(1).DisposeAsync();
    }

    [Test]
    public async Task CompletedRecoveryInvalidatesDiscoveryCacheOnceAfterMediatorCompletes()
    {
        IMediator mediator = Substitute.For<IMediator>();
        IAtprotoDiscoveryCacheInvalidator invalidator = Substitute.For<IAtprotoDiscoveryCacheInvalidator>();
        var command = RecoveryCommand();
        using var cancellation = new CancellationTokenSource();
        var completion = new TaskCompletionSource<AtprotoPdsRecoveryResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        mediator.Send(command, cancellation.Token).Returns(completion.Task);
        (AtprotoJetstreamRuntimeStore Store, ServiceProvider Provider) fixture =
            CreateRecoveryStore(mediator, invalidator);
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
        IMediator mediator = Substitute.For<IMediator>();
        IAtprotoDiscoveryCacheInvalidator invalidator = Substitute.For<IAtprotoDiscoveryCacheInvalidator>();
        var command = RecoveryCommand();
        var expected = new AtprotoPdsRecoveryResult(outcome, new string('d', 64));
        mediator.Send(command, CancellationToken.None).Returns(expected);
        (AtprotoJetstreamRuntimeStore Store, ServiceProvider Provider) fixture =
            CreateRecoveryStore(mediator, invalidator);
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
        IMediator mediator = Substitute.For<IMediator>();
        var command = RecoveryCommand();
        var expected = new AtprotoPdsRecoveryResult(
            AtprotoPdsRecoveryOutcome.Completed,
            new string('e', 64));
        mediator.Send(command, CancellationToken.None).Returns(expected);
        (AtprotoJetstreamRuntimeStore Store, ServiceProvider Provider) fixture =
            CreateRecoveryStore(mediator);
        await using ServiceProvider provider = fixture.Provider;

        AtprotoPdsRecoveryResult result = await fixture.Store.ReconcilePdsSnapshotsAsync(
            command,
            CancellationToken.None);

        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task MediatorFailureDoesNotInvalidateDiscoveryCache()
    {
        IMediator mediator = Substitute.For<IMediator>();
        IAtprotoDiscoveryCacheInvalidator invalidator = Substitute.For<IAtprotoDiscoveryCacheInvalidator>();
        var command = RecoveryCommand();
        var expected = new InvalidOperationException("simulated_mediator_failure");
        mediator.Send(command, CancellationToken.None)
            .Returns(Task.FromException<AtprotoPdsRecoveryResult>(expected));
        (AtprotoJetstreamRuntimeStore Store, ServiceProvider Provider) fixture =
            CreateRecoveryStore(mediator, invalidator);
        await using ServiceProvider provider = fixture.Provider;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Store.ReconcilePdsSnapshotsAsync(command, CancellationToken.None));

        await Assert.That(exception).IsSameReferenceAs(expected);
        await invalidator.DidNotReceiveWithAnyArgs().InvalidateAsync(default);
    }

    [Test]
    public async Task CanceledMediatorSendDoesNotInvalidateDiscoveryCache()
    {
        IMediator mediator = Substitute.For<IMediator>();
        IAtprotoDiscoveryCacheInvalidator invalidator = Substitute.For<IAtprotoDiscoveryCacheInvalidator>();
        var command = RecoveryCommand();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        mediator.Send(command, cancellation.Token)
            .Returns(Task.FromCanceled<AtprotoPdsRecoveryResult>(cancellation.Token));
        (AtprotoJetstreamRuntimeStore Store, ServiceProvider Provider) fixture =
            CreateRecoveryStore(mediator, invalidator);
        await using ServiceProvider provider = fixture.Provider;

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => fixture.Store.ReconcilePdsSnapshotsAsync(command, cancellation.Token));

        await mediator.Received(1).Send(command, cancellation.Token);
        await invalidator.DidNotReceiveWithAnyArgs().InvalidateAsync(default);
    }

    [Test]
    public async Task ApplyDispatchesImportCommandWithExactRequestCancellationAndResult()
    {
        IMediator mediator = Substitute.For<IMediator>();
        IAtprotoJetstreamRepository repository = Substitute.For<IAtprotoJetstreamRepository>();
        IAtprotoDiscoveryCacheInvalidator invalidator = Substitute.For<IAtprotoDiscoveryCacheInvalidator>();
        AtprotoJetstreamApplyRequest request = Request(affectsDiscovery: false);
        using var cancellation = new CancellationTokenSource();
        mediator.Send(
                Arg.Any<ImportAtprotoFederatedEventCommand>(),
                cancellation.Token)
            .Returns(true);
        repository.TryApplyAndAdvanceAsync(request, cancellation.Token).Returns(false);
        var services = new ServiceCollection();
        services.AddScoped(_ => mediator);
        services.AddScoped(_ => repository);
        services.AddScoped(_ => invalidator);
        await using ServiceProvider provider = services.BuildServiceProvider();
        var store = new AtprotoJetstreamRuntimeStore(
            provider.GetRequiredService<IServiceScopeFactory>());

        await Assert.That(request.EventImports).IsEmpty();
        bool applied = await store.TryApplyAndAdvanceAsync(request, cancellation.Token);

        await Assert.That(applied).IsTrue();
        await mediator.Received(1).Send(
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
        IMediator mediator = Substitute.For<IMediator>();
        IAtprotoDiscoveryCacheInvalidator invalidator = Substitute.For<IAtprotoDiscoveryCacheInvalidator>();
        mediator.Send(Arg.Any<ImportAtprotoFederatedEventCommand>(), Arg.Any<CancellationToken>())
            .Returns(true);
        AtprotoJetstreamRuntimeStore store = CreateStore(mediator, invalidator);

        bool applied = await store.TryApplyAndAdvanceAsync(Request(affectsDiscovery: true), CancellationToken.None);

        await Assert.That(applied).IsTrue();
        await invalidator.Received(1).InvalidateAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RejectedApplyDoesNotInvalidateDiscoveryCache()
    {
        IMediator mediator = Substitute.For<IMediator>();
        IAtprotoDiscoveryCacheInvalidator invalidator = Substitute.For<IAtprotoDiscoveryCacheInvalidator>();
        mediator.Send(Arg.Any<ImportAtprotoFederatedEventCommand>(), Arg.Any<CancellationToken>())
            .Returns(false);
        AtprotoJetstreamRuntimeStore store = CreateStore(mediator, invalidator);

        bool applied = await store.TryApplyAndAdvanceAsync(Request(affectsDiscovery: true), CancellationToken.None);

        await Assert.That(applied).IsFalse();
        await invalidator.DidNotReceiveWithAnyArgs().InvalidateAsync(default);
    }

    [Test]
    public async Task SuccessfulUnrelatedApplyDoesNotInvalidateDiscoveryCache()
    {
        IMediator mediator = Substitute.For<IMediator>();
        IAtprotoDiscoveryCacheInvalidator invalidator = Substitute.For<IAtprotoDiscoveryCacheInvalidator>();
        mediator.Send(Arg.Any<ImportAtprotoFederatedEventCommand>(), Arg.Any<CancellationToken>())
            .Returns(true);
        AtprotoJetstreamRuntimeStore store = CreateStore(mediator, invalidator);

        bool applied = await store.TryApplyAndAdvanceAsync(Request(affectsDiscovery: false), CancellationToken.None);

        await Assert.That(applied).IsTrue();
        await invalidator.DidNotReceiveWithAnyArgs().InvalidateAsync(default);
    }

    [Test]
    public async Task CanceledApplyCommandDoesNotInvalidateDiscoveryCache()
    {
        IMediator mediator = Substitute.For<IMediator>();
        IAtprotoDiscoveryCacheInvalidator invalidator = Substitute.For<IAtprotoDiscoveryCacheInvalidator>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        mediator.Send(
                Arg.Any<ImportAtprotoFederatedEventCommand>(),
                cancellation.Token)
            .Returns(Task.FromCanceled<bool>(cancellation.Token));
        AtprotoJetstreamRuntimeStore store = CreateStore(mediator, invalidator);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => store.TryApplyAndAdvanceAsync(Request(affectsDiscovery: true), cancellation.Token));

        await mediator.Received(1).Send(
            Arg.Any<ImportAtprotoFederatedEventCommand>(),
            cancellation.Token);
        await invalidator.DidNotReceiveWithAnyArgs().InvalidateAsync(default);
    }

    private static AtprotoJetstreamRuntimeStore CreateStore(
        IMediator mediator,
        IAtprotoDiscoveryCacheInvalidator invalidator)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => mediator);
        services.AddScoped(_ => invalidator);
        ServiceProvider provider = services.BuildServiceProvider();
        return new AtprotoJetstreamRuntimeStore(provider.GetRequiredService<IServiceScopeFactory>());
    }

    private static (AtprotoJetstreamRuntimeStore Store, ServiceProvider Provider) CreateRecoveryStore(
        IMediator mediator,
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

        services.AddScoped<IMediator>(provider =>
        {
            _ = provider.GetService<IAsyncDisposable>();
            return mediator;
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
