using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.InstanceOnboarding.Handlers.Queries;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Explore.Application.Models;
using NSubstitute;

namespace Event.Application.UnitTests.Features.InstanceOnboarding.Queries;

public sealed class TestInstanceSmtpConnectionQueryHandlerTests
{
    [Test]
    public async Task Handle_WhenConnectionSucceeds_PropagatesResultAndCancellationToken()
    {
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;
        var connectionTester = Substitute.For<IEmailConnectionTester>();
        connectionTester.TestConnectionAsync(cancellationToken)
            .Returns(EmailResult.Ok("Provider accepted the connection.", TimeSpan.FromMilliseconds(17)));

        var result = await new TestInstanceSmtpConnectionQueryHandler(connectionTester)
            .Handle(new TestInstanceSmtpConnectionQuery(), cancellationToken);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Message).IsEqualTo("Provider accepted the connection.");
        await Assert.That(result.ErrorMessage).IsNull();
        await Assert.That(result.Duration).IsEqualTo(TimeSpan.FromMilliseconds(17));
        await connectionTester.Received(1).TestConnectionAsync(cancellationToken);
    }

    [Test]
    public async Task Handle_WhenConnectionFails_PropagatesSafeFailureResult()
    {
        using var cancellationSource = new CancellationTokenSource();
        var connectionTester = Substitute.For<IEmailConnectionTester>();
        connectionTester.TestConnectionAsync(cancellationSource.Token)
            .Returns(EmailResult.Fail("Provider rejected the credentials.", TimeSpan.FromMilliseconds(29)));

        var result = await new TestInstanceSmtpConnectionQueryHandler(connectionTester)
            .Handle(new TestInstanceSmtpConnectionQuery(), cancellationSource.Token);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Message).IsNull();
        await Assert.That(result.ErrorMessage).IsEqualTo("Provider rejected the credentials.");
        await Assert.That(result.Duration).IsEqualTo(TimeSpan.FromMilliseconds(29));
        await connectionTester.Received(1).TestConnectionAsync(cancellationSource.Token);
    }
}
