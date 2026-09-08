using Explore.Blazor.Client.Helpers;
using Explore.Blazor.Client.Models.Events;

namespace Explore.Blazor.Client.Tests.Services;

/// <summary>
/// Unit tests for EventService.
/// Tests all event-related operations including CRUD and session management.
/// </summary>
/// <remarks>
/// These tests verify:
/// - Proper API client calls with HAL resource types
/// - Error handling and fallback behavior
/// - Response transformation from HAL to DTO using extension methods
/// - Edge cases (null responses, exceptions)
///
/// IMPORTANT: The API client uses HAL resource types:
/// - GetEventsAsync returns HalCollectionResourceOfEventListDto
/// - GetMyEventsAsync returns HalCollectionResourceOfEventListDto
/// - GetEventByIdAsync returns HalResourceOfEventDto
/// The service converts these to plain DTOs using HalResourceExtensions.
/// </remarks>
public class EventServiceTests
{
    public interface ITestEventTagClient :
        IEventClient,
        IEventLifecycleClient,
        IEventManagementReadClient,
        IEventParticipationClient,
        IEventPublicActionClient
    {
    }

    private readonly ITestEventTagClient _apiClient;
    private readonly ILogger<EventService> _logger;
    private readonly EventService _service;

    public EventServiceTests()
    {
        _apiClient = Substitute.For<ITestEventTagClient>();
        _logger = Substitute.For<ILogger<EventService>>();
        _service = new EventService(
            _apiClient,
            _apiClient,
            _apiClient,
            _apiClient,
            _apiClient,
            _logger);
    }

    [Test]
    public async Task ConfigureEventParticipationAsync_ForwardsGeneratedDtoAndConcurrencyStamp()
    {
        var eventId = Guid.NewGuid();
        var concurrencyStamp = Guid.NewGuid();
        var configuration = new ConfigureEventParticipationDto
        {
            ParticipationHandlingModeId = 4,
            AdvanceRegistrationObligationId = 3,
            IdentityAccessModeId = 2,
            GuestRecoveryPolicy = GuestRecoveryPolicyEnum.UnverifiedEmailAccepted
        };
        _apiClient.ConfigureEventParticipationAsync(
                eventId,
                $"\"{concurrencyStamp:D}\"",
                configuration,
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Id = eventId, Success = true });

        var result = await _service.ConfigureEventParticipationAsync(
            eventId,
            configuration,
            concurrencyStamp);

        await Assert.That(result.Success).IsTrue();
        await _apiClient.Received(1).ConfigureEventParticipationAsync(
            eventId,
            $"\"{concurrencyStamp:D}\"",
            configuration,
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ConfigureEventParticipationAsync_WhenConflict_ReturnsRefreshableFailure()
    {
        var eventId = Guid.NewGuid();
        var configuration = new ConfigureEventParticipationDto();
        _apiClient.ConfigureEventParticipationAsync(
                eventId,
                Arg.Any<string>(),
                configuration,
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException<ProblemDetails>(
                "Conflict",
                409,
                string.Empty,
                new Dictionary<string, IEnumerable<string>>(),
                new ProblemDetails { Detail = "The participation configuration changed." },
                null));

        var result = await _service.ConfigureEventParticipationAsync(
            eventId,
            configuration,
            Guid.NewGuid());

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("event_participation_configuration_concurrency_conflict");
        await Assert.That(result.Message).Contains("changed");
        await Assert.That(result.Errors).Contains("Refresh the event and try again.");
    }


    [Test]
    public async Task GetEventPublicActionsAsync_ReturnsGeneratedHalItems()
    {
        var eventId = Guid.NewGuid();
        var action = new HalResourceOfEventPublicActionDto
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            DestinationDomain = "registration.example",
            Url = "https://registration.example",
            _links = new Dictionary<string, HalLink>
            {
                ["external-registration"] = new() { Href = "/api/events/actions/redirect", Method = "GET" }
            }
        };
        _apiClient.GetEventPublicActionsAsync(
                eventId,
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new HalCollectionResourceOfEventPublicActionDto
            {
                _embedded = new HalCollectionEmbeddedOfEventPublicActionDto { Items = [action] }
            });

        var result = await _service.GetEventPublicActionsAsync(eventId);

        await Assert.That(result).Contains(action);
    }

    #region GetAllEventsAsync Tests

    [Test]
    public async Task GetAllEventsAsync_ReturnsEvents_WhenApiSucceeds()
    {
        // Arrange
        var expectedEvents = ComponentDataBuilder.EventListDto.Generate(3);
        var halResponse = CreateDiscoveryCollectionResponse(expectedEvents);

        _apiClient.GetEventsAsync().ReturnsForAnyArgs(halResponse);

        // Act
        var result = await _service.GetAllEventsAsync();

        // Assert
        await Assert.That(result.Count).IsEqualTo(3);
        await Assert.That(result.First().Title).IsEqualTo(expectedEvents.First().Title);
        await Assert.That(result.First().Id).IsEqualTo(expectedEvents.First().Id);
    }

    [Test]
    public async Task GetAllEventsAsync_ReturnsEmptyList_WhenApiReturnsNull()
    {
        // Arrange
        _apiClient.GetEventsAsync().ReturnsForAnyArgs((HalCollectionResourceOfEventDiscoveryItemDto?)null);

        // Act
        var result = await _service.GetAllEventsAsync();

        // Assert
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task GetAllEventsAsync_ReturnsEmptyList_WhenApiThrowsException()
    {
        // Arrange
        _apiClient.GetEventsAsync().ThrowsAsyncForAnyArgs(CreateApiException("API Error", 500));

        // Act
        var result = await _service.GetAllEventsAsync();

        // Assert
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task GetAllEventsAsync_ReturnsEmptyList_WhenEmbeddedIsNull()
    {
        // Arrange
        var halResponse = new HalCollectionResourceOfEventDiscoveryItemDto
        {
            _embedded = null
        };
        _apiClient.GetEventsAsync().ReturnsForAnyArgs(halResponse);

        // Act
        var result = await _service.GetAllEventsAsync();

        // Assert
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task GetAllEventsAsync_CallsApiWithCorrectPagination()
    {
        // Arrange
        var halResponse = CreateDiscoveryCollectionResponse([]);
        _apiClient.GetEventsAsync().ReturnsForAnyArgs(halResponse);

        // Act
        await _service.GetAllEventsAsync();

        // Assert - Service should request page 1 with size 100
        await _apiClient.Received(1).GetEventsAsync(1, 100);
    }

    [Test]
    public async Task GetAllEventsAsync_MapsFederatedEnvelopeAndPreservesSourceAffordance()
    {
        var recordId = Guid.NewGuid();
        const string sourcePath = "/api/event/federated/source-record/source";
        _apiClient.GetEventsAsync().ReturnsForAnyArgs(new HalCollectionResourceOfEventDiscoveryItemDto
        {
            _embedded = new HalCollectionEmbeddedOfEventDiscoveryItemDto
            {
                Items =
                [
                    new HalResourceOfEventDiscoveryItemDto
                    {
                        Source = "atproto",
                        FederatedEvent = new FederatedEventDto
                        {
                            Name = "Federated community gathering",
                            Description = "From a community PDS",
                            StartsAtUtc = new DateTimeOffset(2026, 9, 1, 18, 0, 0, TimeSpan.Zero),
                            Mode = "in-person",
                            Status = "scheduled"
                        },
                        Federation = new EventFederationMetadataDto
                        {
                            AtprotoRecordId = recordId,
                            Provenance = "AT Protocol",
                            HasSourceLink = true
                        },
                        _links = new Dictionary<string, HalLink>
                        {
                            ["source"] = new() { Href = sourcePath, Method = "GET" }
                        }
                    }
                ]
            }
        });

        var result = await _service.GetAllEventsAsync();
        var mapped = result.Single();

        await Assert.That(mapped.Title).IsEqualTo("Federated community gathering");
        await Assert.That(mapped.Id).IsNull();
        await Assert.That(mapped.AtprotoRecordId).IsEqualTo(recordId);
        await Assert.That(mapped.IsFederatedDiscoveryEvent()).IsTrue();
        await Assert.That(mapped.GetHalHref("source")).IsEqualTo(sourcePath);
        await Assert.That(mapped.HasHalLink("edit")).IsFalse();
    }

    [Test]
    public async Task GetAllEventsAsync_RefreshReplacesTombstonedFederatedResult()
    {
        var stale = new HalCollectionResourceOfEventDiscoveryItemDto
        {
            _embedded = new HalCollectionEmbeddedOfEventDiscoveryItemDto
            {
                Items =
                [
                    new HalResourceOfEventDiscoveryItemDto
                    {
                        Source = "atproto",
                        FederatedEvent = new FederatedEventDto { Name = "Tombstoned remote event" }
                    }
                ]
            }
        };
        var refreshed = new HalCollectionResourceOfEventDiscoveryItemDto
        {
            _embedded = new HalCollectionEmbeddedOfEventDiscoveryItemDto { Items = [] }
        };
        _apiClient.GetEventsAsync(1, 100).Returns(stale, refreshed);

        var first = await _service.GetAllEventsAsync();
        var second = await _service.GetAllEventsAsync();

        await Assert.That(first.Select(item => item.Title)).Contains("Tombstoned remote event");
        await Assert.That(second).IsEmpty();
        await _apiClient.Received(2).GetEventsAsync(1, 100);
    }

    #endregion

    #region GetMyEventsAsync Tests

    [Test]
    public async Task GetMyEventsAsync_ReturnsUserEvents_WhenApiSucceeds()
    {
        // Arrange
        var expectedEvents = ComponentDataBuilder.EventListDto.Generate(2);
        var halResponse = CreateHalCollectionResponse(expectedEvents);

        _apiClient.GetMyEventsAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(halResponse);

        // Act
        var result = await _service.GetMyEventsAsync();

        // Assert
        await Assert.That(result.Count).IsEqualTo(2);
    }

    [Test]
    public async Task GetMyEventsAsync_ReturnsEmptyList_WhenApiThrowsException()
    {
        // Arrange
        _apiClient.GetMyEventsAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Unauthorized", 401));

        // Act
        var result = await _service.GetMyEventsAsync();

        // Assert
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task GetMyEventsAsync_ReturnsEmptyList_WhenApiReturnsNull()
    {
        // Arrange
        _apiClient.GetMyEventsAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((HalCollectionResourceOfEventListDto?)null);

        // Act
        var result = await _service.GetMyEventsAsync();

        // Assert
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task GetMyEventsAsync_CallsApiWithCorrectPagination()
    {
        // Arrange
        var halResponse = CreateHalCollectionResponse([]);
        _apiClient.GetMyEventsAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(halResponse);

        // Act
        await _service.GetMyEventsAsync();

        // Assert - Service should request page 1 with size 100
        await _apiClient.Received(1).GetMyEventsAsync(1, 100, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetManagedEventsByActorAsync_UsesManagedEndpointWithActorIdAndCancellationToken()
    {
        var actorId = Guid.NewGuid();
        using var cancellationTokenSource = new CancellationTokenSource();
        var halResponse = CreateHalCollectionResponse([]);

        _apiClient.GetManagedEventsByActorAsync(
                actorId,
                2,
                25,
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                cancellationTokenSource.Token)
            .Returns(halResponse);

        var result = await _service.GetManagedEventsByActorAsync(
            actorId,
            2,
            25,
            cancellationTokenSource.Token);

        await Assert.That(result.Items).IsEmpty();
        await _apiClient.Received(1).GetManagedEventsByActorAsync(
            actorId,
            2,
            25,
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            cancellationTokenSource.Token);
        await _apiClient.DidNotReceiveWithAnyArgs().GetEventsAsync(default);
    }

    [Test]
    public async Task GetManagedEventsByActorAsync_PreservesCollectionLinks()
    {
        var actorId = Guid.NewGuid();
        var halResponse = CreateHalCollectionResponse([]);
        halResponse._links = new Dictionary<string, HalLink>
        {
            ["create"] = new() { Href = "/api/event/create", Method = "POST" }
        };

        _apiClient.GetManagedEventsByActorAsync(
                actorId,
                1,
                100,
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(halResponse);

        var result = await _service.GetManagedEventsByActorAsync(actorId);

        await Assert.That(result.HasHalLink("create")).IsTrue();
        await Assert.That(result.Links!["create"].Href).IsEqualTo("/api/event/create");
    }

    [Test]
    public async Task GetMyEventsPagedAsync_PreservesCollectionLinksAndCancellationToken()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        var halResponse = CreateHalCollectionResponse([]);
        halResponse._links = new Dictionary<string, HalLink>
        {
            ["create"] = new() { Href = "/api/event/create", Method = "POST" }
        };

        _apiClient.GetMyEventsAsync(
                2,
                25,
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                cancellationTokenSource.Token)
            .Returns(halResponse);

        var result = await _service.GetMyEventsPagedAsync(2, 25, cancellationTokenSource.Token);

        await Assert.That(result.HasHalLink("create")).IsTrue();
        await Assert.That(result.Links!["create"].Href).IsEqualTo("/api/event/create");
        await _apiClient.Received(1).GetMyEventsAsync(
            2,
            25,
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            cancellationTokenSource.Token);
    }

    #endregion

    #region GetEventByIdAsync Tests

    [Test]
    public async Task GetEventByIdAsync_ReturnsEvent_WhenFound()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var generatedEvent = ComponentDataBuilder.EventDto.Generate();
        var expectedEvent = generatedEvent with
        {
            Id = eventId,
            ActorTypeFullName = generatedEvent.ActorTypeFullName ?? "Organization",
            EventStatusId = 1,
            EventStatusFullName = "Draft",
            EventStatusMasterCode = "DRFT",
            EventFormatId = 1,
            EventFormatFullName = "In-Person",
            EventFormatMasterCode = "INPERSON",
            VisibilityTypeId = 1,
            VisibilityTypeFullName = "Public",
            VisibilityTypeMasterCode = "PUBLIC",
            FeaturedImageId = Guid.NewGuid(),
            FeaturedImageUri = "https://example.com/image.png",
            SessionCount = 1,
            Timezone = "UTC"
        };
        var halResponse = CreateHalResourceResponse(expectedEvent);

        _apiClient.GetEventByIdAsync(eventId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(halResponse);

        // Act
        var result = await _service.GetEventByIdAsync(eventId);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Id).IsEqualTo(eventId);
        await Assert.That(result.Title).IsEqualTo(expectedEvent.Title);
    }

    [Test]
    public async Task GetEventByIdAsync_ReturnsNull_WhenNotFound()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        _apiClient.GetEventByIdAsync(eventId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Not Found", 404));
        _apiClient.GetEventManagementDetailsAsync(eventId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Unauthorized", 401));

        // Act
        var result = await _service.GetEventByIdAsync(eventId);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetEventByIdAsync_WhenPublicDetailHiddenAndManagementDetailAllowed_ReturnsManagementEvent()
    {
        var eventId = Guid.NewGuid();
        var expectedEvent = ComponentDataBuilder.EventDto.Generate() with
        {
            Id = eventId,
            Title = "Moderated management event",
            EventStatusId = 6,
            EventStatusFullName = "Moderated",
            EventStatusMasterCode = "MODERATED"
        };
        var managementResponse = CreateHalResourceResponse(expectedEvent);

        _apiClient.GetEventByIdAsync(eventId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Not Found", 404));
        _apiClient.GetEventManagementDetailsAsync(eventId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(managementResponse);

        var result = await _service.GetEventByIdAsync(eventId);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Id).IsEqualTo(eventId);
        await Assert.That(result.Title).IsEqualTo("Moderated management event");
    }

    [Test]
    public async Task GetEventByIdAsync_WhenPublicDetailHiddenAndManagementDetailForbidden_ReturnsNull()
    {
        var eventId = Guid.NewGuid();

        _apiClient.GetEventByIdAsync(eventId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Not Found", 404));
        _apiClient.GetEventManagementDetailsAsync(eventId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Forbidden", 403));

        var result = await _service.GetEventByIdAsync(eventId);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetEventByIdAsync_ReturnsNull_WhenApiThrowsException()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        _apiClient.GetEventByIdAsync(eventId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Server Error", 500));

        // Act
        var result = await _service.GetEventByIdAsync(eventId);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetEventByIdAsync_CallsApiWithCorrectId()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var halResponse = new HalResourceOfEventDto
        {
            Id = eventId,
            Title = "Tracked Event"
        };

        _apiClient.GetEventByIdAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(halResponse);

        // Act
        await _service.GetEventByIdAsync(eventId);

        // Assert
        await _apiClient.Received(1).GetEventByIdAsync(eventId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region GetEventCreationContextAsync Tests

    [Test]
    public async Task GetEventCreationContextAsync_ReturnsContext_WhenApiSucceeds()
    {
        // Arrange
        var context = new EventCreationContextDto
        {
            CanCreate = true,
            DefaultPublisherMode = "personal",
            PublisherOptions =
            [
                new EventCreationPublisherOptionDto
                {
                    PublisherMode = "personal",
                    DisplayName = "Personal profile",
                    CanPublish = true
                }
            ]
        };

        _apiClient.GetEventCreationContextAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(context);

        // Act
        var result = await _service.GetEventCreationContextAsync();

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.CanCreate).IsTrue();
        await Assert.That(result.DefaultPublisherMode).IsEqualTo("personal");
        await Assert.That(result.PublisherOptions?.Count).IsEqualTo(1);
    }

    [Test]
    public async Task GetEventCreationContextAsync_ReturnsNull_WhenApiThrowsException()
    {
        // Arrange
        _apiClient.GetEventCreationContextAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Unauthorized", 401));

        // Act
        var result = await _service.GetEventCreationContextAsync();

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetEventCreationContextAsync_PassesCancellationToken()
    {
        // Arrange
        using var cancellationTokenSource = new CancellationTokenSource();
        var context = new EventCreationContextDto { CanCreate = false };

        _apiClient.GetEventCreationContextAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(context);

        // Act
        await _service.GetEventCreationContextAsync(cancellationTokenSource.Token);

        // Assert
        await _apiClient.Received(1).GetEventCreationContextAsync(
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            cancellationTokenSource.Token);
    }

    #endregion

    #region GetEventPublishReadinessAsync Tests

    [Test]
    public async Task GetEventPublishReadinessAsync_ReturnsReadiness_WhenApiSucceeds()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var readiness = new EventPublishReadinessDto
        {
            EventId = eventId,
            IsReady = false,
            Errors =
            [
                new EventPublishReadinessErrorDto
                {
                    Code = "schedule_session_required",
                    FieldPath = "schedule.sessions",
                    Message = "At least one scheduled session is required before publishing.",
                    Severity = "error"
                }
            ]
        };

        _apiClient.GetEventPublishReadinessAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(readiness);

        // Act
        var result = await _service.GetEventPublishReadinessAsync(eventId);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.IsReady).IsFalse();
        await Assert.That(result.EventId).IsEqualTo(eventId);
        await Assert.That(result.Errors?.Count).IsEqualTo(1);
        await Assert.That(result.Errors?.First().FieldPath).IsEqualTo("schedule.sessions");
    }

    [Test]
    public async Task GetEventPublishReadinessAsync_ReturnsNull_WhenApiThrowsException()
    {
        // Arrange
        _apiClient.GetEventPublishReadinessAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Not Found", 404));

        // Act
        var result = await _service.GetEventPublishReadinessAsync(Guid.NewGuid());

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetEventPublishReadinessAsync_PassesCancellationToken()
    {
        // Arrange
        using var cancellationTokenSource = new CancellationTokenSource();
        var eventId = Guid.NewGuid();
        var readiness = new EventPublishReadinessDto { EventId = eventId, IsReady = true };

        _apiClient.GetEventPublishReadinessAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(readiness);

        // Act
        await _service.GetEventPublishReadinessAsync(eventId, cancellationTokenSource.Token);

        // Assert
        await _apiClient.Received(1).GetEventPublishReadinessAsync(
            eventId,
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            cancellationTokenSource.Token);
    }

    #endregion

    #region CreateEventAsync Tests

    [Test]
    public async Task CreateEventAsync_ReturnsSuccess_WhenValid()
    {
        // Arrange
        var createDto = ComponentDataBuilder.CreateEventRequest.Generate();
        var expectedId = Guid.NewGuid();
        var expectedResponse = ComponentDataBuilder.SuccessResponse(expectedId);

        _apiClient.CreateEventAsync(Arg.Any<CreateEventDraftRequestDto>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
        .Returns(expectedResponse);

        // Act
        var result = await _service.CreateEventAsync(createDto);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsTrue();
        await Assert.That(result.Id).IsEqualTo(expectedId);
    }

    [Test]
    public async Task CreateEventAsync_ReturnsNull_WhenApiThrowsException()
    {
        // Arrange
        var createDto = ComponentDataBuilder.CreateEventRequest.Generate();
        _apiClient.CreateEventAsync(Arg.Any<CreateEventDraftRequestDto>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
        .ThrowsAsync(CreateApiException("Bad Request", 400, "Validation failed"));

        // Act
        var result = await _service.CreateEventAsync(createDto);

        // Assert - Service propagates the exception, returns null
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task CreateEventAsync_CallsApiWithCorrectDto()
    {
        // Arrange
        var createDto = ComponentDataBuilder.CreateEventRequest.Generate();
        var expectedResponse = ComponentDataBuilder.SuccessResponse();
        _apiClient.CreateEventAsync(Arg.Any<CreateEventDraftRequestDto>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
        .Returns(expectedResponse);

        // Act
        await _service.CreateEventAsync(createDto);

        // Assert
        await _apiClient.Received(1).CreateEventAsync(createDto, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateEventAsync_NormalizesNullableCollectionsBeforeCallingApi()
    {
        // Arrange
        var createDto = ComponentDataBuilder.CreateEventRequest.Generate();
        CreateEventDraftRequestDto? capturedRequest = null;
        _apiClient.CreateEventAsync(
                Arg.Do<CreateEventDraftRequestDto>(request => capturedRequest = request),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(ComponentDataBuilder.SuccessResponse());

        // Act
        await _service.CreateEventAsync(createDto);

        // Assert
        await Assert.That(capturedRequest).IsNotNull();
        await Assert.That(capturedRequest!.CategoryIds).IsNotNull();
        await Assert.That(capturedRequest.TagIds).IsNotNull();
        await Assert.That(capturedRequest.Locations).IsNotNull();
        await Assert.That(capturedRequest.Sessions).IsNotNull();
        await Assert.That(capturedRequest.Days).IsNotNull();
        await Assert.That(capturedRequest.Rooms).IsNotNull();
        await Assert.That(capturedRequest.AgendaItems).IsNotNull();
    }

    [Test]
    public async Task CreateEventAsync_PropagatesValidationProblemDetailsForFieldMapping()
    {
        // Arrange
        var createDto = ComponentDataBuilder.CreateEventRequest.Generate();
        var exception = new ApiException<ValidationProblemDetails>(
            "Bad Request",
            400,
            string.Empty,
            new Dictionary<string, IEnumerable<string>>(),
            new ValidationProblemDetails
            {
                Errors = new Dictionary<string, ICollection<string>>
                {
                    ["Title"] = ["The Title field is required."]
                }
            },
            null);
        _apiClient.CreateEventAsync(
                Arg.Any<CreateEventDraftRequestDto>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(exception);

        // Act
        var action = async () => await _service.CreateEventAsync(createDto);

        // Assert
        await Assert.That(action).Throws<ApiException<ValidationProblemDetails>>();
    }

    [Test]
    public async Task CreateEventAsync_WithConcreteGeneratedClient_SendsIdempotencyKey()
    {
        // Arrange
        var createDto = ComponentDataBuilder.CreateEventRequest.Generate();
        var idempotencyKey = Guid.NewGuid().ToString("N");
        using var handler = new CapturingHandler("""
            {"id":"11111111-1111-1111-1111-111111111111","success":true,"message":"Event created successfully."}
            """);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.test/")
        };
        var lifecycleClient = new EventLifecycleClient(httpClient);
        var service = new EventService(
            Substitute.For<IEventClient>(),
            lifecycleClient,
            Substitute.For<IEventManagementReadClient>(),
            Substitute.For<IEventParticipationClient>(),
            Substitute.For<IEventPublicActionClient>(),
            _logger);

        // Act
        var result = await service.CreateEventAsync(createDto, idempotencyKey);

        // Assert
        await Assert.That(result?.Success).IsTrue();
        IEnumerable<string>? values = null;
        var hasIdempotencyHeader = handler.LastRequest?.Headers.TryGetValues("Idempotency-Key", out values) == true;
        await Assert.That(hasIdempotencyHeader).IsTrue();
        await Assert.That(values!.Single()).IsEqualTo(idempotencyKey);
    }

    #endregion

    #region PublishEventAsync Tests

    [Test]
    public async Task PublishEventAsync_ReturnsSuccess_WhenApiSucceeds()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var concurrencyStamp = Guid.NewGuid();
        var expectedResponse = ComponentDataBuilder.SuccessResponse(eventId);

        _apiClient.PublishEventAsync(
                Arg.Any<Guid>(),
                Arg.Any<PublishEventRequestDto>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(expectedResponse);

        // Act
        var result = await _service.PublishEventAsync(eventId, concurrencyStamp);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsTrue();
        await Assert.That(result.Id).IsEqualTo(eventId);
    }

    [Test]
    public async Task PublishEventAsync_SendsExpectedConcurrencyStamp()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var concurrencyStamp = Guid.NewGuid();
        var expectedResponse = ComponentDataBuilder.SuccessResponse(eventId);

        _apiClient.PublishEventAsync(
                Arg.Any<Guid>(),
                Arg.Any<PublishEventRequestDto>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(expectedResponse);

        // Act
        await _service.PublishEventAsync(eventId, concurrencyStamp);

        // Assert
        await _apiClient.Received(1).PublishEventAsync(
            eventId,
            Arg.Is<PublishEventRequestDto>(request => request.ExpectedConcurrencyStamp == concurrencyStamp),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PublishEventAsync_ReturnsCommandResponse_WhenApiRejectsPublish()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var concurrencyStamp = Guid.NewGuid();
        var rejectedResponse = new BaseCommandResponseOfGuid
        {
            Success = false,
            Id = eventId,
            FailureCode = "event_publish_concurrency_conflict",
            Errors = ["Refresh the event and try publishing again."]
        };

        _apiClient.PublishEventAsync(
                Arg.Any<Guid>(),
                Arg.Any<PublishEventRequestDto>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException<BaseCommandResponseOfGuid>(
                "Conflict",
                409,
                string.Empty,
                new Dictionary<string, IEnumerable<string>>(),
                rejectedResponse,
                null));

        // Act
        var result = await _service.PublishEventAsync(eventId, concurrencyStamp);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("event_publish_concurrency_conflict");
    }

    [Test]
    public async Task PublishEventAsync_ReturnsNull_WhenApiThrowsUnexpectedException()
    {
        // Arrange
        _apiClient.PublishEventAsync(
                Arg.Any<Guid>(),
                Arg.Any<PublishEventRequestDto>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Server Error", 500));

        // Act
        var result = await _service.PublishEventAsync(Guid.NewGuid(), Guid.NewGuid());

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task PublishEventAsync_PassesCancellationToken()
    {
        // Arrange
        using var cancellationTokenSource = new CancellationTokenSource();
        var eventId = Guid.NewGuid();
        var expectedResponse = ComponentDataBuilder.SuccessResponse(eventId);

        _apiClient.PublishEventAsync(
                Arg.Any<Guid>(),
                Arg.Any<PublishEventRequestDto>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(expectedResponse);

        // Act
        await _service.PublishEventAsync(eventId, Guid.NewGuid(), cancellationTokenSource.Token);

        // Assert
        await _apiClient.Received(1).PublishEventAsync(
            eventId,
            Arg.Any<PublishEventRequestDto>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            cancellationTokenSource.Token);
    }

    #endregion


    #region UpdateEventAsync Tests

    [Test]
    public async Task UpdateEventAsync_ReturnsSuccess_WhenValid()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var updateDto = new EventDraftEditModel
        {
            ExpectedConcurrencyStamp = Guid.NewGuid(),
            Title = "Updated Title"
        };
        var expectedResponse = ComponentDataBuilder.SuccessResponse(eventId);

        _apiClient.UpdateEventAsync(
                Arg.Any<Guid>(),
                Arg.Any<UpdateEventDto>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(expectedResponse);

        // Act
        var result = await _service.UpdateEventAsync(eventId, updateDto);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsTrue();
        await Assert.That(result.Id).IsEqualTo(eventId);
    }

    [Test]
    public async Task UpdateEventAsync_ReturnsNull_WhenApiThrowsException()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var updateDto = new EventDraftEditModel
        {
            ExpectedConcurrencyStamp = Guid.NewGuid(),
            Title = "Updated Title"
        };
        _apiClient.UpdateEventAsync(
                Arg.Any<Guid>(),
                Arg.Any<UpdateEventDto>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Not Found", 404));

        // Act
        var result = await _service.UpdateEventAsync(eventId, updateDto);

        // Assert - Service propagates the exception, returns null
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task UpdateEventAsync_CallsApiWithCorrectParameters()
    {
        // Arrange
        var eventId = new Guid("d415b43c-3f93-4b68-9a2d-59021d838e11");
        var concurrencyStamp = Guid.NewGuid();
        var updateDto = new EventDraftEditModel
        {
            ExpectedConcurrencyStamp = concurrencyStamp,
            Title = "Test Title"
        };
        var expectedResponse = ComponentDataBuilder.SuccessResponse(eventId);
        UpdateEventDto? capturedUpdate = null;
        string? capturedIfMatch = null;
        _apiClient.UpdateEventAsync(
                Arg.Any<Guid>(),
                Arg.Do<UpdateEventDto>(dto => capturedUpdate = dto),
                Arg.Do<string?>(value => capturedIfMatch = value),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(expectedResponse);

        // Act
        await _service.UpdateEventAsync(eventId, updateDto);

        await _apiClient.Received(1).UpdateEventAsync(
            eventId,
            Arg.Any<UpdateEventDto>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
        await Assert.That(capturedUpdate).IsNotNull();
        await Assert.That(capturedUpdate!.Title?.Value).IsEqualTo("Test Title");
        await Assert.That(capturedUpdate.Subtitle?.Value.HasValue).IsTrue();
        await Assert.That(capturedUpdate.FeaturedImage?.Value.HasValue).IsTrue();
        await Assert.That(capturedIfMatch).IsEqualTo($"\"{concurrencyStamp:D}\"");
    }

    [Test]
    public async Task UpdateEventAsync_WhenApiReturnsConflictProblem_ReturnsStaleDraftFailure()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var updateDto = new EventDraftEditModel
        {
            ExpectedConcurrencyStamp = Guid.NewGuid(),
            Title = "Updated Title"
        };
        var problemDetails = new ProblemDetails
        {
            Status = 409,
            Title = "Concurrency conflict",
            Detail = "The event draft changed since it was loaded. Refresh the event and try again."
        };

        _apiClient.UpdateEventAsync(
                Arg.Any<Guid>(),
                Arg.Any<UpdateEventDto>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException<ProblemDetails>(
                "Conflict",
                409,
                string.Empty,
                new Dictionary<string, IEnumerable<string>>(),
                problemDetails,
                null));

        // Act
        var result = await _service.UpdateEventAsync(eventId, updateDto);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("event_concurrency_conflict");
        await Assert.That(result.Message).Contains("event draft changed");
    }

    #endregion

    #region DeleteEventAsync Tests

    [Test]
    public async Task DeleteEventAsync_ReturnsTrue_WhenSuccess()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        _apiClient.DeleteEventAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.DeleteEventAsync(eventId);

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task DeleteEventAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        _apiClient.DeleteEventAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Not Found", 404));

        // Act
        var result = await _service.DeleteEventAsync(eventId);

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task DeleteEventAsync_ReturnsFalse_WhenUnauthorized()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        _apiClient.DeleteEventAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Unauthorized", 401));

        // Act
        var result = await _service.DeleteEventAsync(eventId);

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task DeleteEventAsync_CallsApiWithCorrectId()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        _apiClient.DeleteEventAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        await _service.DeleteEventAsync(eventId);

        // Assert
        await _apiClient.Received(1).DeleteEventAsync(eventId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Event Management and Actor Tests

    [Test]
    public async Task GetManagedEventProgramSummaryAsync_UsesManagedRoute()
    {
        var eventId = Guid.NewGuid();
        var expected = new EventProgramSummaryDto { EventId = eventId, EventTitle = "Draft event" };
        _apiClient.GetManagedEventProgramSummaryAsync(
                eventId,
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await _service.GetManagedEventProgramSummaryAsync(eventId);

        await Assert.That(result).IsSameReferenceAs(expected);
        await _apiClient.Received(1).GetManagedEventProgramSummaryAsync(
            eventId,
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
        await _apiClient.DidNotReceiveWithAnyArgs().GetEventProgramSummaryAsync(default);
    }

    [Test]
    public async Task DeleteEventAsync_ReturnsFalse_WhenServerError()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        _apiClient.DeleteEventAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Server Error", 500));

        // Act
        var result = await _service.DeleteEventAsync(eventId);

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task GetEventsPagedAsync_ReturnsEmptyPage_WhenApiThrows()
    {
        // Arrange
        _apiClient.GetEventsAsync(Arg.Any<int?>(), Arg.Any<int?>())
            .ThrowsAsync(CreateApiException("Server Error", 500));

        // Act
        var result = await _service.GetEventsPagedAsync(pageNumber: 3, pageSize: 25);

        // Assert
        await Assert.That(result.Items).IsEmpty();
        await Assert.That(result.PageNumber).IsEqualTo(3);
        await Assert.That(result.PageSize).IsEqualTo(25);
    }

    [Test]
    public async Task GetEventsPagedAsync_CallsApiWithCorrectPagination()
    {
        // Arrange
        var halResponse = CreateDiscoveryCollectionResponse([]);
        _apiClient.GetEventsAsync(Arg.Any<int?>(), Arg.Any<int?>())
            .Returns(halResponse);

        // Act
        await _service.GetEventsPagedAsync(pageNumber: 2, pageSize: 15);

        // Assert
        await _apiClient.Received(1).GetEventsAsync(2, 15);
    }

    [Test]
    public async Task GetPublicEventsByActorAsync_ForwardsActorIdToServerFilter()
    {
        // Arrange
        var actorId = Guid.NewGuid();
        var otherActorId = Guid.NewGuid();
        var events = new List<EventListDto>
        {
            new() { Id = Guid.NewGuid(), ActorId = actorId, Title = "Actor event" },
            new() { Id = Guid.NewGuid(), ActorId = otherActorId, Title = "Server decides visibility" }
        };
        _apiClient.GetEventsAsync().ReturnsForAnyArgs(CreateDiscoveryCollectionResponse(events));

        // Act
        var result = await _service.GetPublicEventsByActorAsync(actorId);

        // Assert
        await Assert.That(result.Count).IsEqualTo(2);
        await _apiClient.Received(1).GetEventsAsync(pageNumber: 1, pageSize: 100, actorId: actorId, view: "All");
    }

    [Test]
    public async Task GetProfileEventsByActorAsync_MergesManagedEventsAndUsesManagedDuplicate()
    {
        var actorId = Guid.NewGuid();
        var sharedEventId = Guid.NewGuid();
        var publicEvent = new EventListDto { Id = sharedEventId, ActorId = actorId, Title = "Public version" };
        var managedDuplicate = new EventListDto { Id = sharedEventId, ActorId = actorId, Title = "Managed version" };
        var moderatedEvent = new EventListDto { Id = Guid.NewGuid(), ActorId = actorId, Title = "Moderated", EventStatusId = 6 };

        _apiClient.GetEventsAsync().ReturnsForAnyArgs(CreateDiscoveryCollectionResponse([publicEvent]));
        _apiClient.GetManagedEventsByActorAsync(
                actorId: actorId,
                pageNumber: 1,
                pageSize: 100)
            .Returns(CreateHalCollectionResponse(new List<EventListDto> { managedDuplicate, moderatedEvent }));

        var result = await _service.GetProfileEventsByActorAsync(actorId);

        await Assert.That(result.Count).IsEqualTo(2);
        await Assert.That(result.Select(evt => evt.Title)).Contains("Managed version");
        await Assert.That(result.Select(evt => evt.Title)).Contains("Moderated");
        await Assert.That(result.Select(evt => evt.Title)).DoesNotContain("Public version");
        await _apiClient.Received(1).GetEventsAsync(pageNumber: 1, pageSize: 100, actorId: actorId, view: "All");
        await _apiClient.Received(1).GetManagedEventsByActorAsync(
            actorId: actorId,
            pageNumber: 1,
            pageSize: 100);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a HAL collection response with the provided event list items.
    /// </summary>
    private static HalCollectionResourceOfEventListDto CreateHalCollectionResponse(
        IList<EventListDto> items)
    {
        return new HalCollectionResourceOfEventListDto
        {
            _embedded = new HalCollectionEmbeddedOfEventListDto
            {
                Items = items.Select(ToHalResource).ToList()
            }
        };
    }

    private static HalCollectionResourceOfEventDiscoveryItemDto CreateDiscoveryCollectionResponse(
        IList<EventListDto> items)
    {
        return new HalCollectionResourceOfEventDiscoveryItemDto
        {
            _embedded = new HalCollectionEmbeddedOfEventDiscoveryItemDto
            {
                Items = items.Select(item => new HalResourceOfEventDiscoveryItemDto
                {
                    Source = "local",
                    Event = item
                }).ToList()
            }
        };
    }

    private static HalResourceOfEventListDto ToHalResource(EventListDto item)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(item);
        return System.Text.Json.JsonSerializer.Deserialize<HalResourceOfEventListDto>(json)
               ?? new HalResourceOfEventListDto();
    }

    private static HalCollectionResourceOfEventSessionListDto CreateHalSessionCollectionResponse(
        IList<EventSessionListDto> items)
    {
        return new HalCollectionResourceOfEventSessionListDto
        {
            _embedded = new HalCollectionEmbeddedOfEventSessionListDto
            {
                Items = items.Select(ToHalSessionResource).ToList()
            }
        };
    }

    private static HalResourceOfEventSessionListDto ToHalSessionResource(EventSessionListDto item)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(item);
        return System.Text.Json.JsonSerializer.Deserialize<HalResourceOfEventSessionListDto>(json)
               ?? new HalResourceOfEventSessionListDto();
    }

    /// <summary>
    /// Creates a HAL resource response from an event DTO.
    /// Uses JSON serialization to properly populate all properties.
    /// </summary>
    private static HalResourceOfEventDto CreateHalResourceResponse(EventDto dto)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(dto);
        return System.Text.Json.JsonSerializer.Deserialize<HalResourceOfEventDto>(json)
               ?? new HalResourceOfEventDto();
    }

    private static ApiException CreateApiException(string message, int statusCode, string response = "")
    {
        return new ApiException(
            message,
            statusCode,
            response,
            new Dictionary<string, IEnumerable<string>>(),
            new InvalidOperationException(message));
    }

    private static Dictionary<string, object> CreateHalLinks(params string[] linkRels)
    {
        var links = string.Join(
            ',',
            linkRels.Select(rel => $"\"{rel}\":{{\"href\":\"/api/eventsession/{rel}\",\"method\":\"POST\"}}"));
        using var doc = System.Text.Json.JsonDocument.Parse($"{{\"_links\":{{{links}}}}}");

        return new Dictionary<string, object>
        {
            ["_links"] = doc.RootElement.GetProperty("_links").Clone()
        };
    }

    private sealed class CapturingHandler(string responseBody) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.Created)
            {
                Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "application/json")
            };

            return Task.FromResult(response);
        }
    }

    #endregion
}
