using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Notifications.Handlers.Queries;
using Explore.Application.Features.Notifications.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using NSubstitute;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Application.UnitTests.Features.Notifications.Queries;

public class GetNotificationByIdRequestHandlerTests
{
    private static readonly Guid UserId = Guid.Parse("01910000-0000-7000-8000-000000000002");
    private static readonly Guid NotificationId = Guid.Parse("01910000-0000-7000-8000-000000000001");
    private readonly INotificationRepository _notificationRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly GetNotificationByIdRequestHandler _handler;

    public GetNotificationByIdRequestHandlerTests()
    {
        _notificationRepository = Substitute.For<INotificationRepository>();
        _currentUserService = Substitute.For<ICurrentUserService>();

        _handler = new GetNotificationByIdRequestHandler(
            _notificationRepository,
            _currentUserService);
    }

    [Test]
    public async Task Handle_WithExistingNotification_ReturnsDto()
    {
        // Arrange
        var userId = UserId;
        var notificationId = NotificationId;
        _currentUserService.UserId.Returns(userId);

        var notification = new Notification
        {
            Id = notificationId,
            UserId = userId,
            NotificationTypeId = (int)NotificationTypeEnum.EventCreated,
            NotificationScopeId = (int)ActorTypeEnum.User,
            Title = "New Event Created",
            DeduplicationKey = "get-notification-by-id-test",
            User = null!,
            Tenant = null!,
            NotificationType = null!,
            NotificationScope = null!
        };
        _notificationRepository.GetByIdForUser(notificationId, userId).Returns(notification);

        var request = new GetNotificationByIdRequest(notificationId);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Id).IsEqualTo(notificationId);
        await Assert.That(result.Title).IsEqualTo("New Event Created");
    }

    [Test]
    public async Task Handle_WithNonExistentNotification_ReturnsNull()
    {
        // Arrange
        var userId = UserId;
        _currentUserService.UserId.Returns(userId);

        _notificationRepository.GetByIdForUser(Arg.Any<Guid>(), userId).Returns((Notification?)null);

        var request = new GetNotificationByIdRequest(NotificationId);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Handle_WithNoUser_ReturnsNull()
    {
        // Arrange
        _currentUserService.UserId.Returns((Guid?)null);
        var request = new GetNotificationByIdRequest(NotificationId);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        await Assert.That(result).IsNull();
    }
}
