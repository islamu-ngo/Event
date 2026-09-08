using System.Reflection;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Events;
using Explore.Blazor.Client.Pages.Events.Dialogs;
using Explore.Blazor.Client.Services;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Event;

public sealed class ProgramSectionsDialogTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();

    [Test]
    public async Task SaveAsync_WhenCreatingSection_MapsLocationAndRoomToRequest()
    {
        var eventId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var roomId = Guid.NewGuid();
        var eventService = Substitute.For<Explore.Blazor.Client.Contracts.Services.IEventSessionService>();
        eventService.CreateSessionGroupAsync(Arg.Any<CreateEventSessionGroupRequestDto>())
            .Returns(new BaseCommandResponseOfGuid { Success = true, Id = Guid.NewGuid() });
        eventService.GetManagedSessionGroupsByEventAsync(eventId).Returns(new List<HalResourceOfEventSessionGroupListDto>());

        RegisterServices(eventService);

        var cut = _ctx.RenderMudComponent<ProgramSectionsDialog>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.InitialSections, new List<HalResourceOfEventSessionGroupListDto>()));

        SetField(cut.Instance, "_name", "Main stage");
        SetField(cut.Instance, "_description", "Primary talks");
        SetField(cut.Instance, "_locationId", locationId);
        SetField(cut.Instance, "_roomId", roomId);
        SetField(cut.Instance, "_sortOrder", 20);

        await InvokePrivateAsync(cut.Instance, "SaveAsync");

        await eventService.Received(1).CreateSessionGroupAsync(Arg.Is<CreateEventSessionGroupRequestDto>(request =>
            request.EventId == eventId &&
            request.Name == "Main stage" &&
            request.Description == "Primary talks" &&
            request.LocationId == locationId &&
            request.RoomId == roomId &&
            request.SortOrder == 20 &&
            request.IsPublished == true));
    }

    [Test]
    public async Task SaveAsync_WhenUpdatingSection_MapsGroupedRequestAndConcurrencyStamp()
    {
        var eventId = Guid.NewGuid();
        var sectionId = Guid.NewGuid();
        var concurrencyStamp = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var roomId = Guid.NewGuid();
        var eventService = Substitute.For<Explore.Blazor.Client.Contracts.Services.IEventSessionService>();
        eventService.UpdateSessionGroupAsync(
                sectionId,
                concurrencyStamp,
                Arg.Any<UpdateEventSessionGroupRequestDto>())
            .Returns(new BaseCommandResponseOfGuid { Success = true, Id = sectionId });
        eventService.GetManagedSessionGroupsByEventAsync(eventId)
            .Returns(new List<HalResourceOfEventSessionGroupListDto>());
        var section = CreateSection(sectionId, eventId, hasDelete: false, concurrencyStamp: concurrencyStamp);

        RegisterServices(eventService);

        var cut = _ctx.RenderMudComponent<ProgramSectionsDialog>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.InitialSections, new List<HalResourceOfEventSessionGroupListDto> { section }));

        await InvokePrivateAsync(cut.Instance, "EditSectionAsync", section);
        SetField(cut.Instance, "_name", "Main stage");
        SetField(cut.Instance, "_color", "#123456");
        SetField(cut.Instance, "_locationId", locationId);
        SetField(cut.Instance, "_roomId", roomId);
        SetField(cut.Instance, "_sortOrder", 20);
        SetField(cut.Instance, "_isPublished", false);

        await InvokePrivateAsync(cut.Instance, "SaveAsync");

        await eventService.Received(1).UpdateSessionGroupAsync(
            sectionId,
            concurrencyStamp,
            Arg.Is<UpdateEventSessionGroupRequestDto>((UpdateEventSessionGroupRequestDto request) =>
                request.Metadata != null &&
                request.Metadata.Name == "Main stage" &&
                request.Metadata.Description != null &&
                request.Metadata.Description.HasValue == true &&
                request.Metadata.Description.Value == "Existing description" &&
                request.Metadata.Color != null &&
                request.Metadata.Color.Value == "#123456" &&
                request.Placement != null &&
                request.Placement.LocationId != null &&
                request.Placement.LocationId.Value == locationId &&
                request.Placement.RoomId != null &&
                request.Placement.RoomId.Value == roomId &&
                request.Ordering != null &&
                request.Ordering.SortOrder == 20 &&
                request.Publication != null &&
                request.Publication.IsPublished == false));
    }

    [Test]
    public async Task OnLocationChangedAsync_ClearsStaleRoomAndLoadsRoomsForLocation()
    {
        var eventId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var oldRoomId = Guid.NewGuid();
        var newRoomId = Guid.NewGuid();
        var eventService = Substitute.For<Explore.Blazor.Client.Contracts.Services.IEventSessionService>();
        var roomService = Substitute.For<ILocationRoomService>();
        eventService.GetEventSessionCreateContextAsync(eventId).Returns(new EventSessionCreateContextDto
        {
            Locations = [new EventSessionCreateLocationOptionDto { Id = locationId, FullName = "Main Hall" }],
            Rooms =
            [
                new EventSessionCreateRoomOptionDto
                {
                    Id = newRoomId,
                    LocationId = locationId,
                    Name = "Auditorium"
                }
            ]
        });

        RegisterServices(eventService, locationRoomService: roomService);

        var cut = _ctx.RenderMudComponent<ProgramSectionsDialog>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.InitialSections, new List<HalResourceOfEventSessionGroupListDto>()));

        SetField(cut.Instance, "_roomId", oldRoomId);

        await InvokePrivateAsync(cut.Instance, "OnLocationChangedAsync", locationId);

        await eventService.Received(1).GetEventSessionCreateContextAsync(eventId);
        await roomService.DidNotReceive().GetRoomsByLocationAsync(Arg.Any<Guid>());
        await Assert.That(GetField<Guid?>(cut.Instance, "_roomId")).IsNull();
    }

    [Test]
    public async Task DeleteSectionAsync_WhenHalDeleteIsPresent_ConfirmsDeletesAndReloadsSections()
    {
        var eventId = Guid.NewGuid();
        var sectionId = Guid.NewGuid();
        var eventService = Substitute.For<Explore.Blazor.Client.Contracts.Services.IEventSessionService>();
        eventService.DeleteSessionGroupAsync(eventId, sectionId).Returns(true);
        eventService.GetManagedSessionGroupsByEventAsync(eventId).Returns(new List<HalResourceOfEventSessionGroupListDto>());
        var dialogService = Substitute.For<IDialogService>();
        dialogService.ShowMessageBoxAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<DialogOptions>())
            .Returns(true);
        var section = CreateSection(sectionId, eventId, hasDelete: true);

        RegisterServices(eventService: eventService, dialogService: dialogService);

        var cut = _ctx.RenderMudComponent<ProgramSectionsDialog>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.InitialSections, new List<HalResourceOfEventSessionGroupListDto> { section }));

        await InvokePrivateAsync(cut.Instance, "DeleteSectionAsync", section);

        await dialogService.Received(1).ShowMessageBoxAsync(
            "Delete program section?",
            Arg.Is<string>(message => message.Contains("Sessions stay saved", StringComparison.OrdinalIgnoreCase)),
            "Delete",
            Arg.Any<string>(),
            "Cancel",
            Arg.Any<DialogOptions>());
        await eventService.Received(1).DeleteSessionGroupAsync(eventId, sectionId);
        await eventService.Received(1).GetManagedSessionGroupsByEventAsync(eventId);
    }

    [Test]
    public async Task DeleteSectionAsync_WhenHalDeleteIsMissing_DoesNotCallService()
    {
        var eventId = Guid.NewGuid();
        var sectionId = Guid.NewGuid();
        var eventService = Substitute.For<Explore.Blazor.Client.Contracts.Services.IEventSessionService>();
        var section = CreateSection(sectionId, eventId, hasDelete: false);

        RegisterServices(eventService: eventService);

        var cut = _ctx.RenderMudComponent<ProgramSectionsDialog>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.InitialSections, new List<HalResourceOfEventSessionGroupListDto> { section }));

        await InvokePrivateAsync(cut.Instance, "DeleteSectionAsync", section);

        await eventService.DidNotReceive().DeleteSessionGroupAsync(Arg.Any<Guid>(), Arg.Any<Guid>());
        await Assert.That(GetField<string?>(cut.Instance, "_errorMessage"))
            .IsEqualTo("You do not currently have permission to delete this program section.");
    }

    [Test]
    public async Task AssignSessionAsync_WhenHalAssignIsPresent_CallsServiceAndReloadsSessions()
    {
        var eventId = Guid.NewGuid();
        var sectionId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var eventService = Substitute.For<Explore.Blazor.Client.Contracts.Services.IEventSessionService>();
        eventService.AssignSessionToGroupAsync(eventId, sectionId, sessionId, true, 10)
            .Returns(new BaseCommandResponseOfGuid { Success = true, Id = Guid.NewGuid() });
        eventService.GetSessionsByEventAsync(eventId, includeManagedSessions: true).Returns(new List<EventSessionListDto>());
        var section = CreateSection(sectionId, eventId, hasDelete: false, hasAssign: true);
        var session = CreateSession(sessionId, "Opening keynote");

        RegisterServices(eventService: eventService);

        var cut = _ctx.RenderMudComponent<ProgramSectionsDialog>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.InitialSections, new List<HalResourceOfEventSessionGroupListDto> { section }));
        SetField(cut.Instance, "_assignmentSection", section);
        SetField(cut.Instance, "_sessions", new List<EventSessionListDto> { session });

        await InvokePrivateAsync(cut.Instance, "AssignSessionAsync", session);

        await eventService.Received(1).AssignSessionToGroupAsync(eventId, sectionId, sessionId, true, 10);
        await eventService.Received(1).GetSessionsByEventAsync(eventId, includeManagedSessions: true);
        await Assert.That(GetField<bool>(cut.Instance, "_hasChanges")).IsTrue();
    }

    [Test]
    public async Task UnassignSessionAsync_WhenHalAssignIsPresent_CallsServiceAndReloadsSessions()
    {
        var eventId = Guid.NewGuid();
        var sectionId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var eventService = Substitute.For<Explore.Blazor.Client.Contracts.Services.IEventSessionService>();
        eventService.UnassignSessionFromGroupAsync(eventId, sectionId, sessionId)
            .Returns(new BaseCommandResponseOfGuid { Success = true, Id = Guid.NewGuid() });
        eventService.GetSessionsByEventAsync(eventId, includeManagedSessions: true).Returns(new List<EventSessionListDto>());
        var section = CreateSection(sectionId, eventId, hasDelete: false, hasAssign: true);
        var session = CreateSession(sessionId, "Workshop", sectionId);

        RegisterServices(eventService: eventService);

        var cut = _ctx.RenderMudComponent<ProgramSectionsDialog>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.InitialSections, new List<HalResourceOfEventSessionGroupListDto> { section }));
        SetField(cut.Instance, "_assignmentSection", section);

        await InvokePrivateAsync(cut.Instance, "UnassignSessionAsync", session);

        await eventService.Received(1).UnassignSessionFromGroupAsync(eventId, sectionId, sessionId);
        await eventService.Received(1).GetSessionsByEventAsync(eventId, includeManagedSessions: true);
        await Assert.That(GetField<bool>(cut.Instance, "_hasChanges")).IsTrue();
    }

    [Test]
    public async Task AssignSessionAsync_WhenHalAssignIsMissing_DoesNotCallService()
    {
        var eventId = Guid.NewGuid();
        var sectionId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var eventService = Substitute.For<Explore.Blazor.Client.Contracts.Services.IEventSessionService>();
        var section = CreateSection(sectionId, eventId, hasDelete: false, hasAssign: false);
        var session = CreateSession(sessionId, "Panel");

        RegisterServices(eventService: eventService);

        var cut = _ctx.RenderMudComponent<ProgramSectionsDialog>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.InitialSections, new List<HalResourceOfEventSessionGroupListDto> { section }));
        SetField(cut.Instance, "_assignmentSection", section);

        await InvokePrivateAsync(cut.Instance, "AssignSessionAsync", session);

        await eventService.DidNotReceive().AssignSessionToGroupAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<int>());
        await Assert.That(GetField<string?>(cut.Instance, "_errorMessage"))
            .IsEqualTo("You do not currently have permission to manage session assignments for this program section.");
    }

    public void Dispose() => _ctx.Dispose();

    private void RegisterServices(
        Explore.Blazor.Client.Contracts.Services.IEventSessionService? eventService = null,
        ILocationService? locationService = null,
        ILocationRoomService? locationRoomService = null,
        IDialogService? dialogService = null)
    {
        var locations = locationService ?? Substitute.For<ILocationService>();
        locations.GetAllLocationsAsync().Returns(new List<LocationListDto>());

        var rooms = locationRoomService ?? Substitute.For<ILocationRoomService>();
        rooms.GetRoomsByLocationAsync(Arg.Any<Guid>()).Returns(new List<LocationRoomListDto>());

        _ctx.Services.AddSingleton(eventService ?? Substitute.For<Explore.Blazor.Client.Contracts.Services.IEventSessionService>());
        _ctx.Services.AddSingleton(locations);
        _ctx.Services.AddSingleton(rooms);
        _ctx.Services.AddSingleton(dialogService ?? Substitute.For<IDialogService>());
    }

    private static HalResourceOfEventSessionGroupListDto CreateSection(
        Guid sectionId,
        Guid eventId,
        bool hasDelete,
        bool hasAssign = false,
        Guid? concurrencyStamp = null)
    {
        var section = new HalResourceOfEventSessionGroupListDto
        {
            Id = sectionId,
            EventId = eventId,
            Name = "Main stage",
            Description = "Existing description",
            SortOrder = 10,
            IsPublished = true,
            ConcurrencyStamp = concurrencyStamp ?? Guid.NewGuid(),
            _links = new Dictionary<string, HalLink>
            {
                ["self"] = new() { Href = $"/api/event-session-group/{sectionId}", Method = "GET" },
                ["edit"] = new() { Href = $"/api/event-session-group/{sectionId}", Method = "PATCH" }
            }
        };

        if (hasDelete)
        {
            section._links["delete"] = new()
            {
                Href = $"/api/event-session-group/{sectionId}?eventId={eventId}",
                Method = "DELETE"
            };
        }

        if (hasAssign)
        {
            section._links["assign-session"] = new()
            {
                Href = $"/api/event-session-group/{sectionId}/sessions",
                Method = "POST"
            };
        }

        return section;
    }

    private static EventSessionListDto CreateSession(Guid sessionId, string title, Guid? assignedSectionId = null)
    {
        var session = new EventSessionListDto
        {
            Id = sessionId,
            Title = title,
            StartTime = new DateTimeOffset(2026, 7, 3, 9, 0, 0, TimeSpan.Zero),
            SessionGroups = new List<SessionGroups2>()
        };

        if (assignedSectionId.HasValue)
        {
            session.SessionGroups.Add(new SessionGroups2
            {
                EventSessionGroupId = assignedSectionId,
                Name = "Main stage",
                IsPrimary = true,
                SortOrder = 10
            });
        }

        return session;
    }

    private static async Task InvokePrivateAsync(object instance, string methodName, params object[] args)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Method {methodName} was not found.");
        if (method.Invoke(instance, args.Length == 0 ? null : args) is Task task)
        {
            await task;
        }
    }

    private static void SetField<T>(object instance, string fieldName, T value)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field {fieldName} was not found.");
        field.SetValue(instance, value);
    }

    private static T? GetField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field {fieldName} was not found.");
        return (T?)field.GetValue(instance);
    }
}
