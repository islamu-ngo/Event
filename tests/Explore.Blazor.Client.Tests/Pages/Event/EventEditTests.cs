using System.Reflection;
using System.Text.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Models.Events;
using Explore.Blazor.Client.Pages.Events;
using Explore.Blazor.Client.Pages.Events.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.Client.Tests.Pages.Event;

public sealed class EventEditTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();

    [Test]
    public async Task AddSession_WhenHalLinkExists_NavigatesToDedicatedSessionComposer()
    {
        var eventId = Guid.NewGuid();
        var eventService = Substitute.For<IEventService>();
        eventService.UpdateEventAsync(eventId, Arg.Any<EventDraftEditModel>()).Returns(new BaseCommandResponseOfGuid
        {
            Success = true,
            Id = eventId
        });
        var component = CreateComponent(eventId, canAddSession: true, eventService);
        InvokePrivate(component, "PopulateFormFromEvent");

        await InvokePrivateAsync(component, "AddSession");

        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        await eventService.Received(1).UpdateEventAsync(eventId, Arg.Any<EventDraftEditModel>());
        await Assert.That(navigation.Uri.EndsWith($"/events/{eventId}/sessions/create", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task AddSession_WhenEventUpdateFails_DoesNotNavigate()
    {
        var eventId = Guid.NewGuid();
        var eventService = Substitute.For<IEventService>();
        eventService.UpdateEventAsync(eventId, Arg.Any<EventDraftEditModel>()).Returns(new BaseCommandResponseOfGuid
        {
            Success = false,
            Message = "Draft could not be saved."
        });
        var component = CreateComponent(eventId, canAddSession: true, eventService);
        InvokePrivate(component, "PopulateFormFromEvent");
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        var originalUri = navigation.Uri;

        await InvokePrivateAsync(component, "AddSession");

        await eventService.Received(1).UpdateEventAsync(eventId, Arg.Any<EventDraftEditModel>());
        await Assert.That(navigation.Uri).IsEqualTo(originalUri);
        await Assert.That(GetSubmitError(component))
            .IsEqualTo("Draft could not be saved.");
    }

    [Test]
    public async Task AddSession_WhenHalLinkIsMissing_DoesNotNavigateAndSetsError()
    {
        var eventId = Guid.NewGuid();
        var component = CreateComponent(eventId, canAddSession: false);
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        var originalUri = navigation.Uri;

        await InvokePrivateAsync(component, "AddSession");

        await Assert.That(navigation.Uri).IsEqualTo(originalUri);
        await Assert.That(GetSubmitError(component))
            .IsEqualTo("You do not currently have permission to add sessions to this event.");
    }

    [Test]
    public async Task PopulateFormFromEvent_WhenFeaturedImageUriMissing_UsesPublicStorageObjectUrl()
    {
        var eventId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var component = CreateComponent(eventId, canAddSession: true);
        var currentEvent = GetPrivateField<EventDto>(component, "currentEvent");
        SetField(component, "currentEvent", currentEvent with { FeaturedImageId = imageId, FeaturedImageUri = null });

        InvokePrivate(component, "PopulateFormFromEvent");

        var imagePreviewUrl = GetPrivateField<string>(component, "imagePreviewUrl");
        await Assert.That(imagePreviewUrl).EndsWith($"/api/storageobject/{imageId}/public");
    }

    [Test]
    public async Task PopulateFormFromEvent_PreservesTypedParticipationConfiguration()
    {
        var eventId = Guid.NewGuid();
        var configuration = new ParticipationConfiguration
        {
            EventId = eventId,
            ConcurrencyStamp = Guid.NewGuid(),
            ParticipationHandlingModeId = 4,
            AdvanceRegistrationObligationId = 3,
            IdentityAccessModeId = 2,
            GuestRecoveryPolicy = GuestRecoveryPolicyEnum.UnverifiedEmailAccepted
        };
        var component = CreateComponent(eventId, canAddSession: true);
        SetField(component, "currentEvent", CreateEvent(
            eventId,
            canAddSession: true,
            participationConfiguration: configuration));

        InvokePrivate(component, "PopulateFormFromEvent");

        var update = GetPrivateField<EventDraftEditModel>(component, "updateDto");
        await Assert.That(update.ParticipationConfiguration).IsSameReferenceAs(configuration);
    }

    [Test]
    public async Task EditSession_WhenSessionHasId_NavigatesToDedicatedSessionEditor()
    {
        var eventId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var component = CreateComponent(eventId, canAddSession: true);
        SetField(component, "sessions", new List<SessionEditorModel>
        {
            new()
            {
                Id = sessionId,
                Title = "Saved session"
            }
        });

        InvokePrivate(component, "EditSession", 0);

        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        await Assert.That(navigation.Uri.EndsWith($"/events/{eventId}/sessions/{sessionId}/edit", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task EditSession_WhenSessionIsUnsaved_DoesNotNavigateAndSetsError()
    {
        var eventId = Guid.NewGuid();
        var component = CreateComponent(eventId, canAddSession: true);
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        var originalUri = navigation.Uri;
        SetField(component, "sessions", new List<SessionEditorModel>
        {
            new()
            {
                Title = "Unsaved session"
            }
        });

        InvokePrivate(component, "EditSession", 0);

        await Assert.That(navigation.Uri).IsEqualTo(originalUri);
        await Assert.That(GetSubmitError(component))
            .IsEqualTo("Save the session before editing it in the dedicated composer.");
    }

    [Test]
    public async Task ShowDuplicateUnavailable_SetsProgressiveDisclosureMessage()
    {
        var component = CreateComponent(Guid.NewGuid(), canAddSession: true);

        InvokePrivate(component, "ShowDuplicateUnavailable");

        await Assert.That(GetSubmitError(component))
            .IsEqualTo("Duplicate session will be available from the dedicated session composer.");
    }

    [Test]
    public async Task OpenProgramSectionsDialogAsync_WhenHalLinkIsMissing_SetsPermissionMessage()
    {
        var component = CreateComponent(Guid.NewGuid(), canAddSession: true, canManageProgramSections: false);

        await InvokePrivateAsync(component, "OpenProgramSectionsDialogAsync");

        await Assert.That(GetSubmitError(component))
            .IsEqualTo("You do not currently have permission to manage program sections for this event.");
    }

    [Test]
    public async Task ProgramSectionsSummary_WhenEmptyGroupsExist_UsesDirectGroupList()
    {
        var component = CreateComponent(Guid.NewGuid(), canAddSession: true, canManageProgramSections: true);
        SetField(component, "_programSummary", new EventProgramSummaryDto
        {
            Sections = new List<EventProgramSectionDto>()
        });
        SetField(component, "_programSections", new List<HalResourceOfEventSessionGroupListDto>
        {
            new() { Id = Guid.NewGuid(), EventId = Guid.NewGuid(), Name = "Main stage", SortOrder = 0, IsPublished = true },
            new() { Id = Guid.NewGuid(), EventId = Guid.NewGuid(), Name = "Workshop track", SortOrder = 10, IsPublished = true }
        });

        await Assert.That(GetPrivateProperty<string>(component, "ProgramSectionsSummary"))
            .IsEqualTo("2 sections or tracks");
    }

    [Test]
    public async Task ProgramItemsDescription_WhenSessionHasMetadata_SummarizesComposerDetails()
    {
        var locationId = Guid.NewGuid();
        var component = CreateComponent(Guid.NewGuid(), canAddSession: true);
        SetField(component, "locations", new List<EventSessionCreateLocationOptionDto>
        {
            new()
            {
                Id = locationId,
                FullName = "Main Hall"
            }
        });
        SetField(component, "registrationModes", new List<RegistrationModeListDto>
        {
            new()
            {
                Id = 2,
                FullName = "Approval required",
                MasterCode = "APPROVAL_REQUIRED"
            }
        });
        SetField(component, "sessions", new List<SessionEditorModel>
        {
            new()
            {
                Title = "Opening talk",
                StartTime = new DateTime(2026, 7, 3, 9, 0, 0),
                EndTime = new DateTime(2026, 7, 3, 10, 15, 0),
                LocationId = locationId,
                MaxAudienceAttendees = 120,
                RegistrationModeId = 2
            }
        });

        var description = GetPrivateProperty<string>(component, "ProgramItemsDescription");

        await Assert.That(description).IsEqualTo("Fri 3 Jul, 09:00–10:15 · Main Hall · 120 seats · Approval required");
    }

    [Test]
    public async Task ProgramSummary_WhenServerSummaryExists_UsesServerBackedProgramDetails()
    {
        var sessionId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var component = CreateComponent(Guid.NewGuid(), canAddSession: true);
        SetField(component, "_programSummary", new EventProgramSummaryDto
        {
            EventTitle = "Program launch",
            Sections = new List<EventProgramSectionDto>
            {
                new()
                {
                    SectionKey = "main-stage",
                    Title = "Main stage",
                    SessionGroups = new List<EventProgramSessionGroupSectionDto>
                    {
                        new()
                        {
                            SessionGroupId = groupId,
                            Title = "Keynotes",
                            Days = new List<EventProgramDayGroupDto>
                            {
                                new()
                                {
                                    LocalDate = new DateTimeOffset(2026, 7, 3, 0, 0, 0, TimeSpan.Zero),
                                    DisplayLabel = "Fri 3 Jul",
                                    Items = new List<EventProgramItemDto>
                                    {
                                        new()
                                        {
                                            SessionId = sessionId,
                                            Title = "Opening keynote",
                                            LocalDate = new DateTimeOffset(2026, 7, 3, 0, 0, 0, TimeSpan.Zero),
                                            LocalStartTime = new TimeSpan(9, 0, 0),
                                            LocalEndTime = new TimeSpan(10, 15, 0),
                                            RoomName = "Auditorium",
                                            Capacity = 250,
                                            RegistrationModeName = "Open registration"
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        });

        await Assert.That(GetPrivateProperty<string>(component, "ProgramSummary"))
            .IsEqualTo("1 program item saved across 1 program section.");
        await Assert.That(GetPrivateProperty<string>(component, "ProgramItemsSummary"))
            .IsEqualTo("1 session saved");
        await Assert.That(GetPrivateProperty<string>(component, "ProgramItemsDescription"))
            .IsEqualTo("Fri 3 Jul, 09:00–10:15 · Auditorium · 250 seats · Open registration");
        await Assert.That(GetPrivateProperty<string>(component, "ProgramSectionsSummary"))
            .IsEqualTo("1 section or track");
    }

    [Test]
    public async Task HandleSubmit_WhenEventUpdateSucceeds_DoesNotPersistSessionsFromShell()
    {
        var eventId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        const int registrationPolicyId = 3;
        var eventService = Substitute.For<IEventService>();
        eventService.UpdateEventAsync(eventId, Arg.Any<EventDraftEditModel>()).Returns(new BaseCommandResponseOfGuid
        {
            Success = true,
            Id = eventId
        });
        var component = CreateComponent(eventId, canAddSession: true, eventService);
        SetField(component, "currentEvent", CreateEvent(eventId, canAddSession: true, registrationPolicyId));
        InvokePrivate(component, "PopulateFormFromEvent");
        SetField(component, "sessions", new List<SessionEditorModel>
        {
            new()
            {
                Id = sessionId,
                Title = "Saved session",
                StartTime = new DateTime(2026, 7, 3, 9, 0, 0),
                EndTime = new DateTime(2026, 7, 3, 10, 0, 0),
                RegistrationModeId = 1
            },
            new()
            {
                Title = "Unsaved session",
                StartTime = new DateTime(2026, 7, 3, 11, 0, 0),
                EndTime = new DateTime(2026, 7, 3, 12, 0, 0),
                RegistrationModeId = 1
            }
        });

        await InvokePrivateAsync(component, "HandleSubmit");

        await eventService.Received(1).UpdateEventAsync(
            eventId,
            Arg.Is<EventDraftEditModel>(dto =>
                dto.RegistrationPolicyId == registrationPolicyId
                && dto.ExpectedConcurrencyStamp == GetPrivateField<EventDto>(component, "currentEvent").ConcurrencyStamp));
    }

    [Test]
    public async Task HandleSubmit_WhenEventUpdateIsStale_ShowsRefreshMessage()
    {
        var eventId = Guid.NewGuid();
        var eventService = Substitute.For<IEventService>();
        eventService.UpdateEventAsync(eventId, Arg.Any<EventDraftEditModel>()).Returns(new BaseCommandResponseOfGuid
        {
            Success = false,
            Id = eventId,
            Message = "The event draft changed since it was loaded. Refresh the event and try again.",
            Errors = ["Refresh the event and try again."],
            FailureCode = "event_concurrency_conflict"
        });
        var component = CreateComponent(eventId, canAddSession: true, eventService);
        InvokePrivate(component, "PopulateFormFromEvent");

        await InvokePrivateAsync(component, "HandleSubmit");

        await Assert.That(GetSubmitError(component))
            .Contains("The event draft changed since it was loaded");
    }

    [Test]
    public async Task HandleSubmit_WithValidationProblemDetails_MapsServerErrorsIntoEditContext()
    {
        var eventId = Guid.NewGuid();
        var eventService = Substitute.For<IEventService>();
        eventService.UpdateEventAsync(eventId, Arg.Any<EventDraftEditModel>())
            .ThrowsAsync(new ApiException<ValidationProblemDetails>(
                "Bad Request",
                400,
                string.Empty,
                new Dictionary<string, IEnumerable<string>>(),
                new ValidationProblemDetails
                {
                    Errors = new Dictionary<string, ICollection<string>>
                    {
                        ["Title.Value"] = new[] { "Use a clearer event title." }
                    }
                },
                null));
        var component = CreateComponent(eventId, canAddSession: true, eventService);
        InvokePrivate(component, "PopulateFormFromEvent");

        await InvokePrivateAsync(component, "HandleSubmit");

        await Assert.That(GetSubmitError(component)).IsEqualTo("Please fix the validation errors below.");
        await Assert.That(GetValidationMessages(component)).Contains("Use a clearer event title.");
    }

    [Test]
    public async Task HandleSubmit_WithUnexpectedException_DoesNotEchoRawExceptionMessage()
    {
        const string rawProviderMessage = "provider rejected <script>alert(1)</script> secret";
        var eventId = Guid.NewGuid();
        var eventService = Substitute.For<IEventService>();
        eventService.UpdateEventAsync(eventId, Arg.Any<EventDraftEditModel>())
            .ThrowsAsync(new InvalidOperationException(rawProviderMessage));
        var component = CreateComponent(eventId, canAddSession: true, eventService);
        InvokePrivate(component, "PopulateFormFromEvent");

        await InvokePrivateAsync(component, "HandleSubmit");

        var submitError = GetSubmitError(component);
        await Assert.That(submitError).IsEqualTo("Event could not be updated. Please try again.");
        await Assert.That(submitError).DoesNotContain(rawProviderMessage);
        await Assert.That(submitError).DoesNotContain("<script>");
    }

    public void Dispose() => _ctx.Dispose();

    private EventEdit CreateComponent(
        Guid eventId,
        bool canAddSession,
        IEventService? eventService = null,
        bool canManageProgramSections = false)
    {
        var component = new EventEdit();
        SetProperty(component, "EventService", eventService ?? Substitute.For<IEventService>());
        SetProperty(component, "Navigation", _ctx.Services.GetRequiredService<NavigationManager>());
        SetProperty(component, "Logger", Substitute.For<ILogger<EventEdit>>());
        SetProperty(component, "EventId", eventId);
        SetField(component, "currentEvent", CreateEvent(eventId, canAddSession, canManageProgramSections: canManageProgramSections));
        return component;
    }

    private static EventDto CreateEvent(
        Guid eventId,
        bool canAddSession,
        int? registrationPolicyId = null,
        bool canManageProgramSections = false,
        ParticipationConfiguration? participationConfiguration = null)
    {
        var dto = new EventDto
        {
            Id = eventId,
            TenantId = Guid.NewGuid(),
            ConcurrencyStamp = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Title = "Program launch",
            ActorDisplayName = "ISLAMU",
            ActorTypeFullName = "Organization",
            EventStatusFullName = "Draft",
            EventStatusMasterCode = "DRAFT",
            VisibilityTypeFullName = "Public",
            VisibilityTypeMasterCode = "PUBLIC",
            EventFormatFullName = "In person",
            EventFormatMasterCode = "IN_PERSON",
            RegistrationPolicyId = registrationPolicyId,
            ParticipationConfiguration = participationConfiguration,
            AdditionalProperties = new Dictionary<string, object>()
        };

        if (canAddSession || canManageProgramSections)
        {
            var links = new Dictionary<string, object>
            {
                ["self"] = new { href = "/api/event/1" }
            };

            if (canAddSession)
                links["add-session"] = new { href = "/api/event-session", method = "POST" };

            if (canManageProgramSections)
                links["add-session-group"] = new { href = "/api/event-session-group", method = "POST" };

            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(links));
            dto.AdditionalProperties["_links"] = doc.RootElement.Clone();
        }

        return dto;
    }

    private static void InvokePrivate(object instance, string methodName, params object[] args)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Method {methodName} was not found.");
        method.Invoke(instance, args.Length == 0 ? null : args);
    }

    private static async Task InvokePrivateAsync(object instance, string methodName)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Method {methodName} was not found.");
        if (method.Invoke(instance, null) is Task task)
        {
            await task;
        }
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field {fieldName} was not found.");
        return (T)(field.GetValue(instance) ?? throw new InvalidOperationException($"Field {fieldName} was null."));
    }

    private static string GetSubmitError(object instance)
    {
        var submitState = GetPrivateField<Explore.Blazor.Client.Components.Forms.FormSubmitState>(instance, "_submitState");
        return submitState.ErrorMessage ?? throw new InvalidOperationException("Submit state error message was not set.");
    }

    private static IReadOnlyList<string> GetValidationMessages(object instance)
    {
        var editContext = GetPrivateField<EditContext>(instance, "_editContext");
        return editContext.GetValidationMessages().ToList();
    }

    private static T GetPrivateProperty<T>(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Property {propertyName} was not found.");
        return (T)(property.GetValue(instance) ?? throw new InvalidOperationException($"Property {propertyName} was null."));
    }

    private static void SetField<T>(object instance, string fieldName, T value)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field {fieldName} was not found.");
        field.SetValue(instance, value);
    }

    private static void SetProperty<T>(object instance, string propertyName, T value)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Property {propertyName} was not found.");
        property.SetValue(instance, value);
    }
}
