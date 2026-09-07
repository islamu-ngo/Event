using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Notifications.Handlers.Commands;
using Explore.Application.Features.Notifications.Requests.Commands;
using NSubstitute;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Application.UnitTests.Features.Notifications.Commands;

public class MarkAllNotificationsAsReadCommandHandlerTests
{
    private readonly INotificationRepository _notificationRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly MarkAllNotificationsAsReadCommandHandler _handler;

    public MarkAllNotificationsAsReadCommandHandlerTests()
    {
        _notificationRepository = Substitute.For<INotificationRepository>();
        _currentUserService = Substitute.For<ICurrentUserService>();

        _handler = new MarkAllNotificationsAsReadCommandHandler(
            _notificationRepository,
            _currentUserService);
    }

    [Test]
    public async Task Handle_WithUnreadNotifications_MarksAllAndReturnsCount()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _currentUserService.UserId.Returns(userId);
        _notificationRepository.MarkAllAsRead(userId, Arg.Any<DateTime>()).Returns(10);

        // Act
        var result = await _handler.Handle(new MarkAllNotificationsAsReadCommand(), CancellationToken.None);

        // Assert
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Message).Contains("10");
    }

    [Test]
    public async Task Handle_WithNoUnreadNotifications_ReturnsZeroCount()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _currentUserService.UserId.Returns(userId);
        _notificationRepository.MarkAllAsRead(userId, Arg.Any<DateTime>()).Returns(0);

        // Act
        var result = await _handler.Handle(new MarkAllNotificationsAsReadCommand(), CancellationToken.None);

        // Assert
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Message).Contains("0");
    }

    [Test]
    public async Task Handle_WithNoUser_ReturnsFailure()
    {
        // Arrange
        _currentUserService.UserId.Returns((Guid?)null);

        // Act
        var result = await _handler.Handle(new MarkAllNotificationsAsReadCommand(), CancellationToken.None);

        // Assert
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Message).IsEqualTo("User not authenticated.");
    }
}
