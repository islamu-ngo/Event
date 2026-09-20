using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Localization.Handlers.Queries;
using Explore.Application.Features.Localization.Requests.Queries;
using Explore.Application.Responses;
using NSubstitute;

namespace Event.Application.UnitTests.Infrastructure.Localization;

public class TestTmsConnectionQueryHandlerTests
{
    private static readonly Guid ActorId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Test]
    public async Task QueryAsync_WhenConnectionSucceeds_ReturnsSuccess()
    {
        var provider = Substitute.For<ITranslationManagementProvider>();
        provider.TestConnectionAsync(Arg.Any<CancellationToken>()).Returns(true);
        IQueryHandler<TestTmsConnectionQuery, BaseCommandResponse<Guid>> handler =
            new TestTmsConnectionQueryHandler(BuildAdminContext(), provider);

        var result = await handler.QueryAsync(new(), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Id).IsEqualTo(Guid.Empty);
    }

    [Test]
    public async Task QueryAsync_WhenConnectionFails_ReturnsValidationFailure()
    {
        var provider = Substitute.For<ITranslationManagementProvider>();
        provider.TestConnectionAsync(Arg.Any<CancellationToken>()).Returns(false);
        IQueryHandler<TestTmsConnectionQuery, BaseCommandResponse<Guid>> handler =
            new TestTmsConnectionQueryHandler(BuildAdminContext(), provider);

        var result = await handler.QueryAsync(new(), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Errors!.Count).IsEqualTo(1);
        await Assert.That(result.FailureCode).IsNull();
    }

    [Test]
    public async Task QueryAsync_WhenUserIsNotInstanceAdmin_DeniesBeforeProviderProbe()
    {
        var provider = Substitute.For<ITranslationManagementProvider>();
        provider.TestConnectionAsync(Arg.Any<CancellationToken>()).Returns(true);
        IQueryHandler<TestTmsConnectionQuery, BaseCommandResponse<Guid>> handler =
            new TestTmsConnectionQueryHandler(BuildAdminContext(isInstanceAdmin: false), provider);

        var result = await handler.QueryAsync(new(), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Errors!.Count).IsEqualTo(1);
        await provider.DidNotReceive().TestConnectionAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task QueryAsync_WhenProviderCancels_PreservesTheSuppliedToken()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        var provider = Substitute.For<ITranslationManagementProvider>();
        provider.TestConnectionAsync(source.Token).Returns(Task.FromCanceled<bool>(source.Token));
        IQueryHandler<TestTmsConnectionQuery, BaseCommandResponse<Guid>> handler =
            new TestTmsConnectionQueryHandler(BuildAdminContext(), provider);

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            handler.QueryAsync(new(), source.Token));

        await Assert.That(exception?.CancellationToken).IsEqualTo((CancellationToken?)source.Token);
    }

    [Test]
    public async Task QueryAsync_WhenCancelledDuringAdminResolution_PropagatesCancellationBeforeProviderProbe()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        var adminContext = BuildAdminContext();
        adminContext.ResolveUserIdAsync(source.Token).Returns(Task.FromCanceled<Guid?>(source.Token));
        var provider = Substitute.For<ITranslationManagementProvider>();
        IQueryHandler<TestTmsConnectionQuery, BaseCommandResponse<Guid>> handler =
            new TestTmsConnectionQueryHandler(adminContext, provider);

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            handler.QueryAsync(new(), source.Token));

        await Assert.That(exception?.CancellationToken).IsEqualTo((CancellationToken?)source.Token);
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
