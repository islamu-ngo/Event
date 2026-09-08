using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Notifications.Handlers.Commands;
using Explore.Application.Features.Notifications.Requests.Commands;
using NSubstitute;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Application.UnitTests.Features.Notifications.Commands;

public class MarkNotificationAsReadCommandHandlerTests
{
    private readonly INotificationRepository _notificationRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly MarkNotificationAsReadCommandHandler _handler;

    public MarkNotificationAsReadCommandHandlerTests()
    {
        _notificationRepository = Substitute.For<INotificationRepository>();
        _currentUserService = Substitute.For<ICurrentUserService>();

        _handler = new MarkNotificationAsReadCommandHandler(
            _notificationRepository,
            _currentUserService);
    }

    [Test]
    public async Task Handle_WithValidNotification_ReturnsSuccess()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();
        _currentUserService.UserId.Returns(userId);
        _notificationRepository.MarkAsRead(notificationId, userId).Returns(true);

        var command = new MarkNotificationAsReadCommand(notificationId);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Id).IsEqualTo(notificationId);
    }

    [Test]
    public async Task Handle_WithNonExistentNotification_ReturnsFailure()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _currentUserService.UserId.Returns(userId);
        _notificationRepository.MarkAsRead(Arg.Any<Guid>(), userId).Returns(false);

        var command = new MarkNotificationAsReadCommand(Guid.NewGuid());

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Message).IsEqualTo("Notification not found.");
    }

    [Test]
    public async Task Handle_WithNoUser_ReturnsFailure()
    {
        // Arrange
        _currentUserService.UserId.Returns((Guid?)null);
        var command = new MarkNotificationAsReadCommand(Guid.NewGuid());

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Message).IsEqualTo("User not authenticated.");
    }
}
