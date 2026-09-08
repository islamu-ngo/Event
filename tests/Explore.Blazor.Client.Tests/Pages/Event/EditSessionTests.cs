using System.Reflection;
using System.Text.Json;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Helpers;
using Explore.Blazor.Client.Pages.Events.Sessions;
using Microsoft.AspNetCore.Components.Forms;
using ClientValidationProblemDetails = Explore.Blazor.Client.Clients.ValidationProblemDetails;

namespace Explore.Blazor.Client.Tests.Pages.Event;

public sealed class EditSessionTests : IDisposable
{
    private readonly BlazorTestContext _ctx;
    private readonly IEventService _eventService;
    private readonly Explore.Blazor.Client.Contracts.Services.IEventSessionService _eventSessionService;
    private readonly IEventSessionLanguageService _eventSessionLanguageService;
    private readonly Explore.Blazor.Client.Contracts.Services.Lookup.IEventLookupService _eventLookupService;
    private readonly ILocationService _locationService;
    private readonly ILocationRoomService _locationRoomService;
    public EditSessionTests()
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
        _eventSessionLanguageService.GetLanguagesBySessionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<EventSessionLanguageListDto>());
        _eventSessionLanguageService.SyncLanguagesForSessionAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<IEnumerable<int>>(),
                Arg.Any<CancellationToken>())
            .Returns(true);
        _locationService.GetAllLocationsAsync().Returns(new List<LocationListDto>());
        _locationRoomService.GetRoomsByLocationAsync(Arg.Any<Guid>()).Returns(new List<LocationRoomListDto>());
        _eventSessionService.GetManagedSessionGroupsByEventAsync(Arg.Any<Guid>()).Returns(new List<HalResourceOfEventSessionGroupListDto>());
        _eventSessionService.GetEventSessionCreateContextAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => CreateSessionContext(call.ArgAt<Guid>(0)));
        _eventSessionService.AssignSessionToGroupAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<int>())
            .Returns(new BaseCommandResponseOfGuid { Success = true, Id = Guid.NewGuid() });
        _eventSessionService.UnassignSessionFromGroupAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>())
            .Returns(new BaseCommandResponseOfGuid { Success = true, Id = Guid.NewGuid() });
    }

    [Test]
    public async Task OnInitializedAsync_WhenRenderedFromBlazouterUrl_UsesEventAndSessionIdsFromCurrentUri()
    {
        var eventId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"/events/{eventId}/sessions/{sessionId}/edit");

        _eventService.GetEventByIdAsync(eventId).Returns(CreateDraftEvent(eventId));
        _eventSessionService.GetManagedSessionByIdAsync(eventId, sessionId).Returns(CreateSession(eventId, sessionId, canEdit: true));

        var cut = _ctx.Render<EditSession>();
        cut.WaitForState(() => !cut.Markup.Contains("Loading session", StringComparison.Ordinal));

        await _eventService.Received(1).GetEventByIdAsync(eventId);
        await _eventSessionService.Received(1).GetManagedSessionByIdAsync(eventId, sessionId);
        await _eventSessionService.DidNotReceive().GetSessionByIdAsync(Arg.Any<Guid>());
        await _locationService.DidNotReceive().GetAllLocationsAsync();
        await _locationRoomService.DidNotReceive().GetRoomsByLocationAsync(Arg.Any<Guid>());
        await Assert.That(cut.Markup).DoesNotContain("The program item could not be loaded.");
    }

    [Test]
    public async Task Render_UsesProgramItemDefaultCopy()
    {
        var eventId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        _eventService.GetEventByIdAsync(eventId).Returns(CreateDraftEvent(eventId));
        _eventSessionService.GetManagedSessionByIdAsync(eventId, sessionId).Returns(CreateSession(eventId, sessionId, canEdit: true));

        var cut = _ctx.Render<EditSession>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.SessionId, sessionId));
        cut.WaitForState(() => cut.Markup.Contains("Edit program item", StringComparison.Ordinal));

        await Assert.That(cut.Markup).Contains("Program item title");
        await Assert.That(cut.Markup).Contains("Program item date");
        await Assert.That(cut.Markup).Contains("Registration mode");
        await Assert.That(cut.Markup).Contains("Open");
        await Assert.That(cut.Markup).Contains("This dedicated composer edits the saved program item without reopening the event shell drawer.");
        await Assert.That(cut.Markup).Contains("Program items are sessions, not child events.");
        await Assert.That(cut.Markup).DoesNotContain("Edit session");
    }

    [Test]
    public async Task SaveSessionAsync_UpdatesSessionAndReturnsToDraft()
    {
        var eventId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var roomId = Guid.NewGuid();
        _eventService.GetEventByIdAsync(eventId).Returns(CreateDraftEvent(eventId));
        _locationService.GetAllLocationsAsync().Returns(new List<LocationListDto>
        {
            new() { Id = locationId, FullName = "Workshop Room", City = "Antwerp", Country = "Belgium" }
        });
        _locationRoomService.GetRoomsByLocationAsync(locationId).Returns(new List<LocationRoomListDto>
        {
            new() { Id = roomId, LocationId = locationId, Name = "Breakout A", Capacity = 50 }
        });
        _eventSessionService.GetManagedSessionByIdAsync(eventId, sessionId).Returns(CreateSession(eventId, sessionId, canEdit: true, locationId: locationId, roomId: roomId));
        _eventSessionService.GetEventSessionCreateContextAsync(eventId, Arg.Any<CancellationToken>())
            .Returns(CreateSessionContext(eventId, locationId, roomId));
        _eventSessionService.UpdateSessionAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<UpdateEventSessionDto>()).Returns(new BaseCommandResponseOfGuid
        {
            Success = true,
            Id = sessionId
        });

        var cut = _ctx.Render<EditSession>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.SessionId, sessionId));
        var session = GetPrivateField<UpdateEventSessionDto>(cut.Instance, "_session");
        session.Title!.Value!.Value = "Updated workshop";
        session.Description!.Value!.Value = "Updated practical session.";
        session.Location!.Value!.Value = locationId;
        session.Room!.Value!.Value = roomId;
        session.Kind!.Value!.Value = 2;
        session.MaxAudienceAttendees!.Value!.Value = 50;
        session.RegistrationMode!.Value!.Value = 2;
        SetPrivateField(cut.Instance, "_sessionDate", new DateTime(2026, 7, 3));
        SetPrivateField(cut.Instance, "_startTime", new TimeSpan(14, 0, 0));
        SetPrivateField(cut.Instance, "_endTime", new TimeSpan(15, 30, 0));

        await cut.InvokeAsync(() => InvokePrivateAsync(cut.Instance, "SaveSessionAsync"));

        await _eventSessionService.Received(1).UpdateSessionAsync(
            sessionId,
            sessionId,
            Arg.Is<UpdateEventSessionDto>(dto =>
                dto.Event!.EventId == eventId
                && dto.Title!.Value!.Value == "Updated workshop"
                && dto.Description!.Value!.Value == "Updated practical session."
                && dto.Slug!.Value!.Value == "original-session"
                && dto.Location!.Value!.Value == locationId
                && dto.Room!.Value!.Value == roomId
                && dto.Kind!.Value!.Value == 2
                && dto.MaxAudienceAttendees!.Value!.Value == 50
                && dto.RegistrationMode!.Value!.Value == 2
                && dto.IslamicAspect!.Value!.Value!.ReferencePrayer == (PrayerTime)2
                && dto.IslamicAspect.Value.Value.RequiresWudu == true
                && dto.Schedule!.StartTime!.Value == new DateTimeOffset(2026, 7, 3, 12, 0, 0, TimeSpan.Zero)
                && dto.Schedule.EndTime!.Value == new DateTimeOffset(2026, 7, 3, 13, 30, 0, TimeSpan.Zero)
                && dto.Schedule.StartTime.Value.Value.Offset == TimeSpan.Zero
                && dto.Schedule.EndTime.Value.Value.Offset == TimeSpan.Zero));
        await Assert.That(_ctx.Services.GetRequiredService<NavigationManager>().Uri.EndsWith($"/events/{eventId}/edit?programUpdated=1", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task SaveSessionAsync_WhenProgramSectionChanges_AssignsSessionToSelectedGroup()
    {
        var eventId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        _eventService.GetEventByIdAsync(eventId).Returns(CreateDraftEvent(eventId));
        _eventSessionService.GetManagedSessionByIdAsync(eventId, sessionId).Returns(CreateSession(eventId, sessionId, canEdit: true));
        _eventSessionService.GetManagedSessionGroupsByEventAsync(eventId).Returns(new List<HalResourceOfEventSessionGroupListDto>
        {
            new() { Id = groupId, EventId = eventId, Name = "Workshop track", SortOrder = 1, TenantId = Guid.NewGuid() }
        });
        _eventSessionService.UpdateSessionAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<UpdateEventSessionDto>()).Returns(new BaseCommandResponseOfGuid
        {
            Success = true,
            Id = sessionId
        });
        _eventSessionService.AssignSessionToGroupAsync(eventId, groupId, sessionId, true, 0).Returns(new BaseCommandResponseOfGuid
        {
            Success = true,
            Id = Guid.NewGuid()
        });

        var cut = _ctx.Render<EditSession>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.SessionId, sessionId));
        var session = GetPrivateField<UpdateEventSessionDto>(cut.Instance, "_session");
        session.Title!.Value!.Value = "Updated workshop";
        SetPrivateField(cut.Instance, "_selectedSessionGroupId", (Guid?)groupId);

        await cut.InvokeAsync(() => InvokePrivateAsync(cut.Instance, "SaveSessionAsync"));

        await _eventSessionService.Received(1).AssignSessionToGroupAsync(eventId, groupId, sessionId, true, 0);
        await Assert.That(_ctx.Services.GetRequiredService<NavigationManager>().Uri.EndsWith($"/events/{eventId}/edit?programUpdated=1", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task SaveSessionAsync_WhenProgramSectionCleared_UnassignsExistingGroup()
    {
        var eventId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        _eventService.GetEventByIdAsync(eventId).Returns(CreateDraftEvent(eventId));
        _eventSessionService.GetManagedSessionByIdAsync(eventId, sessionId).Returns(CreateSession(eventId, sessionId, canEdit: true, primaryGroupId: groupId));
        _eventSessionService.GetManagedSessionGroupsByEventAsync(eventId).Returns(new List<HalResourceOfEventSessionGroupListDto>
        {
            new() { Id = groupId, EventId = eventId, Name = "Workshop track", SortOrder = 1, TenantId = Guid.NewGuid() }
        });
        _eventSessionService.UpdateSessionAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<UpdateEventSessionDto>()).Returns(new BaseCommandResponseOfGuid
        {
            Success = true,
            Id = sessionId
        });
        _eventSessionService.UnassignSessionFromGroupAsync(eventId, groupId, sessionId).Returns(new BaseCommandResponseOfGuid
        {
            Success = true,
            Id = sessionId
        });

        var cut = _ctx.Render<EditSession>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.SessionId, sessionId));
        var session = GetPrivateField<UpdateEventSessionDto>(cut.Instance, "_session");
        session.Title!.Value!.Value = "Updated workshop";
        SetPrivateField(cut.Instance, "_selectedSessionGroupId", (Guid?)null);

        await cut.InvokeAsync(() => InvokePrivateAsync(cut.Instance, "SaveSessionAsync"));

        await _eventSessionService.Received(1).UnassignSessionFromGroupAsync(eventId, groupId, sessionId);
        await _eventSessionService.DidNotReceive().AssignSessionToGroupAsync(eventId, groupId, sessionId, true, 0);
        await Assert.That(_ctx.Services.GetRequiredService<NavigationManager>().Uri.EndsWith($"/events/{eventId}/edit?programUpdated=1", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task SaveSessionAsync_WhenEditLinkIsMissing_DoesNotCallApi()
    {
        var eventId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        _eventService.GetEventByIdAsync(eventId).Returns(CreateDraftEvent(eventId));
        _eventSessionService.GetManagedSessionByIdAsync(eventId, sessionId).Returns(CreateSession(eventId, sessionId, canEdit: false));

        var cut = _ctx.Render<EditSession>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.SessionId, sessionId));

        await cut.InvokeAsync(async () => await InvokePrivateAsync(cut.Instance, "SaveSessionAsync"));

        await _eventSessionService.DidNotReceive().UpdateSessionAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<UpdateEventSessionDto>());
        var errorMessage = GetSubmitError(cut.Instance);
        await Assert.That(errorMessage).IsEqualTo("You do not currently have permission to edit this program item.");
    }

    [Test]
    public async Task SaveSessionAsync_WhenSessionBelongsToDifferentEvent_DoesNotCallApi()
    {
        var eventId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        _eventService.GetEventByIdAsync(eventId).Returns(CreateDraftEvent(eventId));
        _eventSessionService.GetManagedSessionByIdAsync(eventId, sessionId).Returns(CreateSession(Guid.NewGuid(), sessionId, canEdit: true));

        var cut = _ctx.Render<EditSession>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.SessionId, sessionId));

        await cut.InvokeAsync(async () => await InvokePrivateAsync(cut.Instance, "SaveSessionAsync"));

        await _eventSessionService.DidNotReceive().UpdateSessionAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<UpdateEventSessionDto>());
        var errorMessage = GetSubmitError(cut.Instance);
        await Assert.That(errorMessage).IsEqualTo("You do not currently have permission to edit this program item.");
    }

    [Test]
    public async Task SaveSessionAsync_WithValidationProblemDetails_MapsServerErrorsIntoEditContext()
    {
        var eventId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        _eventService.GetEventByIdAsync(eventId).Returns(CreateDraftEvent(eventId));
        _eventSessionService.GetManagedSessionByIdAsync(eventId, sessionId).Returns(CreateSession(eventId, sessionId, canEdit: true));
        _eventSessionService.UpdateSessionAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<UpdateEventSessionDto>())
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

        var cut = _ctx.Render<EditSession>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.SessionId, sessionId));

        await cut.InvokeAsync(async () => await InvokePrivateAsync(cut.Instance, "SaveSessionAsync"));

        await Assert.That(GetSubmitError(cut.Instance)).IsEqualTo("Please fix the validation errors below.");
        await Assert.That(GetValidationMessages(cut.Instance)).Contains("Use a clearer program item title.");
    }

    [Test]
    public async Task SaveSessionAsync_WithUnexpectedException_DoesNotEchoRawExceptionMessage()
    {
        const string rawProviderMessage = "provider rejected <script>alert(1)</script> secret";
        var eventId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        _eventService.GetEventByIdAsync(eventId).Returns(CreateDraftEvent(eventId));
        _eventSessionService.GetManagedSessionByIdAsync(eventId, sessionId).Returns(CreateSession(eventId, sessionId, canEdit: true));
        _eventSessionService.UpdateSessionAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<UpdateEventSessionDto>())
            .ThrowsAsync(new InvalidOperationException(rawProviderMessage));

        var cut = _ctx.Render<EditSession>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.SessionId, sessionId));

        await cut.InvokeAsync(async () => await InvokePrivateAsync(cut.Instance, "SaveSessionAsync"));

        var submitError = GetSubmitError(cut.Instance);
        await Assert.That(submitError).IsEqualTo("Program item could not be saved. Please try again.");
        await Assert.That(submitError).DoesNotContain(rawProviderMessage);
        await Assert.That(submitError).DoesNotContain("<script>");
    }

    [Test]
    public async Task OnLocationChangedAsync_WhenLocationChanges_ClearsStaleRoom()
    {
        var eventId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var originalLocationId = Guid.NewGuid();
        var originalRoomId = Guid.NewGuid();
        var newLocationId = Guid.NewGuid();
        _eventService.GetEventByIdAsync(eventId).Returns(CreateDraftEvent(eventId));
        _eventSessionService.GetManagedSessionByIdAsync(eventId, sessionId).Returns(CreateSession(eventId, sessionId, canEdit: true, originalLocationId, originalRoomId));
        _eventSessionService.GetEventSessionCreateContextAsync(eventId, Arg.Any<CancellationToken>())
            .Returns(CreateSessionContext(eventId, originalLocationId, originalRoomId, newLocationId));
        _locationRoomService.GetRoomsByLocationAsync(originalLocationId).Returns(new List<LocationRoomListDto>
        {
            new() { Id = originalRoomId, LocationId = originalLocationId, Name = "Room A" }
        });
        _locationRoomService.GetRoomsByLocationAsync(newLocationId).Returns(new List<LocationRoomListDto>
        {
            new() { Id = Guid.NewGuid(), LocationId = newLocationId, Name = "Room B" }
        });

        var cut = _ctx.Render<EditSession>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.SessionId, sessionId));
        var session = GetPrivateField<UpdateEventSessionDto>(cut.Instance, "_session");
        await Assert.That(session.Room?.Value?.Value).IsEqualTo(originalRoomId);

        await InvokePrivateAsync(cut.Instance, "OnLocationChangedAsync", newLocationId);

        await Assert.That(session.Location?.Value?.Value).IsEqualTo(newLocationId);
        await Assert.That(session.Room?.Value?.Value).IsNull();
    }

    public void Dispose() => _ctx.Dispose();

    private static EventDto CreateDraftEvent(Guid eventId) => new()
    {
        Id = eventId,
        TenantId = Guid.NewGuid(),
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

    private static EventSessionDto CreateSession(
        Guid eventId,
        Guid sessionId,
        bool canEdit,
        Guid? locationId = null,
        Guid? roomId = null,
        Guid? primaryGroupId = null)
    {
        var dto = new EventSessionDto
        {
            Id = sessionId,
            ConcurrencyStamp = sessionId,
            EventId = eventId,
            EventTitle = "Program launch",
            Title = "Original session",
            Description = "Original description.",
            Slug = "original-session",
            StartTime = DateTimeOffset.Parse("2026-07-03T09:00:00+00:00"),
            EndTime = DateTimeOffset.Parse("2026-07-03T10:00:00+00:00"),
            LocationId = locationId,
            RoomId = roomId,
            EventSessionKindId = 2,
            EventSessionKindFullName = "Workshop",
            EventSessionKindMasterCode = "WORKSHOP",
            MaxAudienceAttendees = 25,
            RegistrationModeId = 1,
            IslamicAspect = new EventSessionIslamicAspectDto
            {
                StartTimeType = SessionStartTimeType.RelativeToPrayer,
                ReferencePrayer = (PrayerTime)2,
                OffsetMinutes = 15,
                RequiresWudu = true,
                RitualRequirementsJson = "{\"note\":\"Bring prayer mat\"}"
            },
            TenantId = Guid.NewGuid(),
            SessionGroups = primaryGroupId.HasValue
                ? new List<SessionGroups>
                {
                    new()
                    {
                        EventSessionGroupId = primaryGroupId,
                        Name = "Workshop track",
                        IsPrimary = true,
                        SortOrder = 0
                    }
                }
                : null,
            AdditionalProperties = new Dictionary<string, object>()
        };

        if (canEdit)
        {
            using var doc = JsonDocument.Parse(
                "{\"self\":{\"href\":\"/api/event-session/1\"},\"edit\":{\"href\":\"/api/event-session/1\",\"method\":\"PUT\"}}");
            dto.AdditionalProperties["_links"] = doc.RootElement.Clone();
        }

        return dto;
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

    private static EventSessionCreateContextDto CreateSessionContext(
        Guid eventId,
        Guid? locationId = null,
        Guid? roomId = null,
        Guid? additionalLocationId = null)
        => new()
        {
            EventId = eventId,
            EventTitle = "Program launch",
            TenantId = Guid.NewGuid(),
            TimeZoneId = "Europe/Brussels",
            EventStartDate = new DateTimeOffset(2026, 7, 3, 0, 0, 0, TimeSpan.Zero),
            EventEndDate = new DateTimeOffset(2026, 7, 4, 0, 0, 0, TimeSpan.Zero),
            Defaults = new EventSessionCreateDefaultsDto { RegistrationModeId = 1 },
            Locations = new[] { locationId, additionalLocationId }
                .OfType<Guid>()
                .Select(id => new EventSessionCreateLocationOptionDto
                {
                    Id = id,
                    FullName = id == locationId ? "Workshop Room" : "Second Venue",
                    City = "Antwerp",
                    Country = "Belgium"
                })
                .ToList(),
            Rooms = roomId.HasValue && locationId.HasValue
                ? [new EventSessionCreateRoomOptionDto
                {
                    Id = roomId,
                    LocationId = locationId,
                    Name = "Breakout A",
                    Capacity = 50
                }]
                : [],
            SessionGroups = [],
            Notices = []
        };

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
