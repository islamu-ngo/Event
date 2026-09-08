using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Notifications.Handlers.Queries;
using Explore.Application.Features.Notifications.Requests.Queries;
using NSubstitute;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Application.UnitTests.Features.Notifications.Queries;

public class GetUnreadCountRequestHandlerTests
{
    private readonly INotificationRepository _notificationRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly GetUnreadCountRequestHandler _handler;

    public GetUnreadCountRequestHandlerTests()
    {
        _notificationRepository = Substitute.For<INotificationRepository>();
        _currentUserService = Substitute.For<ICurrentUserService>();

        _handler = new GetUnreadCountRequestHandler(
            _notificationRepository,
            _currentUserService);
    }

    [Test]
    public async Task Handle_WithAuthenticatedUser_ReturnsCount()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _currentUserService.UserId.Returns(userId);
        _notificationRepository.GetUnreadCount(userId, null).Returns(5);

        // Act
        var result = await _handler.Handle(new GetUnreadCountRequest(), CancellationToken.None);

        // Assert
        await Assert.That(result.UnreadCount).IsEqualTo(5);
    }

    [Test]
    public async Task Handle_WithNoUser_ReturnsZero()
    {
        // Arrange
        _currentUserService.UserId.Returns((Guid?)null);

        // Act
        var result = await _handler.Handle(new GetUnreadCountRequest(), CancellationToken.None);

        // Assert
        await Assert.That(result.UnreadCount).IsEqualTo(0);
    }

    [Test]
    public async Task Handle_WithNoUnreadNotifications_ReturnsZero()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _currentUserService.UserId.Returns(userId);
        _notificationRepository.GetUnreadCount(userId, null).Returns(0);

        // Act
        var result = await _handler.Handle(new GetUnreadCountRequest(), CancellationToken.None);

        // Assert
        await Assert.That(result.UnreadCount).IsEqualTo(0);
    }
}
