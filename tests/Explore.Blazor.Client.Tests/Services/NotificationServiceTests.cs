namespace Explore.Blazor.Client.Tests.Services;

/// <summary>
/// Tests NotificationService across eight areas:
/// 1. GetNotificationsAsync (success, maps fields, empty on API error, empty on general error, null items, new filter params)
/// 2. GetNotificationByIdAsync (success, null on 404, null on API error, null on general error)
/// 3. GetUnreadCountAsync (success, with scope, zero on API error, zero on general error)
/// 4. MarkAsReadAsync (success, false on API error, false on general error)
/// 5. MarkAllAsReadAsync (success, false on API error, false on general error)
/// 6. DeleteAsync (success, false on API error, false on general error)
/// 7. ArchiveAsync (success, unarchive, false on API error, false on general error)
/// 8. SnoozeAsync (success, false on API error, false on general error)
/// </summary>
public class NotificationServiceTests
{
    private readonly INotificationClient _apiClient;
    private readonly ILogger<NotificationService> _logger;
    private readonly NotificationService _service;

    public NotificationServiceTests()
    {
        _apiClient = Substitute.For<INotificationClient>();
        _logger = Substitute.For<ILogger<NotificationService>>();
        _service = new NotificationService(
            _apiClient,
            Substitute.For<IGroupClient>(),
            Substitute.For<IOrganizationClient>(),
            _logger);
    }

    // ========== GetNotificationsAsync ==========

    #region GetNotificationsAsync Tests

    [Test]
    public async Task GetNotificationsAsync_ReturnsPaginatedResult_WhenApiSucceeds()
    {
        // Arrange
        var notifications = new List<NotificationListDto>
        {
            new() { Id = Guid.NewGuid(), Title = "Event Updated", IsRead = false },
            new() { Id = Guid.NewGuid(), Title = "New Registration", IsRead = true }
        };
        var response = new PaginatedResultOfNotificationListDto
        {
            Items = notifications,
            PageNumber = 1,
            PageSize = 20,
            TotalCount = 2
        };

        _apiClient.GetNotificationsAsync(
                Arg.Any<bool?>(), Arg.Any<int?>(), Arg.Any<int?>(),
                Arg.Any<int?>(), Arg.Any<bool?>(), Arg.Any<bool?>(),
                Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(response);

        // Act
        var result = await _service.GetNotificationsAsync(1, 20);

        // Assert
        await Assert.That(result.Items.Count).IsEqualTo(2);
        await Assert.That(result.PageNumber).IsEqualTo(1);
        await Assert.That(result.PageSize).IsEqualTo(20);
        await Assert.That(result.TotalCount).IsEqualTo(2);
    }

    [Test]
    public async Task GetNotificationsAsync_PassesScopeFilter_WhenProvided()
    {
        // Arrange
        var response = new PaginatedResultOfNotificationListDto
        {
            Items = new List<NotificationListDto>(),
            PageNumber = 1,
            PageSize = 20,
            TotalCount = 0
        };

        _apiClient.GetNotificationsAsync(
                Arg.Any<bool?>(), Arg.Any<int?>(), Arg.Any<int?>(),
                Arg.Any<int?>(), Arg.Any<bool?>(), Arg.Any<bool?>(),
                Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(response);

        // Act
        await _service.GetNotificationsAsync(1, 20, isRead: false, notificationScopeId: 2);

        // Assert
        await _apiClient.Received(1).GetNotificationsAsync(
            false, Arg.Any<int?>(), 2, Arg.Any<int?>(),
            Arg.Any<bool?>(), Arg.Any<bool?>(), 1, 20,
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetNotificationsAsync_ReturnsEmpty_WhenApiThrows()
    {
        // Arrange
        _apiClient.GetNotificationsAsync(
                Arg.Any<bool?>(), Arg.Any<int?>(), Arg.Any<int?>(),
                Arg.Any<int?>(), Arg.Any<bool?>(), Arg.Any<bool?>(),
                Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Server Error", 500));

        // Act
        var result = await _service.GetNotificationsAsync(1, 20);

        // Assert
        await Assert.That(result.Items).IsEmpty();
        await Assert.That(result.TotalCount).IsEqualTo(0);
    }

    [Test]
    public async Task GetNotificationsAsync_ReturnsEmpty_WhenGeneralExceptionThrown()
    {
        // Arrange
        _apiClient.GetNotificationsAsync(
                Arg.Any<bool?>(), Arg.Any<int?>(), Arg.Any<int?>(),
                Arg.Any<int?>(), Arg.Any<bool?>(), Arg.Any<bool?>(),
                Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Network failure"));

        // Act
        var result = await _service.GetNotificationsAsync(1, 20);

        // Assert
        await Assert.That(result.Items).IsEmpty();
        await Assert.That(result.TotalCount).IsEqualTo(0);
    }

    [Test]
    public async Task GetNotificationsAsync_HandlesNullItems_FromApi()
    {
        // Arrange
        var response = new PaginatedResultOfNotificationListDto
        {
            Items = null,
            PageNumber = 1,
            PageSize = 20,
            TotalCount = 0
        };

        _apiClient.GetNotificationsAsync(
                Arg.Any<bool?>(), Arg.Any<int?>(), Arg.Any<int?>(),
                Arg.Any<int?>(), Arg.Any<bool?>(), Arg.Any<bool?>(),
                Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(response);

        // Act
        var result = await _service.GetNotificationsAsync(1, 20);

        // Assert
        await Assert.That(result.Items).IsEmpty();
    }

    [Test]
    public async Task GetNotificationsAsync_PassesReasonFilter_WhenProvided()
    {
        // Arrange
        var response = new PaginatedResultOfNotificationListDto
        {
            Items = new List<NotificationListDto>(),
            PageNumber = 1,
            PageSize = 20,
            TotalCount = 0
        };

        _apiClient.GetNotificationsAsync(
                Arg.Any<bool?>(), Arg.Any<int?>(), Arg.Any<int?>(),
                Arg.Any<int?>(), Arg.Any<bool?>(), Arg.Any<bool?>(),
                Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(response);

        // Act
        await _service.GetNotificationsAsync(1, 20, notificationReasonId: 2);

        // Assert
        await _apiClient.Received(1).GetNotificationsAsync(
            Arg.Any<bool?>(), null, Arg.Any<int?>(), 2,
            Arg.Any<bool?>(), Arg.Any<bool?>(), 1, 20,
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetNotificationsAsync_PassesArchivedFilter_WhenProvided()
    {
        // Arrange
        var response = new PaginatedResultOfNotificationListDto
        {
            Items = new List<NotificationListDto>(),
            PageNumber = 1,
            PageSize = 20,
            TotalCount = 0
        };

        _apiClient.GetNotificationsAsync(
                Arg.Any<bool?>(), Arg.Any<int?>(), Arg.Any<int?>(),
                Arg.Any<int?>(), Arg.Any<bool?>(), Arg.Any<bool?>(),
                Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(response);

        // Act
        await _service.GetNotificationsAsync(1, 20, isArchived: true);

        // Assert
        await _apiClient.Received(1).GetNotificationsAsync(
            Arg.Any<bool?>(), null, Arg.Any<int?>(), Arg.Any<int?>(),
            true, Arg.Any<bool?>(), 1, 20,
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetNotificationsAsync_PassesSnoozedFilter_WhenProvided()
    {
        // Arrange
        var response = new PaginatedResultOfNotificationListDto
        {
            Items = new List<NotificationListDto>(),
            PageNumber = 1,
            PageSize = 20,
            TotalCount = 0
        };

        _apiClient.GetNotificationsAsync(
                Arg.Any<bool?>(), Arg.Any<int?>(), Arg.Any<int?>(),
                Arg.Any<int?>(), Arg.Any<bool?>(), Arg.Any<bool?>(),
                Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(response);

        // Act
        await _service.GetNotificationsAsync(1, 20, isSnoozed: true);

        // Assert
        await _apiClient.Received(1).GetNotificationsAsync(
            Arg.Any<bool?>(), null, Arg.Any<int?>(), Arg.Any<int?>(),
            Arg.Any<bool?>(), true, 1, 20,
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetNotificationsAsync_PassesAllFilters_WhenProvided()
    {
        // Arrange
        var response = new PaginatedResultOfNotificationListDto
        {
            Items = new List<NotificationListDto>(),
            PageNumber = 1,
            PageSize = 20,
            TotalCount = 0
        };

        _apiClient.GetNotificationsAsync(
                Arg.Any<bool?>(), Arg.Any<int?>(), Arg.Any<int?>(),
                Arg.Any<int?>(), Arg.Any<bool?>(), Arg.Any<bool?>(),
                Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(response);

        // Act
        await _service.GetNotificationsAsync(1, 20,
            isRead: false, notificationScopeId: 2, notificationReasonId: 3,
            isArchived: true, isSnoozed: false);

        // Assert
        await _apiClient.Received(1).GetNotificationsAsync(
            false, null, 2, 3, true, false, 1, 20,
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    #endregion

    [Test]
    public async Task GetWebPushConfigurationAsync_ReturnsPublicConfiguration_WhenApiSucceeds()
    {
        _apiClient.GetWebPushConfigurationAsync(cancellationToken: Arg.Any<CancellationToken>())
            .Returns(new WebPushPublicConfiguration { Enabled = true, PublicKey = "public-key" });

        var result = await _service.GetWebPushConfigurationAsync();

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Enabled).IsTrue();
        await Assert.That(result.PublicKey).IsEqualTo("public-key");
    }

    [Test]
    public async Task GetVapidPublicKeyAsync_UsesGeneratedApiClient()
    {
        _apiClient.GetVapidPublicKeyAsync(cancellationToken: Arg.Any<CancellationToken>())
            .Returns("public-key");

        var result = await _service.GetVapidPublicKeyAsync();

        await Assert.That(result).IsEqualTo("public-key");
        await _apiClient.Received(1).GetVapidPublicKeyAsync(cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SubscribeWebPushAsync_UsesGeneratedClientAndReturnsSuccess()
    {
        _apiClient.SubscribeCurrentUserWebPushSubscriptionAsync(Arg.Any<SubscribeCurrentUserWebPushSubscriptionCommand>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true, Id = Guid.NewGuid() });

        var result = await _service.SubscribeWebPushAsync("device-a", "https://push.example/sub", "p256dh", "auth", null);

        await Assert.That(result).IsTrue();
        await _apiClient.Received(1).SubscribeCurrentUserWebPushSubscriptionAsync(
            Arg.Is<SubscribeCurrentUserWebPushSubscriptionCommand>(request => request.DeviceIdentifier == "device-a" && request.Endpoint == "https://push.example/sub"),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UnsubscribeWebPushAsync_ReturnsFalse_WhenApiThrows()
    {
        _apiClient.UnsubscribeCurrentUserWebPushSubscriptionAsync(Arg.Any<Guid>(), cancellationToken: Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Forbidden", 403));

        var result = await _service.UnsubscribeWebPushAsync(Guid.NewGuid());

        await Assert.That(result).IsFalse();
    }

    // ========== GetNotificationByIdAsync ==========

    #region GetNotificationByIdAsync Tests

    [Test]
    public async Task GetNotificationByIdAsync_ReturnsNotification_WhenApiSucceeds()
    {
        // Arrange
        var notificationId = Guid.NewGuid();
        var notification = new NotificationDto { Id = notificationId, Title = "Test Notification" };

        _apiClient.GetNotificationByIdAsync(notificationId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(notification);

        // Act
        var result = await _service.GetNotificationByIdAsync(notificationId);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Id).IsEqualTo(notificationId);
    }

    [Test]
    public async Task GetNotificationByIdAsync_ReturnsNull_WhenNotFound()
    {
        // Arrange
        var notificationId = Guid.NewGuid();
        _apiClient.GetNotificationByIdAsync(notificationId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Not Found", 404));

        // Act
        var result = await _service.GetNotificationByIdAsync(notificationId);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetNotificationByIdAsync_ReturnsNull_WhenApiThrows()
    {
        // Arrange
        var notificationId = Guid.NewGuid();
        _apiClient.GetNotificationByIdAsync(notificationId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Server Error", 500));

        // Act
        var result = await _service.GetNotificationByIdAsync(notificationId);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetNotificationByIdAsync_ReturnsNull_WhenGeneralExceptionThrown()
    {
        // Arrange
        var notificationId = Guid.NewGuid();
        _apiClient.GetNotificationByIdAsync(notificationId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Network failure"));

        // Act
        var result = await _service.GetNotificationByIdAsync(notificationId);

        // Assert
        await Assert.That(result).IsNull();
    }

    #endregion

    // ========== GetUnreadCountAsync ==========

    #region GetUnreadCountAsync Tests

    [Test]
    public async Task GetUnreadCountAsync_ReturnsCount_WhenApiSucceeds()
    {
        // Arrange
        var response = new UnreadCountDto { UnreadCount = 5 };
        _apiClient.GetUnreadNotificationCountAsync(Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(response);

        // Act
        var result = await _service.GetUnreadCountAsync();

        // Assert
        await Assert.That(result).IsEqualTo(5);
    }

    [Test]
    public async Task GetUnreadCountAsync_PassesScopeFilter_WhenProvided()
    {
        // Arrange
        var response = new UnreadCountDto { UnreadCount = 3 };
        _apiClient.GetUnreadNotificationCountAsync(Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(response);

        // Act
        var result = await _service.GetUnreadCountAsync(notificationScopeId: 2);

        // Assert
        await _apiClient.Received(1).GetUnreadNotificationCountAsync(2, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await Assert.That(result).IsEqualTo(3);
    }

    [Test]
    public async Task GetUnreadCountAsync_ReturnsZero_WhenNullUnreadCount()
    {
        // Arrange
        var response = new UnreadCountDto { UnreadCount = null };
        _apiClient.GetUnreadNotificationCountAsync(Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(response);

        // Act
        var result = await _service.GetUnreadCountAsync();

        // Assert
        await Assert.That(result).IsEqualTo(0);
    }

    [Test]
    public async Task GetUnreadCountAsync_ReturnsZero_WhenApiThrows()
    {
        // Arrange
        _apiClient.GetUnreadNotificationCountAsync(Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Server Error", 500));

        // Act
        var result = await _service.GetUnreadCountAsync();

        // Assert
        await Assert.That(result).IsEqualTo(0);
    }

    [Test]
    public async Task GetUnreadCountAsync_ReturnsZero_WhenGeneralExceptionThrown()
    {
        // Arrange
        _apiClient.GetUnreadNotificationCountAsync(Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Network failure"));

        // Act
        var result = await _service.GetUnreadCountAsync();

        // Assert
        await Assert.That(result).IsEqualTo(0);
    }

    #endregion

    // ========== MarkAsReadAsync ==========

    #region MarkAsReadAsync Tests

    [Test]
    public async Task MarkAsReadAsync_ReturnsTrue_WhenApiSucceeds()
    {
        // Arrange
        var notificationId = Guid.NewGuid();
        var response = new BaseCommandResponseOfGuid { Success = true, Id = notificationId };
        _apiClient.MarkNotificationAsReadAsync(notificationId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(response);

        // Act
        var result = await _service.MarkAsReadAsync(notificationId);

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task MarkAsReadAsync_ReturnsFalse_WhenApiReturnsFailure()
    {
        // Arrange
        var notificationId = Guid.NewGuid();
        var response = new BaseCommandResponseOfGuid { Success = false };
        _apiClient.MarkNotificationAsReadAsync(notificationId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(response);

        // Act
        var result = await _service.MarkAsReadAsync(notificationId);

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task MarkAsReadAsync_ReturnsFalse_WhenSuccessDefaultsFalse()
    {
        // Arrange
        var notificationId = Guid.NewGuid();
        var response = new BaseCommandResponseOfGuid();
        _apiClient.MarkNotificationAsReadAsync(notificationId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(response);

        // Act
        var result = await _service.MarkAsReadAsync(notificationId);

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task MarkAsReadAsync_ReturnsFalse_WhenApiThrows()
    {
        // Arrange
        var notificationId = Guid.NewGuid();
        _apiClient.MarkNotificationAsReadAsync(notificationId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Server Error", 500));

        // Act
        var result = await _service.MarkAsReadAsync(notificationId);

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task MarkAsReadAsync_ReturnsFalse_WhenGeneralExceptionThrown()
    {
        // Arrange
        var notificationId = Guid.NewGuid();
        _apiClient.MarkNotificationAsReadAsync(notificationId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Network failure"));

        // Act
        var result = await _service.MarkAsReadAsync(notificationId);

        // Assert
        await Assert.That(result).IsFalse();
    }

    #endregion

    // ========== MarkAllAsReadAsync ==========

    #region MarkAllAsReadAsync Tests

    [Test]
    public async Task MarkAllAsReadAsync_ReturnsTrue_WhenApiSucceeds()
    {
        // Arrange
        var response = new BaseCommandResponseOfGuid { Success = true };
        _apiClient.MarkAllNotificationsAsReadAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(response);

        // Act
        var result = await _service.MarkAllAsReadAsync();

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task MarkAllAsReadAsync_ReturnsFalse_WhenApiReturnsFailure()
    {
        // Arrange
        var response = new BaseCommandResponseOfGuid { Success = false };
        _apiClient.MarkAllNotificationsAsReadAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(response);

        // Act
        var result = await _service.MarkAllAsReadAsync();

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task MarkAllAsReadAsync_ReturnsFalse_WhenApiThrows()
    {
        // Arrange
        _apiClient.MarkAllNotificationsAsReadAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Server Error", 500));

        // Act
        var result = await _service.MarkAllAsReadAsync();

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task MarkAllAsReadAsync_ReturnsFalse_WhenGeneralExceptionThrown()
    {
        // Arrange
        _apiClient.MarkAllNotificationsAsReadAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Network failure"));

        // Act
        var result = await _service.MarkAllAsReadAsync();

        // Assert
        await Assert.That(result).IsFalse();
    }

    #endregion

    // ========== DeleteAsync ==========

    #region DeleteAsync Tests

    [Test]
    public async Task DeleteAsync_ReturnsTrue_WhenApiSucceeds()
    {
        // Arrange
        var notificationId = Guid.NewGuid();
        _apiClient.DeleteNotificationAsync(notificationId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.DeleteAsync(notificationId);

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalse_WhenApiThrows()
    {
        // Arrange
        var notificationId = Guid.NewGuid();
        _apiClient.DeleteNotificationAsync(notificationId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Server Error", 500));

        // Act
        var result = await _service.DeleteAsync(notificationId);

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalse_WhenGeneralExceptionThrown()
    {
        // Arrange
        var notificationId = Guid.NewGuid();
        _apiClient.DeleteNotificationAsync(notificationId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Network failure"));

        // Act
        var result = await _service.DeleteAsync(notificationId);

        // Assert
        await Assert.That(result).IsFalse();
    }

    #endregion

    // ========== ArchiveAsync ==========

    #region ArchiveAsync Tests

    [Test]
    public async Task ArchiveAsync_ReturnsTrue_WhenApiSucceeds()
    {
        // Arrange
        var notificationId = Guid.NewGuid();
        var response = new BaseCommandResponseOfGuid { Success = true, Id = notificationId };
        _apiClient.ArchiveNotificationAsync(notificationId, true, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(response);

        // Act
        var result = await _service.ArchiveAsync(notificationId);

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task ArchiveAsync_PassesArchiveFalse_WhenUnarchiving()
    {
        // Arrange
        var notificationId = Guid.NewGuid();
        var response = new BaseCommandResponseOfGuid { Success = true, Id = notificationId };
        _apiClient.ArchiveNotificationAsync(notificationId, false, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(response);

        // Act
        var result = await _service.ArchiveAsync(notificationId, archive: false);

        // Assert
        await Assert.That(result).IsTrue();
        await _apiClient.Received(1).ArchiveNotificationAsync(notificationId, false, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ArchiveAsync_ReturnsFalse_WhenApiReturnsFailure()
    {
        // Arrange
        var notificationId = Guid.NewGuid();
        var response = new BaseCommandResponseOfGuid { Success = false };
        _apiClient.ArchiveNotificationAsync(notificationId, true, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(response);

        // Act
        var result = await _service.ArchiveAsync(notificationId);

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task ArchiveAsync_ReturnsFalse_WhenApiThrows()
    {
        // Arrange
        var notificationId = Guid.NewGuid();
        _apiClient.ArchiveNotificationAsync(notificationId, Arg.Any<bool?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Server Error", 500));

        // Act
        var result = await _service.ArchiveAsync(notificationId);

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task ArchiveAsync_ReturnsFalse_WhenGeneralExceptionThrown()
    {
        // Arrange
        var notificationId = Guid.NewGuid();
        _apiClient.ArchiveNotificationAsync(notificationId, Arg.Any<bool?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Network failure"));

        // Act
        var result = await _service.ArchiveAsync(notificationId);

        // Assert
        await Assert.That(result).IsFalse();
    }

    #endregion

    // ========== SnoozeAsync ==========

    #region SnoozeAsync Tests

    [Test]
    public async Task SnoozeAsync_ReturnsTrue_WhenApiSucceeds()
    {
        // Arrange
        var notificationId = Guid.NewGuid();
        var snoozedUntil = TestTime.UtcNow.AddHours(3);
        var response = new BaseCommandResponseOfGuid { Success = true, Id = notificationId };
        _apiClient.SnoozeNotificationAsync(notificationId, snoozedUntil, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(response);

        // Act
        var result = await _service.SnoozeAsync(notificationId, snoozedUntil);

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task SnoozeAsync_PassesSnoozedUntil_ToApi()
    {
        // Arrange
        var notificationId = Guid.NewGuid();
        var snoozedUntil = TestTime.UtcNow.AddDays(1);
        var response = new BaseCommandResponseOfGuid { Success = true, Id = notificationId };
        _apiClient.SnoozeNotificationAsync(notificationId, snoozedUntil, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(response);

        // Act
        await _service.SnoozeAsync(notificationId, snoozedUntil);

        // Assert
        await _apiClient.Received(1).SnoozeNotificationAsync(notificationId, snoozedUntil, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SnoozeAsync_ReturnsFalse_WhenApiReturnsFailure()
    {
        // Arrange
        var notificationId = Guid.NewGuid();
        var snoozedUntil = TestTime.UtcNow.AddHours(3);
        var response = new BaseCommandResponseOfGuid { Success = false };
        _apiClient.SnoozeNotificationAsync(notificationId, snoozedUntil, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(response);

        // Act
        var result = await _service.SnoozeAsync(notificationId, snoozedUntil);

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task SnoozeAsync_ReturnsFalse_WhenApiThrows()
    {
        // Arrange
        var notificationId = Guid.NewGuid();
        var snoozedUntil = TestTime.UtcNow.AddHours(3);
        _apiClient.SnoozeNotificationAsync(notificationId, Arg.Any<DateTimeOffset?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Server Error", 500));

        // Act
        var result = await _service.SnoozeAsync(notificationId, snoozedUntil);

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task SnoozeAsync_ReturnsFalse_WhenGeneralExceptionThrown()
    {
        // Arrange
        var notificationId = Guid.NewGuid();
        var snoozedUntil = TestTime.UtcNow.AddHours(3);
        _apiClient.SnoozeNotificationAsync(notificationId, Arg.Any<DateTimeOffset?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Network failure"));

        // Act
        var result = await _service.SnoozeAsync(notificationId, snoozedUntil);

        // Assert
        await Assert.That(result).IsFalse();
    }

    #endregion

    private static ApiException CreateApiException(string message, int statusCode, string response = "")
    {
        return new ApiException(
            message,
            statusCode,
            response,
            new Dictionary<string, IEnumerable<string>>(),
            new InvalidOperationException(message));
    }
}
