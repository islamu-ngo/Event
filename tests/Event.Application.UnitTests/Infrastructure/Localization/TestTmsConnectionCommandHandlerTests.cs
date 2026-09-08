using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.Localization.Handlers.Commands;
using Explore.Application.Features.Localization.Requests.Commands;
using NSubstitute;

namespace Event.Application.UnitTests.Infrastructure.Localization;

public class TestTmsConnectionCommandHandlerTests
{
    private static readonly Guid ActorId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Test]
    public async Task Handle_WhenConnectionSucceeds_ReturnsSuccess()
    {
        var adminContext = BuildAdminContext();
        var provider = Substitute.For<ITranslationManagementProvider>();
        provider.TestConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var handler = new TestTmsConnectionCommandHandler(adminContext, provider);

        var result = await handler.Handle(new TestTmsConnectionCommand(), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Message).Contains("successful");
    }

    [Test]
    public async Task Handle_WhenConnectionFails_ReturnsFailure()
    {
        var adminContext = BuildAdminContext();
        var provider = Substitute.For<ITranslationManagementProvider>();
        provider.TestConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        var handler = new TestTmsConnectionCommandHandler(adminContext, provider);

        var result = await handler.Handle(new TestTmsConnectionCommand(), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Message).Contains("failed");
    }

    [Test]
    public async Task Handle_WhenUserIsNotInstanceAdmin_DeniesBeforeProviderProbe()
    {
        var adminContext = BuildAdminContext(isInstanceAdmin: false);
        var provider = Substitute.For<ITranslationManagementProvider>();
        provider.TestConnectionAsync(Arg.Any<CancellationToken>()).Returns(true);
        var handler = new TestTmsConnectionCommandHandler(adminContext, provider);

        var result = await handler.Handle(new TestTmsConnectionCommand(), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Message).Contains("Instance administrator");
        await provider.DidNotReceive().TestConnectionAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Handle_WhenUserIsInstanceAdmin_ProbesProviderOnce()
    {
        var adminContext = BuildAdminContext();
        var provider = Substitute.For<ITranslationManagementProvider>();
        provider.TestConnectionAsync(Arg.Any<CancellationToken>()).Returns(true);
        var handler = new TestTmsConnectionCommandHandler(adminContext, provider);

        var result = await handler.Handle(new TestTmsConnectionCommand(), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await provider.Received(1).TestConnectionAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Handle_WhenCancelledDuringAdminResolution_PropagatesCancellationBeforeProviderProbe()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        var adminContext = BuildAdminContext();
        adminContext.ResolveUserIdAsync(source.Token)
            .Returns(Task.FromCanceled<Guid?>(source.Token));
        var provider = Substitute.For<ITranslationManagementProvider>();
        var handler = new TestTmsConnectionCommandHandler(adminContext, provider);

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            handler.Handle(new TestTmsConnectionCommand(), source.Token));

        await Assert.That(exception.CancellationToken).IsEqualTo(source.Token);
        await adminContext.Received(1).ResolveUserIdAsync(source.Token);
        await adminContext.DidNotReceive().IsInstanceAdminAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await provider.DidNotReceive().TestConnectionAsync(Arg.Any<CancellationToken>());
    }

    private static IAdminContext BuildAdminContext(bool isInstanceAdmin = true)
    {
        var adminContext = Substitute.For<IAdminContext>();
        adminContext.ResolveUserIdAsync(Arg.Any<CancellationToken>()).Returns(ActorId);
        adminContext.IsInstanceAdminAsync(ActorId, Arg.Any<CancellationToken>()).Returns(isInstanceAdmin);
        return adminContext;
    }
}
