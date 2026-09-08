using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Event.Application.UnitTests.Services;

public sealed class NotificationRefreshStreamServiceTests
{
    private readonly ICurrentUserService _currentUserService = Substitute.For<ICurrentUserService>();
    private readonly INotificationRepository _notificationRepository = Substitute.For<INotificationRepository>();
    private readonly NotificationRefreshStreamService _service;

    public NotificationRefreshStreamServiceTests()
    {
        _service = new NotificationRefreshStreamService(
            _currentUserService,
            _notificationRepository,
            Substitute.For<ILogger<NotificationRefreshStreamService>>());
    }

    [Test]
    public async Task StreamAsync_WithAuthenticatedUser_YieldsInitialUnreadCountHint()
    {
        var userId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        _currentUserService.IsAuthenticated.Returns(true);
        _currentUserService.UserId.Returns(userId);
        _notificationRepository.GetUnreadCount(userId).Returns(3);

        await using var enumerator = _service.StreamAsync(cancellation.Token).GetAsyncEnumerator(cancellation.Token);
        var hasHint = await enumerator.MoveNextAsync();
        await cancellation.CancelAsync();

        await Assert.That(hasHint).IsTrue();
        await Assert.That(enumerator.Current.UnreadCount).IsEqualTo(3);
        await Assert.That(enumerator.Current.HasUnread).IsTrue();
        await Assert.That(enumerator.Current.Reason).IsEqualTo(NotificationRefreshStreamService.InitialReason);
    }

    [Test]
    public async Task StreamAsync_WithoutAuthenticatedUser_YieldsNoHints()
    {
        _currentUserService.IsAuthenticated.Returns(false);
        _currentUserService.UserId.Returns((Guid?)null);

        await using var enumerator = _service.StreamAsync().GetAsyncEnumerator();
        var hasHint = await enumerator.MoveNextAsync();

        await Assert.That(hasHint).IsFalse();
        _notificationRepository.DidNotReceiveWithAnyArgs().GetUnreadCount(default);
    }
}
