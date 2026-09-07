using System.Reflection;
using System.Text.Json;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Helpers;
using Explore.Blazor.Client.Pages.Events.Sessions;
using Microsoft.AspNetCore.Components.Forms;
using ClientValidationProblemDetails = Explore.Blazor.Client.Clients.ValidationProblemDetails;
using ComposerCreateEventSessionRequest = Explore.Blazor.Client.Clients.CreateEventSessionDto;

namespace Explore.Blazor.Client.Tests.Pages.Event;

public sealed class CreateSessionTests : IDisposable
{
    private readonly BlazorTestContext _ctx;
    private readonly IEventService _eventService;
    private readonly Explore.Blazor.Client.Contracts.Services.IEventSessionService _eventSessionService;
    private readonly IEventSessionLanguageService _eventSessionLanguageService;
    private readonly Explore.Blazor.Client.Contracts.Services.Lookup.IEventLookupService _eventLookupService;
    private readonly ILocationService _locationService;
    private readonly ILocationRoomService _locationRoomService;
    public CreateSessionTests()
    {
        _ctx = new BlazorTestContext();
        _eventService = Substitute.For<IEventService>();
        _eventSessionService = Substitute.For<Explore.Blazor.Client.Contracts.Services.IEventSessionService>();
        _eventSessionLanguageService = Substitute.For<IEventSessionLanguageService>();
        _eventLookupService = Substitute.For<Explore.Blazor.Client.Contracts.Services.Lookup.IEventLookupService>();
        _locationService = Substitute.For<ILocationService>();
        _locationRoomService = Substitute.For<ILocationRoomService>();
        _ctx.Services.AddSingleton(_eventService);
        _ctx.Services.AddSingleton(_eventSessionService);
        _ctx.Services.AddSingleton(_eventSessionLanguageService);
        _ctx.Services.AddSingleton(_eventLookupService);
        _ctx.Services.AddSingleton(_locationService);
        _ctx.Services.AddSingleton(_locationRoomService);
        _eventLookupService.GetRegistrationModesAsync().Returns(CreateRegistrationModes());
        _eventLookupService.GetEventSessionKindsAsync().Returns(CreateSessionKinds());
        _eventSessionLanguageService.SyncLanguagesForSessionAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<IEnumerable<int>>(),
                Arg.Any<CancellationToken>())
            .Returns(true);
        _locationService.GetAllLocationsAsync().Returns(new List<LocationListDto>());
        _locationRoomService.GetRoomsByLocationAsync(Arg.Any<Guid>()).Returns(new List<LocationRoomListDto>());
        _eventSessionService.GetSessionGroupsByEventAsync(Arg.Any<Guid>()).Returns(new List<HalResourceOfEventSessionGroupListDto>());
        _eventSessionService.GetEventSessionCreateContextAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => CreateSessionContext(call.ArgAt<Guid>(0), Guid.NewGuid()));
        _eventSessionService.AssignSessionToGroupAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<int>())
            .Returns(new BaseCommandResponseOfGuid { Success = true, Id = Guid.NewGuid() });
    }

    [Test]
    public async Task OnInitializedAsync_WhenRenderedFromBlazouterUrl_UsesEventIdFromCurrentUri()
    {
        var eventId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"/events/{eventId}/sessions/create");

        _eventService.GetEventByIdAsync(eventId).Returns(CreateDraftEvent(eventId, tenantId));
        _eventSessionService.GetEventSessionCreateContextAsync(eventId, Arg.Any<CancellationToken>())
            .Returns(CreateSessionContext(eventId, tenantId));

        var cut = _ctx.Render<CreateSession>();
        cut.WaitForState(() => !cut.Markup.Contains("Loading event draft", StringComparison.Ordinal));

        await _eventService.Received(1).GetEventByIdAsync(eventId);
        await _eventSessionService.Received(1).GetEventSessionCreateContextAsync(eventId, Arg.Any<CancellationToken>());
        await Assert.That(cut.Markup).DoesNotContain("The event draft could not be loaded.");
    }

    [Test]
    public async Task Render_UsesProgramItemDefaultCopy()
    {
        var eventId = Guid.NewGuid();
        _eventService.GetEventByIdAsync(eventId).Returns(CreateDraftEvent(eventId, Guid.NewGuid()));

        var cut = _ctx.Render<CreateSession>(parameters => parameters.Add(component => component.EventId, eventId));
        cut.WaitForState(() => cut.Markup.Contains("Add program item", StringComparison.Ordinal));

        await Assert.That(cut.Markup).Contains("Add a talk, workshop, panel, class, or activity");
        await Assert.That(cut.Markup).Contains("Program item title");
        await Assert.That(cut.Markup).Contains("Program item date");
        await Assert.That(cut.Markup).Contains("Registration mode");
        await Assert.That(cut.Markup).Contains("Open");
        await Assert.That(cut.Markup).Contains("Program item context");
        await Assert.That(cut.Markup).Contains("Event timezone");
        await Assert.That(cut.Markup).Contains("Europe/Brussels");
        await Assert.That(cut.Markup).Contains("Save program item");
        await Assert.That(cut.Markup).DoesNotContain("Add session");
    }

    [Test]
    public async Task Render_WhenContextHasNotices_ShowsSetupGuidance()
    {
        var eventId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _eventService.GetEventByIdAsync(eventId).Returns(CreateDraftEvent(eventId, tenantId));
        _eventSessionService.GetEventSessionCreateContextAsync(eventId, Arg.Any<CancellationToken>()).Returns(CreateSessionContext(
            eventId,
            tenantId,
            notices: ["Add at least one program section before assigning this item."]));

        var cut = _ctx.Render<CreateSession>(parameters => parameters.Add(component => component.EventId, eventId));
        cut.WaitForState(() => cut.Markup.Contains("Program item context", StringComparison.Ordinal));

        await Assert.That(cut.Markup).Contains("Add at least one program section before assigning this item.");
    }

    [Test]
    public async Task SaveSessionAsync_CreatesSessionForDraftEventAndReturnsToDraft()
    {
        var eventId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var roomId = Guid.NewGuid();
        _eventService.GetEventByIdAsync(eventId).Returns(CreateDraftEvent(eventId, tenantId));
        _eventSessionService.GetEventSessionCreateContextAsync(eventId, Arg.Any<CancellationToken>()).Returns(CreateSessionContext(
            eventId,
            tenantId,
            locations:
            [
                new EventSessionCreateLocationOptionDto { Id = locationId, FullName = "Main Hall", City = "Brussels", Country = "Belgium" }
            ],
            rooms:
            [
                new EventSessionCreateRoomOptionDto { Id = roomId, LocationId = locationId, Name = "Auditorium", Capacity = 120 }
            ]));

        _eventSessionService.CreateSessionAsync(Arg.Any<ComposerCreateEventSessionRequest>()).Returns(new BaseCommandResponseOfGuid
        {
            Success = true,
            Id = sessionId
        });

        var cut = _ctx.Render<CreateSession>(parameters => parameters.Add(component => component.EventId, eventId));
        var session = GetPrivateField<ComposerCreateEventSessionRequest>(cut.Instance, "_session");
        session.Title = "Opening talk";
        session.Description = "A focused opening session.";
        session.LocationId = locationId;
        session.RoomId = roomId;
        session.EventSessionKindId = 1;
        session.RegistrationModeId = 2;
        SetPrivateField(cut.Instance, "_sessionDate", new DateTime(2026, 6, 1));
        SetPrivateField(cut.Instance, "_startTime", new TimeSpan(9, 30, 0));
        SetPrivateField(cut.Instance, "_endTime", new TimeSpan(10, 30, 0));

        await cut.InvokeAsync(() => InvokePrivateAsync(cut.Instance, "SaveSessionAsync"));

        await _eventSessionService.Received(1).CreateSessionAsync(Arg.Is<ComposerCreateEventSessionRequest>(dto =>
            dto.EventId == eventId
            && dto.Title == "Opening talk"
            && dto.Description == "A focused opening session."
            && dto.LocationId == locationId
            && dto.RoomId == roomId
            && dto.EventSessionKindId == 1
            && dto.RegistrationModeId == 2
            && dto.StartTime == new DateTimeOffset(2026, 6, 1, 7, 30, 0, TimeSpan.Zero)
            && dto.EndTime == new DateTimeOffset(2026, 6, 1, 8, 30, 0, TimeSpan.Zero)
            && dto.StartTime.Value.Offset == TimeSpan.Zero
            && dto.EndTime.Value.Offset == TimeSpan.Zero));
        await Assert.That(_ctx.Services.GetRequiredService<NavigationManager>().Uri.EndsWith($"/events/{eventId}/edit?programUpdated=1", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task SaveSessionAsync_WhenProgramSectionSelected_AssignsSessionToGroupBeforeReturning()
    {
        var eventId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        _eventService.GetEventByIdAsync(eventId).Returns(CreateDraftEvent(eventId, tenantId));
        _eventSessionService.GetEventSessionCreateContextAsync(eventId, Arg.Any<CancellationToken>()).Returns(CreateSessionContext(
            eventId,
            tenantId,
            groups: [new EventSessionCreateGroupOptionDto { Id = groupId, Name = "Main track", SortOrder = 1 }]));
        _eventSessionService.CreateSessionAsync(Arg.Any<ComposerCreateEventSessionRequest>()).Returns(new BaseCommandResponseOfGuid
        {
            Success = true,
            Id = sessionId
        });
        _eventSessionService.AssignSessionToGroupAsync(eventId, groupId, sessionId, true, 0).Returns(new BaseCommandResponseOfGuid
        {
            Success = true,
            Id = Guid.NewGuid()
        });

        var cut = _ctx.Render<CreateSession>(parameters => parameters.Add(component => component.EventId, eventId));
        var session = GetPrivateField<ComposerCreateEventSessionRequest>(cut.Instance, "_session");
        session.Title = "Opening talk";
        SetPrivateField(cut.Instance, "_selectedSessionGroupId", (Guid?)groupId);

        await cut.InvokeAsync(() => InvokePrivateAsync(cut.Instance, "SaveSessionAsync"));

        await _eventSessionService.Received(1).AssignSessionToGroupAsync(eventId, groupId, sessionId, true, 0);
        await Assert.That(_ctx.Services.GetRequiredService<NavigationManager>().Uri.EndsWith($"/events/{eventId}/edit?programUpdated=1", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task SaveSessionAsync_WhenProgramAssignmentFails_DoesNotCreateDuplicateOnRetry()
    {
        var eventId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        _eventService.GetEventByIdAsync(eventId).Returns(CreateDraftEvent(eventId, tenantId));
        _eventSessionService.GetEventSessionCreateContextAsync(eventId, Arg.Any<CancellationToken>()).Returns(CreateSessionContext(
            eventId,
            tenantId,
            groups: [new EventSessionCreateGroupOptionDto { Id = groupId, Name = "Main track", SortOrder = 1 }]));
        _eventSessionService.CreateSessionAsync(Arg.Any<ComposerCreateEventSessionRequest>()).Returns(new BaseCommandResponseOfGuid
        {
            Success = true,
            Id = sessionId
        });
        _eventSessionService.AssignSessionToGroupAsync(eventId, groupId, sessionId, true, 0)
            .Returns(
                new BaseCommandResponseOfGuid { Success = false, Message = "Group assignment failed." },
                new BaseCommandResponseOfGuid { Success = true, Id = Guid.NewGuid() });

        var cut = _ctx.Render<CreateSession>(parameters => parameters.Add(component => component.EventId, eventId));
        var session = GetPrivateField<ComposerCreateEventSessionRequest>(cut.Instance, "_session");
        session.Title = "Opening talk";
        SetPrivateField(cut.Instance, "_selectedSessionGroupId", (Guid?)groupId);

        await cut.InvokeAsync(() => InvokePrivateAsync(cut.Instance, "SaveSessionAsync"));
        await cut.InvokeAsync(() => InvokePrivateAsync(cut.Instance, "SaveSessionAsync"));

        await _eventSessionService.Received(1).CreateSessionAsync(Arg.Any<ComposerCreateEventSessionRequest>());
        await _eventSessionService.Received(2).AssignSessionToGroupAsync(eventId, groupId, sessionId, true, 0);
        await Assert.That(_ctx.Services.GetRequiredService<NavigationManager>().Uri.EndsWith($"/events/{eventId}/edit?programUpdated=1", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task SaveSessionAsync_WhenEndTimeIsBeforeStartTime_DoesNotCallApi()
    {
        var eventId = Guid.NewGuid();
        _eventService.GetEventByIdAsync(eventId).Returns(CreateDraftEvent(eventId, Guid.NewGuid()));

        var cut = _ctx.Render<CreateSession>(parameters => parameters.Add(component => component.EventId, eventId));
        var session = GetPrivateField<ComposerCreateEventSessionRequest>(cut.Instance, "_session");
        session.Title = "Opening talk";
        SetPrivateField(cut.Instance, "_startTime", new TimeSpan(11, 0, 0));
        SetPrivateField(cut.Instance, "_endTime", new TimeSpan(10, 0, 0));

        await cut.InvokeAsync(async () => await InvokePrivateAsync(cut.Instance, "SaveSessionAsync"));

        await _eventSessionService.DidNotReceive().CreateSessionAsync(Arg.Any<ComposerCreateEventSessionRequest>());
        var errorMessage = GetSubmitError(cut.Instance);
        await Assert.That(errorMessage).IsEqualTo("End time must be after start time.");
    }

    [Test]
    public async Task SaveSessionAsync_WhenApiFails_AnnouncesAssertiveError()
    {
        var eventId = Guid.NewGuid();
        _eventService.GetEventByIdAsync(eventId).Returns(CreateDraftEvent(eventId, Guid.NewGuid()));
        _eventSessionService.CreateSessionAsync(Arg.Any<ComposerCreateEventSessionRequest>()).Returns(new BaseCommandResponseOfGuid
        {
            Success = false,
            Message = "Program item could not be saved."
        });

        var cut = _ctx.Render<CreateSession>(parameters => parameters.Add(component => component.EventId, eventId));
        var session = GetPrivateField<ComposerCreateEventSessionRequest>(cut.Instance, "_session");
        session.Title = "Opening talk";

        await cut.InvokeAsync(async () => await InvokePrivateAsync(cut.Instance, "SaveSessionAsync"));

        var errorMessage = GetSubmitError(cut.Instance);
        await Assert.That(errorMessage).IsEqualTo("Program item could not be saved.");
    }

    [Test]
    public async Task SaveSessionAsync_WithValidationProblemDetails_MapsServerErrorsIntoEditContext()
    {
        var eventId = Guid.NewGuid();
        _eventService.GetEventByIdAsync(eventId).Returns(CreateDraftEvent(eventId, Guid.NewGuid()));
        _eventSessionService.CreateSessionAsync(Arg.Any<ComposerCreateEventSessionRequest>())
            .ThrowsAsync(new ApiException<ClientValidationProblemDetails>(
                "Bad Request",
                400,
                string.Empty,
                new Dictionary<string, IEnumerable<string>>(),
                new ClientValidationProblemDetails
                {
                    Errors = new Dictionary<string, ICollection<string>>
                    {
                        ["Title.Value"] = new[] { "Use a clearer program item title." }
                    }
                },
                null));

        var cut = _ctx.Render<CreateSession>(parameters => parameters.Add(component => component.EventId, eventId));
        var session = GetPrivateField<ComposerCreateEventSessionRequest>(cut.Instance, "_session");
        session.Title = "Opening talk";

        await cut.InvokeAsync(async () => await InvokePrivateAsync(cut.Instance, "SaveSessionAsync"));

        await Assert.That(GetSubmitError(cut.Instance)).IsEqualTo("Please fix the validation errors below.");
        await Assert.That(GetValidationMessages(cut.Instance)).Contains("Use a clearer program item title.");
    }

    [Test]
    public async Task SaveSessionAsync_WithUnexpectedException_DoesNotEchoRawExceptionMessage()
    {
        const string rawProviderMessage = "provider rejected <script>alert(1)</script> secret";
        var eventId = Guid.NewGuid();
        _eventService.GetEventByIdAsync(eventId).Returns(CreateDraftEvent(eventId, Guid.NewGuid()));
        _eventSessionService.CreateSessionAsync(Arg.Any<ComposerCreateEventSessionRequest>())
            .ThrowsAsync(new InvalidOperationException(rawProviderMessage));

        var cut = _ctx.Render<CreateSession>(parameters => parameters.Add(component => component.EventId, eventId));
        var session = GetPrivateField<ComposerCreateEventSessionRequest>(cut.Instance, "_session");
        session.Title = "Opening talk";

        await cut.InvokeAsync(async () => await InvokePrivateAsync(cut.Instance, "SaveSessionAsync"));

        var submitError = GetSubmitError(cut.Instance);
        await Assert.That(submitError).IsEqualTo("Program item could not be saved. Please try again.");
        await Assert.That(submitError).DoesNotContain(rawProviderMessage);
        await Assert.That(submitError).DoesNotContain("<script>");
    }

    [Test]
    public async Task SaveSessionAsync_WhenCapacityIsZero_NormalizesCapacityBeforeCreate()
    {
        var eventId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _eventService.GetEventByIdAsync(eventId).Returns(CreateDraftEvent(eventId, tenantId));
        _eventSessionService.CreateSessionAsync(Arg.Any<ComposerCreateEventSessionRequest>()).Returns(new BaseCommandResponseOfGuid
        {
            Success = true,
            Id = Guid.NewGuid()
        });

        var cut = _ctx.Render<CreateSession>(parameters => parameters.Add(component => component.EventId, eventId));
        var session = GetPrivateField<ComposerCreateEventSessionRequest>(cut.Instance, "_session");
        session.Title = "Opening talk";
        session.MaxAudienceAttendees = 0;

        await InvokePrivateAsync(cut.Instance, "SaveSessionAsync");

        await _eventSessionService.Received(1).CreateSessionAsync(Arg.Is<ComposerCreateEventSessionRequest>(dto => dto.MaxAudienceAttendees == null));
    }

    [Test]
    public async Task SaveSessionAsync_WhenAddSessionLinkIsMissing_DoesNotCallApi()
    {
        var eventId = Guid.NewGuid();
        _eventService.GetEventByIdAsync(eventId).Returns(CreateDraftEvent(eventId, Guid.NewGuid(), canAddSession: false));

        var cut = _ctx.Render<CreateSession>(parameters => parameters.Add(component => component.EventId, eventId));
        var session = GetPrivateField<ComposerCreateEventSessionRequest>(cut.Instance, "_session");
        session.Title = "Opening talk";

        await InvokePrivateAsync(cut.Instance, "SaveSessionAsync");

        await _eventSessionService.DidNotReceive().CreateSessionAsync(Arg.Any<ComposerCreateEventSessionRequest>());
        var errorMessage = GetSubmitError(cut.Instance);
        await Assert.That(errorMessage).IsEqualTo("You do not currently have permission to add program items to this event draft.");
    }

    [Test]
    public async Task OnLocationChangedAsync_WhenLocationClears_ClearsRoomsAndSelectedRoom()
    {
        var eventId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var roomId = Guid.NewGuid();
        _eventService.GetEventByIdAsync(eventId).Returns(CreateDraftEvent(eventId, Guid.NewGuid()));
        _eventSessionService.GetEventSessionCreateContextAsync(eventId, Arg.Any<CancellationToken>()).Returns(CreateSessionContext(
            eventId,
            Guid.NewGuid(),
            locations: [new EventSessionCreateLocationOptionDto { Id = locationId, FullName = "Main Hall", City = "Brussels", Country = "Belgium" }],
            rooms: [new EventSessionCreateRoomOptionDto { Id = roomId, LocationId = locationId, Name = "Auditorium" }]));

        var cut = _ctx.Render<CreateSession>(parameters => parameters.Add(component => component.EventId, eventId));
        await InvokePrivateAsync(cut.Instance, "OnLocationChangedAsync", locationId);
        var session = GetPrivateField<ComposerCreateEventSessionRequest>(cut.Instance, "_session");
        session.RoomId = roomId;

        await InvokePrivateAsync(cut.Instance, "OnLocationChangedAsync", new object?[] { null });

        await Assert.That(session.LocationId).IsNull();
        await Assert.That(session.RoomId).IsNull();
        await Assert.That(GetPrivateField<ICollection<EventSessionCreateRoomOptionDto>>(cut.Instance, "_rooms")).IsEmpty();
    }

    [Test]
    public async Task OnInitializedAsync_AppliesServerOwnedRegistrationModeDefault()
    {
        var eventId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _eventService.GetEventByIdAsync(eventId).Returns(CreateDraftEvent(eventId, tenantId));
        _eventSessionService.GetEventSessionCreateContextAsync(eventId, Arg.Any<CancellationToken>()).Returns(CreateSessionContext(
            eventId,
            tenantId,
            registrationModeId: 3));

        var cut = _ctx.Render<CreateSession>(parameters => parameters.Add(component => component.EventId, eventId));
        cut.WaitForState(() => !cut.Markup.Contains("Loading event draft", StringComparison.Ordinal));

        var session = GetPrivateField<ComposerCreateEventSessionRequest>(cut.Instance, "_session");
        await Assert.That(session.RegistrationModeId).IsEqualTo(3);
    }

    public void Dispose() => _ctx.Dispose();

    private static EventDto CreateDraftEvent(Guid eventId, Guid tenantId, bool canAddSession = true)
    {
        var dto = new EventDto
        {
            Id = eventId,
            TenantId = tenantId,
            Title = "Program launch",
            ActorDisplayName = "ISLAMU",
            ActorTypeFullName = "Organization",
            EventStatusFullName = "Draft",
            EventStatusMasterCode = "DRAFT",
            VisibilityTypeFullName = "Public",
            VisibilityTypeMasterCode = "PUBLIC",
            EventFormatFullName = "In person",
            EventFormatMasterCode = "IN_PERSON",
            AdditionalProperties = new Dictionary<string, object>()
        };

        if (canAddSession)
        {
            using var doc = JsonDocument.Parse(
                "{\"self\":{\"href\":\"/api/event/1\"},\"add-session\":{\"href\":\"/api/event-session\",\"method\":\"POST\"}}");
            dto.AdditionalProperties["_links"] = doc.RootElement.Clone();
        }

        return dto;
    }

    private static EventSessionCreateContextDto CreateSessionContext(
        Guid eventId,
        Guid tenantId,
        IReadOnlyCollection<EventSessionCreateLocationOptionDto>? locations = null,
        IReadOnlyCollection<EventSessionCreateRoomOptionDto>? rooms = null,
        IReadOnlyCollection<EventSessionCreateGroupOptionDto>? groups = null,
        IReadOnlyCollection<string>? notices = null,
        int registrationModeId = 1)
    {
        return new EventSessionCreateContextDto
        {
            EventId = eventId,
            EventTitle = "Program launch",
            TenantId = tenantId,
            TimeZoneId = "Europe/Brussels",
            EventStartDate = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            EventEndDate = new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero),
            Defaults = new EventSessionCreateDefaultsDto
            {
                RegistrationModeId = registrationModeId
            },
            Locations = locations?.ToList() ?? [],
            Rooms = rooms?.ToList() ?? [],
            SessionGroups = groups?.ToList() ?? [],
            Notices = notices?.ToList() ?? []
        };
    }

    private static ICollection<RegistrationModeListDto> CreateRegistrationModes()
        =>
        [
            new() { Id = 1, MasterCode = "OPEN", FullName = "Open", Description = "Anyone can register." },
            new() { Id = 2, MasterCode = "APPROVAL_REQUIRED", FullName = "Approval required", Description = "Review requests before confirming." },
            new() { Id = 3, MasterCode = "INVITE_ONLY", FullName = "Invite only" }
        ];

    private static ICollection<EventSessionKindListDto> CreateSessionKinds()
        =>
        [
            new() { Id = 1, MasterCode = "TALK", FullName = "Talk", Description = "Lecture, khutbah, or presentation." },
            new() { Id = 2, MasterCode = "WORKSHOP", FullName = "Workshop", Description = "Hands-on guided session." }
        ];

    private static ICollection<LanguageListDto> CreateLanguages()
        =>
        [
            new() { Id = 1, MasterCode = "EN", FullName = "English" },
            new() { Id = 2, MasterCode = "AR", FullName = "Arabic" }
        ];

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field {fieldName} was not found.");
        return (T)(field.GetValue(instance) ?? throw new InvalidOperationException($"Field {fieldName} was null."));
    }

    private static string? GetSubmitError(object instance)
    {
        var submitState = GetPrivateField<Explore.Blazor.Client.Components.Forms.FormSubmitState>(instance, "_submitState");
        return submitState.ErrorMessage;
    }

    private static List<string> GetValidationMessages(object instance)
    {
        var editContext = GetPrivateField<EditContext>(instance, "_editContext");
        return editContext.GetValidationMessages().ToList();
    }

    private static void SetPrivateField<T>(object instance, string fieldName, T value)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field {fieldName} was not found.");
        field.SetValue(instance, value);
    }

    private static async Task InvokePrivateAsync(object instance, string methodName, params object?[] arguments)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Method {methodName} was not found.");
        if (method.Invoke(instance, arguments is null || arguments.Length == 0 ? null : arguments) is Task task)
        {
            await task;
        }
    }
}
