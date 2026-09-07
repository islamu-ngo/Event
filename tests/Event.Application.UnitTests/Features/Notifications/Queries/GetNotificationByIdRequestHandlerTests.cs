using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Notification;
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
    private readonly INotificationRepository _notificationRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IMapper _mapper;
    private readonly GetNotificationByIdRequestHandler _handler;

    public GetNotificationByIdRequestHandlerTests()
    {
        _notificationRepository = Substitute.For<INotificationRepository>();
        _currentUserService = Substitute.For<ICurrentUserService>();
        _mapper = Substitute.For<IMapper>();

        _handler = new GetNotificationByIdRequestHandler(
            _notificationRepository,
            _currentUserService,
            _mapper);
    }

    [Test]
    public async Task Handle_WithExistingNotification_ReturnsDto()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();
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
        var expectedDto = new NotificationDto
        {
            Id = notificationId,
            UserId = userId,
            NotificationTypeId = (int)NotificationTypeEnum.EventCreated,
            Title = "New Event Created"
        };

        _notificationRepository.GetByIdForUser(notificationId, userId).Returns(notification);
        _mapper.Map<NotificationDto>(notification).Returns(expectedDto);

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
        var userId = Guid.NewGuid();
        _currentUserService.UserId.Returns(userId);

        _notificationRepository.GetByIdForUser(Arg.Any<Guid>(), userId).Returns((Notification?)null);

        var request = new GetNotificationByIdRequest(Guid.NewGuid());

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
        var request = new GetNotificationByIdRequest(Guid.NewGuid());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        await Assert.That(result).IsNull();
    }
}
