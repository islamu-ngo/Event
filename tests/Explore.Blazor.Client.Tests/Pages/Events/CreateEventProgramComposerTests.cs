using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Explore.Blazor.Client.Pages.Events;
using Explore.Blazor.Client.Pages.Events.Components;
using Explore.Blazor.Client.Pages.Events.Models;
using Explore.Blazor.Client.Pages.Events.Sessions;
using Explore.Blazor.Client.Pages.Events.Workflows;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Events;

public sealed class CreateEventProgramComposerTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly Guid CreatedId = Guid.Parse("0198c9e0-0000-7000-8000-000000000017");
    private readonly BlazorTestContext _ctx = new();
    private readonly CreationHandler _handler = new();
    private readonly HttpClient _http;

    public CreateEventProgramComposerTests()
    {
        _ctx.JSInterop.SetupVoid("Blazor._internal.NavigationLock.enableNavigationPrompt", _ => true).SetVoidResult();
        _ctx.JSInterop.SetupVoid("Blazor._internal.NavigationLock.disableNavigationPrompt", _ => true).SetVoidResult();
        _ctx.JSInterop.SetupVoid("mudTimePicker.destroyPointerEvents", _ => true).SetVoidResult();
        _http = new HttpClient(_handler) { BaseAddress = new Uri("https://event.test/") };
        _ctx.Services.AddSingleton<IEventService>(new EventService(
            new EventClient(_http),
            new EventLifecycleClient(_http),
            new EventManagementReadClient(_http),
            new EventParticipationClient(_http),
            new EventPublicActionClient(_http),
            NullLogger<EventService>.Instance));
        _ctx.AddMockService<IUserService>();
        _ctx.AddMockService<IEventLookupService>();
        _ctx.AddMockService<IDemographicLookupService>();
        _ctx.AddMockService<ICultureLookupService>();
        _ctx.AddMockService<IImageStorageService>();
        _ctx.AddMockService<IEventRegistrationPolicyService>();
        _ctx.AddMockService<IEventTemplateService>()
            .GetTemplatesAsync(Arg.Any<int?>(), 1, 100, Arg.Any<CancellationToken>())
            .Returns(new HalCollectionResourceOfEventTemplateListDto());
        _ctx.Services.AddSingleton<MainContentAppearanceState>();
    }

    public void Dispose()
    {
        _ctx.Dispose();
        _http.Dispose();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task InvalidInitialTimezone_RetainsProposalUntilExplicitCorrection(bool scheduled)
    {
        var parent = Parent();
        parent.Timezone = "Mars/Olympus";
        parent.Sessions = [new CreateEventGraphSessionDto
        {
            TempKey = "retained-proposal", Title = "Prepared proposal", Description = "Retained metadata",
            StartTime = scheduled ? new DateTimeOffset(2040, 7, 21, 9, 0, 0, TimeSpan.FromHours(2)) : null,
            EndTime = scheduled ? new DateTimeOffset(2040, 7, 21, 10, 0, 0, TimeSpan.FromHours(2)) : null
        }];
        var cut = _ctx.RenderMudComponent<CreateEvent>(p => p.Add(x => x.InitialDraft, parent));
        await Assert.That(Timeline(cut).Instance.Sessions.Single().Title).IsEqualTo("Prepared proposal");
        await Assert.That(Timeline(cut).Instance.Sessions.Single().ExistingStartInstant).IsEqualTo(parent.Sessions.Single().StartTime);
        await Assert.That(cut.Find("[data-testid='initial-timezone-error']").GetAttribute("role")).IsEqualTo("alert");
        await cut.Find("button.create-event__draft-button").ClickAsync(new MouseEventArgs());
        await Assert.That(_handler.Requests).IsEmpty();
        await cut.InvokeAsync(() => cut.FindComponent<MudAutocomplete<TimeZoneInfo>>().Instance.ValueChanged.InvokeAsync(TimeZoneInfo.Utc));
        await Assert.That(cut.FindAll("[data-testid='initial-timezone-error']")).IsEmpty();
        var row = Timeline(cut).Instance.Sessions.Single();
        await Assert.That(row.Description).IsEqualTo("Retained metadata");
        await Assert.That(row.StartTime).IsEqualTo(scheduled ? new DateTime(2040, 7, 21, 7, 0, 0) : null);
        await Assert.That(parent.Timezone).IsEqualTo("Mars/Olympus");
    }

    [Test]
    public async Task TitleOnlyAndEmptyDrafts_DoNotInventDatesOrDays()
    {
        var parent = Parent();
        parent.Title = "  Prepared event  ";
        var row = new SessionEditorModel { Title = "Undecided date" };
        await Assert.That(row.StartTime).IsNull();
        await Assert.That(row.EndTime).IsNull();
        await Assert.That(CreateEventProgramMapper.TryMap(parent, [row], out var request, out _)).IsTrue();
        await Assert.That(request!.Sessions!.Single().StartTime).IsNull();
        await Assert.That(request.Sessions.Single().EndTime).IsNull();
        await Assert.That(request.Days!.Count).IsEqualTo(0);
        await Assert.That(request.Title).IsEqualTo("Prepared event");
        await Assert.That(parent.Title).IsEqualTo("  Prepared event  ");
        await Assert.That(CreateEventProgramMapper.TryMap(parent, [], out request, out _)).IsTrue();
        await Assert.That(request!.Sessions!.Count).IsEqualTo(0);
    }

    [Test]
    public async Task SourceRowsAndClones_RetainMetadataWithoutSharingMutableCollections()
    {
        var imageId = Guid.NewGuid();
        var roomId = Guid.NewGuid();
        var speakerId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var parent = Parent();
        parent.Sessions =
        [
            new CreateEventGraphSessionDto
            {
                TempKey = "source-row",
                Title = "Source item",
                Description = "Details",
                Slug = "source-item",
                DayTempKey = "day-one",
                RoomTempKey = "room-one",
                RoomId = roomId,
                LocationTempKey = "venue",
                FeaturedImageId = imageId,
                SessionTemplateId = templateId,
                EventSessionKindId = 2,
                EndTimeType = SessionEndTimeType.Fixed,
                LanguageIds = [2],
                SpeakerActorIds = [speakerId],
                IslamicAspect = new EventSessionIslamicAspectDto { RequiresWudu = true }
            }
        ];
        var row = CreateEventProgramMapper.FromRequest(parent).Single();
        var edited = row.Clone();
        var duplicate = row.Clone(newLocalId: true);
        edited.LanguageIds = [3];
        edited.IslamicAspect!.RequiresWudu = false;
        await Assert.That(row.LanguageIds.Single()).IsEqualTo(2);
        await Assert.That(row.IslamicAspect!.RequiresWudu).IsTrue();
        await Assert.That(edited.LocalId).IsEqualTo(row.LocalId);
        await Assert.That(duplicate.LocalId == row.LocalId).IsFalse();
        await Assert.That(CreateEventProgramMapper.TryMap(parent, [row, duplicate], out var request, out _)).IsTrue();
        var mapped = request!.Sessions!.First();
        await Assert.That(mapped.RoomId).IsEqualTo(roomId);
        await Assert.That(mapped.FeaturedImageId).IsEqualTo(imageId);
        await Assert.That(mapped.SessionTemplateId).IsEqualTo(templateId);
        await Assert.That(mapped.EventSessionKindId).IsEqualTo(2);
        await Assert.That(mapped.DayTempKey).IsEqualTo("day-one");
        await Assert.That(mapped.RoomTempKey).IsEqualTo("room-one");
        await Assert.That(mapped.LocationTempKey).IsEqualTo("venue");
        await Assert.That(mapped.Slug).IsEqualTo("source-item");
        await Assert.That(mapped.SpeakerActorIds!.Single()).IsEqualTo(speakerId);
        await Assert.That(request.Sessions.Select(x => x.TempKey).Distinct().Count()).IsEqualTo(2);
        parent.Title = "Changed parent";
        parent.Sessions.First().LanguageIds!.Add(9);
        row.Title = "Changed row";
        row.IslamicAspect.RequiresWudu = false;
        await Assert.That(request.Title).IsEqualTo("Prepared event");
        await Assert.That(mapped.Title).IsEqualTo("Source item");
        await Assert.That(mapped.LanguageIds!.Count).IsEqualTo(1);
        await Assert.That(mapped.IslamicAspect!.RequiresWudu).IsTrue();
    }

    [Test]
    public async Task MixedDraftAndEqualStarts_RetainExplicitRowOrderAndOvernightEnd()
    {
        var parent = Parent();
        parent.Timezone = "Europe/Brussels";
        var start = new DateTime(2030, 6, 1, 23, 30, 0);
        var rows = new[]
        {
            new SessionEditorModel { Title = "Unscheduled" },
            new SessionEditorModel { Title = "Night workshop", StartTime = start, EndTime = new DateTime(2030, 6, 2, 1, 0, 0) },
            new SessionEditorModel { Title = "Parallel workshop", StartTime = start, EndTime = new DateTime(2030, 6, 2, 2, 0, 0) }
        };
        await Assert.That(CreateEventProgramMapper.TryMap(parent, rows, out var request, out _)).IsTrue();
        var mapped = request!.Sessions!.ToArray();
        await Assert.That(mapped[0].Title).IsEqualTo("Unscheduled");
        await Assert.That(mapped[1].StartTime).IsEqualTo(new DateTimeOffset(2030, 6, 1, 21, 30, 0, TimeSpan.Zero));
        await Assert.That(mapped[1].EndTime).IsEqualTo(new DateTimeOffset(2030, 6, 1, 23, 0, 0, TimeSpan.Zero));
        await Assert.That(mapped[2].SortOrder).IsEqualTo(3);
        parent.EventStatusId = 2;
        await Assert.That(CreateEventProgramMapper.TryMap(parent, rows, out _, out var error)).IsFalse();
        await Assert.That(error).Contains("Sessions[0]");
    }

    [Test]
    [Arguments("2030-06-01T10:00:00", null, SessionEndTimeType.Fixed)]
    [Arguments(null, "2030-06-01T11:00:00", SessionEndTimeType.Fixed)]
    [Arguments("2030-06-01T10:00:00", "2030-06-01T09:00:00", SessionEndTimeType.Fixed)]
    [Arguments("2030-06-01T10:00:00", "2030-06-01T11:00:00", SessionEndTimeType.OpenEnded)]
    [Arguments(null, null, SessionEndTimeType.RelativeToPrayer)]
    public async Task InvalidSchedules_RetainInputAndReturnIndexedFieldContext(
        string? start, string? end, SessionEndTimeType endType)
    {
        var row = new SessionEditorModel
        {
            Title = "Retained",
            StartTime = start is null ? null : DateTime.Parse(start),
            EndTime = end is null ? null : DateTime.Parse(end),
            EndTimeType = endType
        };
        var before = row.Clone();
        await Assert.That(CreateEventProgramMapper.TryMap(Parent(), [row], out _, out var error)).IsFalse();
        await Assert.That(error).Contains("Sessions[0]");
        await Assert.That(row.StartTime).IsEqualTo(before.StartTime);
        await Assert.That(row.EndTime).IsEqualTo(before.EndTime);
    }

    [Test]
    public async Task TimezoneChange_RevalidatesWallTimeAndKeepsOpenEndedStartOnly()
    {
        var parent = Parent();
        var row = new SessionEditorModel
        {
            Title = "Start only",
            StartTime = new DateTime(2026, 3, 29, 2, 30, 0),
            EndTimeType = SessionEndTimeType.OpenEnded
        };
        await Assert.That(CreateEventProgramMapper.TryMap(parent, [row], out var request, out _)).IsTrue();
        await Assert.That(request!.Sessions!.Single().EndTime).IsNull();
        parent.Timezone = "Europe/Brussels";
        await Assert.That(CreateEventProgramMapper.TryMap(parent, [row], out _, out var error)).IsFalse();
        await Assert.That(error).Contains("Sessions[0].StartTime");
        row.StartTime = new DateTime(2026, 10, 25, 2, 30, 0);
        await Assert.That(CreateEventProgramMapper.TryMap(parent, [row], out _, out _)).IsFalse();
    }

    [Test]
    public async Task PersistedMappers_UseExplicitEndDateAndPreserveSavedOverlapOccurrence()
    {
        var create = new CreateEventSessionDto { Title = "Overnight" };
        await Assert.That(EventSessionFormModelMapper.TryPrepareCreateRequest(
            create, Guid.NewGuid(), Guid.NewGuid(), new DateTime(2030, 6, 1),
            new TimeSpan(23, 0, 0), new TimeSpan(1, 0, 0), out _, "UTC",
            endDate: new DateTime(2030, 6, 2))).IsTrue();
        await Assert.That(create.EndTime).IsEqualTo(new DateTimeOffset(2030, 6, 2, 1, 0, 0, TimeSpan.Zero));
        var eventId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var source = new EventSessionDto
        {
            Id = sessionId,
            EventId = eventId,
            Title = "Overlap",
            StartTime = new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero)
        };
        var update = new UpdateEventSessionDto();
        var state = EventSessionFormModelMapper.PopulateUpdateRequest(update, source, eventId, "Europe/Brussels");
        await Assert.That(state.EndDate).IsEqualTo(new DateTime(2026, 10, 26));
        await Assert.That(EventSessionFormModelMapper.TryPrepareUpdateRequest(
            update, eventId, sessionId, sessionId, state.SessionDate, state.StartTime, state.EndTime,
            out _, "Europe/Brussels", source.StartTime, source.EndTime, endDate: state.EndDate)).IsTrue();
        await Assert.That(update.Schedule!.StartTime!.Value).IsEqualTo(source.StartTime);
        await Assert.That(update.Schedule.EndTime!.Value).IsEqualTo(source.EndTime);
        var unscheduled = EventSessionFormModelMapper.PopulateUpdateRequest(
            new UpdateEventSessionDto(), new EventSessionDto { Title = "No dates" }, eventId);
        await Assert.That(unscheduled.SessionDate).IsNull();
        await Assert.That(unscheduled.StartTime).IsNull();
        await Assert.That(unscheduled.EndDate).IsNull();
    }

    [Test]
    public async Task MixedDayGroups_MoveOnlyWithinDisplayedGroupAndRespectVisibleBoundaries()
    {
        var parent = Parent();
        parent.Sessions =
        [
            new() { Title = "Unscheduled A" },
            new() { Title = "Scheduled B", StartTime = new DateTimeOffset(2030, 6, 1, 9, 0, 0, TimeSpan.Zero), EndTime = new DateTimeOffset(2030, 6, 1, 10, 0, 0, TimeSpan.Zero) },
            new() { Title = "Unscheduled C" },
            new() { Title = "Other day", StartTime = new DateTimeOffset(2030, 6, 2, 9, 0, 0, TimeSpan.Zero), EndTime = new DateTimeOffset(2030, 6, 2, 10, 0, 0, TimeSpan.Zero) }
        ];
        var cut = _ctx.RenderMudComponent<CreateEvent>(p => p.Add(c => c.InitialDraft, parent));
        var dated = cut.FindAll(".schedule-timeline__day")[0];
        await Assert.That(dated.QuerySelector("button[aria-label='Move session earlier']")!.HasAttribute("disabled")).IsTrue();
        await Assert.That(dated.QuerySelector("button[aria-label='Move session later']")!.HasAttribute("disabled")).IsTrue();
        var unscheduled = cut.FindAll(".schedule-timeline__day")[2];
        await Assert.That(unscheduled.QuerySelectorAll("button[aria-label='Move session earlier']")[0].HasAttribute("disabled")).IsTrue();
        await cut.FindAll(".schedule-timeline__day")[2].QuerySelectorAll("button[aria-label='Move session later']")[0].ClickAsync(new MouseEventArgs());
        await Assert.That(cut.FindAll(".schedule-timeline__day")[2].QuerySelectorAll("article")[0].TextContent).Contains("Unscheduled C");
        await Assert.That(cut.FindAll(".schedule-timeline__day")[2].QuerySelectorAll("article")[1].TextContent).Contains("Unscheduled A");
        await Assert.That(Timeline(cut).Instance.Sessions[0].Title).IsEqualTo("Unscheduled C");
        await Assert.That(Timeline(cut).Instance.Sessions[1].Title).IsEqualTo("Scheduled B");
        await Assert.That(Timeline(cut).Instance.Sessions[2].Title).IsEqualTo("Unscheduled A");
        await Assert.That(Timeline(cut).Instance.Sessions[3].Title).IsEqualTo("Other day");
        await Assert.That(_handler.Requests).IsEmpty();
    }

    [Test]
    public async Task LocalEditing_CancelDuplicateMoveDeleteAndSaveAnotherNeverWriteOrNavigate()
    {
        var cut = Render();
        await AddTitleOnly(cut, "First", another: true);
        await SetTitle(cut, "Second");
        await cut.Find("[data-testid='program-save']").ClickAsync(new MouseEventArgs());
        await Assert.That(Timeline(cut).Instance.Sessions.Count).IsEqualTo(2);
        var firstId = Timeline(cut).Instance.Sessions[0].LocalId;
        await cut.InvokeAsync(() => Timeline(cut).Instance.OnEditSession.InvokeAsync(firstId));
        await SetTitle(cut, "Canceled edit");
        await cut.Find("[data-testid='program-cancel']").ClickAsync(new MouseEventArgs());
        await Assert.That(Timeline(cut).Instance.Sessions[0].Title).IsEqualTo("First");
        await cut.InvokeAsync(() => Timeline(cut).Instance.OnDuplicateSession.InvokeAsync(firstId));
        await Assert.That(Timeline(cut).Instance.Sessions.Count).IsEqualTo(3);
        await Assert.That(Timeline(cut).Instance.Sessions[1].LocalId == firstId).IsFalse();
        var duplicateId = Timeline(cut).Instance.Sessions[1].LocalId;
        var neighborId = Timeline(cut).Instance.Sessions[2].LocalId;
        await cut.InvokeAsync(() => Timeline(cut).Instance.OnMoveSession.InvokeAsync((duplicateId, neighborId)));
        await Assert.That(Timeline(cut).Instance.Sessions[1].Title).IsEqualTo("Second");
        await cut.InvokeAsync(() => Timeline(cut).Instance.OnRemoveSession.InvokeAsync(firstId));
        await cut.InvokeAsync(() => Timeline(cut).Instance.OnRemoveSession.InvokeAsync(Timeline(cut).Instance.Sessions[0].LocalId));
        await cut.InvokeAsync(() => Timeline(cut).Instance.OnRemoveSession.InvokeAsync(duplicateId));
        await Assert.That(Timeline(cut).Instance.Sessions.Count).IsEqualTo(0);
        await Assert.That(_handler.Requests.Count).IsEqualTo(0);
        await Assert.That(_ctx.Services.GetRequiredService<NavigationManager>().Uri).IsEqualTo("http://localhost/");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DraftAndPublish_SendOneCompleteSnapshotAndGuardConcurrentSubmit(bool publish)
    {
        var cut = Render();
        await cut.InvokeAsync(() => EventTitle(cut).Instance.ValueChanged.InvokeAsync("Prepared event"));
        await AddScheduled(cut, "First");
        await AddScheduled(cut, "Second");
        var submission = publish
            ? cut.Find("form").SubmitAsync()
            : cut.Find("button.create-event__draft-button").ClickAsync(new MouseEventArgs());
        var submitted = await _handler.Started.Task.WaitAsync(Timeout);
        await cut.Find("form").SubmitAsync();
        await Assert.That(_handler.Requests.Count).IsEqualTo(1);
        await Assert.That(submitted.Sessions!.Count).IsEqualTo(2);
        await Assert.That(submitted.EventStatusId).IsEqualTo(publish ? 2 : 1);
        await Assert.That(submitted.Sessions.Last().Title).IsEqualTo("Second");
        _handler.Response.SetResult(new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(new BaseCommandResponseOfGuid { Success = true, Id = CreatedId })
        });
        await submission.WaitAsync(Timeout);
        await Assert.That(_ctx.Services.GetRequiredService<NavigationManager>().Uri)
            .IsEqualTo($"http://localhost/studio/events/{CreatedId}");
    }

    [Test]
    public async Task DefiniteRejection_RetainsRowsAndPermitsCorrectedExplicitSubmission()
    {
        var cut = Render();
        await cut.InvokeAsync(() => EventTitle(cut).Instance.ValueChanged.InvokeAsync("Prepared event"));
        await AddTitleOnly(cut, "Retained child");
        var submission = cut.Find("button.create-event__draft-button").ClickAsync(new MouseEventArgs());
        await _handler.Started.Task.WaitAsync(Timeout);
        _handler.Response.SetResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(new ValidationProblemDetails
            {
                Status = 400,
                Errors = new Dictionary<string, ICollection<string>> { ["Sessions[0].Title"] = ["Invalid value"] }
            })
        });
        await submission.WaitAsync(Timeout);
        await Assert.That(Timeline(cut).Instance.Sessions.Single().Title).IsEqualTo("Retained child");
        await Assert.That(cut.Find("button.create-event__draft-button").HasAttribute("disabled")).IsFalse();
        await Assert.That(cut.FindAll("[data-testid='program-server-errors'] li").Count).IsEqualTo(1);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task LostResponseOrPostSendCancellation_RetainsInputAndBlocksBlindRecreation(int failure)
    {
        var cut = Render();
        await cut.InvokeAsync(() => EventTitle(cut).Instance.ValueChanged.InvokeAsync("Prepared event"));
        await AddTitleOnly(cut, "Keep this child");
        var submission = cut.Find("button.create-event__draft-button").ClickAsync(new MouseEventArgs());
        await _handler.Started.Task.WaitAsync(Timeout);
        if (failure < 2)
        {
            _handler.Response.SetException(failure == 1
                ? new OperationCanceledException()
                : new HttpRequestException("Response lost"));
        }
        else
        {
            _handler.Response.SetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = failure == 2
                    ? JsonContent.Create(new ProblemDetails { Status = 503, Title = "Response unavailable" })
                    : JsonContent.Create(new BaseCommandResponseOfGuid { Success = false, Message = "Response unavailable" })
            });
        }
        await submission.WaitAsync(Timeout);
        await Assert.That(Timeline(cut).Instance.Sessions.Single().Title).IsEqualTo("Keep this child");
        await Assert.That(cut.Find("[data-testid='create-outcome-unknown']").GetAttribute("role")).IsEqualTo("status");
        await Assert.That(cut.Find("button.create-event__draft-button").HasAttribute("disabled")).IsTrue();
        await cut.Find("form").SubmitAsync();
        await Assert.That(_handler.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ConfirmedSaveWithNavigationFailure_KeepsSafeLinkAndNeverCreatesAgain()
    {
        _ctx.Services.AddSingleton<NavigationManager>(new FailingNavigation());
        var cut = Render();
        await cut.InvokeAsync(() => EventTitle(cut).Instance.ValueChanged.InvokeAsync("Prepared event"));
        var submission = cut.Find("button.create-event__draft-button").ClickAsync(new MouseEventArgs());
        await _handler.Started.Task.WaitAsync(Timeout);
        _handler.Response.SetResult(new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(new BaseCommandResponseOfGuid { Success = true, Id = CreatedId })
        });
        await submission.WaitAsync(Timeout);
        await Assert.That(cut.Find("[data-testid='create-saved-link']").GetAttribute("href"))
            .IsEqualTo($"/studio/events/{CreatedId}");
        await Assert.That(cut.Find("[data-testid='create-saved']").GetAttribute("role")).IsEqualTo("status");
        await cut.Find("form").SubmitAsync();
        await Assert.That(_handler.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SharedForm_ExposesLabeledEndDateAndDoesNotRequireDraftSchedule()
    {
        var cut = _ctx.RenderMudComponent<EventSessionForm>(p => p.Add(x => x.AllowUnscheduled, true));
        await Assert.That(cut.FindComponents<MudDatePicker>().Single(x => x.Instance.Label == "End date").Instance.Required).IsFalse();
        await Assert.That(cut.FindComponents<MudTimePicker>().Single(x => x.Instance.Label == "Start time").Instance.Required).IsFalse();
        await Assert.That(cut.FindComponents<MudTextField<string>>().Single(x => x.Instance.Label == "Program item title").Instance.Required).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PersistedConsumers_SubmitExplicitOvernightEndDateThroughTheSharedForm(bool edit)
    {
        _handler.PersistedSessionMode = true;
        _ctx.Services.AddSingleton<IEventSessionService>(new EventSessionService(
            new EventSessionClient(_http), new EventSessionGroupClient(_http),
            new EventManagementReadClient(_http), NullLogger<EventSessionService>.Instance));

        if (edit)
        {
            var cut = _ctx.RenderMudComponent<EditSession>(p => p
                .Add(x => x.EventId, CreatedId).Add(x => x.SessionId, CreationHandler.SessionId));
            await cut.InvokeAsync(() => cut.FindComponent<EventSessionForm>().Instance.EndDateChanged.InvokeAsync(new DateTime(2030, 6, 2)));
            await cut.InvokeAsync(() => cut.FindComponent<EventSessionForm>().Instance.EndTimeChanged.InvokeAsync(TimeSpan.FromHours(1)));
            await cut.Find("form").SubmitAsync();
            var sent = _handler.SessionRequests.Single().Deserialize<UpdateEventSessionDto>(JsonSerializerOptions.Web)!;
            await Assert.That(sent.Schedule!.EndTime!.Value).IsEqualTo(new DateTimeOffset(2030, 6, 2, 1, 0, 0, TimeSpan.Zero));
        }
        else
        {
            var cut = _ctx.RenderMudComponent<CreateSession>(p => p.Add(x => x.EventId, CreatedId));
            await cut.InvokeAsync(() => cut.FindComponent<EventSessionForm>().Instance.TitleChanged.InvokeAsync("Overnight"));
            await cut.InvokeAsync(() => cut.FindComponent<EventSessionForm>().Instance.SessionDateChanged.InvokeAsync(new DateTime(2030, 6, 1)));
            await cut.InvokeAsync(() => cut.FindComponent<EventSessionForm>().Instance.StartTimeChanged.InvokeAsync(TimeSpan.FromHours(23)));
            await cut.InvokeAsync(() => cut.FindComponent<EventSessionForm>().Instance.EndDateChanged.InvokeAsync(new DateTime(2030, 6, 2)));
            await cut.InvokeAsync(() => cut.FindComponent<EventSessionForm>().Instance.EndTimeChanged.InvokeAsync(TimeSpan.FromHours(1)));
            await cut.Find("form").SubmitAsync();
            var sent = _handler.SessionRequests.Single().Deserialize<CreateEventSessionDto>(JsonSerializerOptions.Web)!;
            await Assert.That(sent.StartTime).IsEqualTo(new DateTimeOffset(2030, 6, 1, 23, 0, 0, TimeSpan.Zero));
            await Assert.That(sent.EndTime).IsEqualTo(new DateTimeOffset(2030, 6, 2, 1, 0, 0, TimeSpan.Zero));
        }
        await Assert.That(_ctx.Services.GetRequiredService<NavigationManager>().Uri)
            .IsEqualTo($"http://localhost/studio/events/{CreatedId}/schedule?programUpdated=1");
    }

    [Test]
    public async Task InitialSourceDraft_IsLocalAndRetainsMetadataAcrossRenderedCancelAndSave()
    {
        var parent = Parent();
        parent.Sessions = [new CreateEventGraphSessionDto
        {
            Title = "From proposal", TempKey = "proposal-item", EndTimeType = SessionEndTimeType.Fixed,
            LanguageIds = [2], SpeakerActorIds = [], SessionTemplateId = CreatedId,
            IslamicAspect = new EventSessionIslamicAspectDto { RequiresWudu = true }
        }];
        var cut = _ctx.RenderMudComponent<CreateEvent>(p => p.Add(x => x.InitialDraft, parent));
        var localId = Timeline(cut).Instance.Sessions[0].LocalId;
        await cut.InvokeAsync(() => Timeline(cut).Instance.OnEditSession.InvokeAsync(localId));
        await cut.InvokeAsync(() => cut.FindComponents<MudSelect<int>>().Single(x => x.Instance.Label == "Session languages")
            .Instance.SelectedValuesChanged.InvokeAsync([3]));
        await SetTitle(cut, "Discarded");
        await cut.Find("[data-testid='program-cancel']").ClickAsync(new MouseEventArgs());
        await Assert.That(Timeline(cut).Instance.Sessions.Single().LanguageIds.Single()).IsEqualTo(2);
        await cut.InvokeAsync(() => Timeline(cut).Instance.OnEditSession.InvokeAsync(localId));
        await SetTitle(cut, "Prepared proposal");
        await cut.Find("[data-testid='program-save']").ClickAsync(new MouseEventArgs());
        var submission = cut.Find("button.create-event__draft-button").ClickAsync(new MouseEventArgs());
        var sent = await _handler.Started.Task.WaitAsync(Timeout);
        await Assert.That(sent.Sessions!.Single().SessionTemplateId).IsEqualTo(CreatedId);
        await Assert.That(sent.Sessions.Single().IslamicAspect!.RequiresWudu).IsTrue();
        await Assert.That(sent.Sessions.Single().Title).IsEqualTo("Prepared proposal");
        await Assert.That(parent.Sessions.Single().Title).IsEqualTo("From proposal");
        _handler.Response.SetResult(new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(new BaseCommandResponseOfGuid { Success = true, Id = CreatedId })
        });
        await submission.WaitAsync(Timeout);
    }

    private IRenderedComponent<CreateEvent> Render() => _ctx.RenderMudComponent<CreateEvent>();

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ImportedSession_ChangedVenueDropsCompetingLocationAndRoomReferences(bool graphVenue, bool eventDefault)
    {
        var originalLocationId = Guid.CreateVersion7();
        var selectedLocationId = eventDefault ? (Guid?)null : Guid.CreateVersion7();
        var originalRoomId = Guid.CreateVersion7();
        var parent = Parent();
        parent.Sessions = [new CreateEventGraphSessionDto
        {
            TempKey = "imported-session", Title = "Imported session",
            LocationId = graphVenue ? null : originalLocationId,
            LocationTempKey = graphVenue ? "imported-venue" : null,
            RoomId = graphVenue ? null : originalRoomId,
            RoomTempKey = graphVenue ? "imported-room" : null
        }];
        if (graphVenue)
        {
            parent.Locations = [new CreateEventLocationDto
            {
                TempKey = "imported-venue", FullName = "Imported venue", Address = "Test address",
                Postcode = "1000", Country = "BE", City = "Test city", Timezone = "UTC"
            }];
            parent.Rooms = [new CreateEventRoomDto
            {
                TempKey = "imported-room", LocationTempKey = "imported-venue", Name = "Imported room"
            }];
        }
        var cut = _ctx.RenderMudComponent<CreateEvent>(parameters => parameters.Add(component => component.InitialDraft, parent));
        var localId = Timeline(cut).Instance.Sessions.Single().LocalId;
        await cut.InvokeAsync(() => Timeline(cut).Instance.OnEditSession.InvokeAsync(localId));
        await cut.InvokeAsync(() => cut.FindComponent<EventSessionForm>().Instance.LocationIdChanged.InvokeAsync(selectedLocationId));
        await cut.Find("[data-testid='program-save']").ClickAsync(new MouseEventArgs());
        var submission = cut.Find("button.create-event__draft-button").ClickAsync(new MouseEventArgs());
        var sent = await _handler.Started.Task.WaitAsync(Timeout);
        _handler.Response.SetResult(new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(new BaseCommandResponseOfGuid { Success = true, Id = CreatedId })
        });
        await submission.WaitAsync(Timeout);

        var saved = sent.Sessions!.Single();
        await Assert.That(saved.LocationId).IsEqualTo(selectedLocationId);
        await Assert.That(saved.LocationTempKey).IsNull();
        await Assert.That(saved.RoomId).IsNull();
        await Assert.That(saved.RoomTempKey).IsNull();
        await Assert.That(parent.Sessions.Single().RoomId).IsEqualTo(graphVenue ? null : originalRoomId);
        await Assert.That(parent.Sessions.Single().LocationTempKey).IsEqualTo(graphVenue ? "imported-venue" : null);
    }

    private static IRenderedComponent<ScheduleTimelineComposer> Timeline(IRenderedComponent<CreateEvent> cut) =>
        cut.FindComponent<ScheduleTimelineComposer>();
    private static IRenderedComponent<MudTextField<string>> EventTitle(IRenderedComponent<CreateEvent> cut) =>
        cut.FindComponents<MudTextField<string>>().Single(x => x.Instance.Label == "Event name");
    private static Task SetTitle(IRenderedComponent<CreateEvent> cut, string title) =>
        cut.InvokeAsync(() => cut.FindComponent<EventSessionForm>().Instance.TitleChanged.InvokeAsync(title));

    private static async Task AddTitleOnly(IRenderedComponent<CreateEvent> cut, string title, bool another = false)
    {
        await cut.InvokeAsync(() => Timeline(cut).Instance.OnAddSession.InvokeAsync());
        await SetTitle(cut, title);
        await cut.Find(another ? "[data-testid='program-save-another']" : "[data-testid='program-save']")
            .ClickAsync(new MouseEventArgs());
    }

    private static async Task AddScheduled(IRenderedComponent<CreateEvent> cut, string title)
    {
        await cut.InvokeAsync(() => Timeline(cut).Instance.OnAddSession.InvokeAsync());
        await SetTitle(cut, title);
        await cut.InvokeAsync(() => cut.FindComponent<EventSessionForm>().Instance.SessionDateChanged.InvokeAsync(new DateTime(2030, 6, 1)));
        await cut.InvokeAsync(() => cut.FindComponent<EventSessionForm>().Instance.EndDateChanged.InvokeAsync(new DateTime(2030, 6, 2)));
        await cut.InvokeAsync(() => cut.FindComponent<EventSessionForm>().Instance.StartTimeChanged.InvokeAsync(TimeSpan.FromHours(23)));
        await cut.InvokeAsync(() => cut.FindComponent<EventSessionForm>().Instance.EndTimeChanged.InvokeAsync(TimeSpan.FromHours(1)));
        await cut.Find("[data-testid='program-save']").ClickAsync(new MouseEventArgs());
    }

    private static CreateEventDraftRequestDto Parent() => new()
    {
        Title = "Prepared event",
        EventStatusId = 1,
        Timezone = "UTC",
        ParticipationConfiguration = new ConfigureEventParticipationDto { ParticipationHandlingModeId = 1, AdvanceRegistrationObligationId = 1 }
    };

    private sealed class CreationHandler : HttpMessageHandler
    {
        public static readonly Guid SessionId = Guid.Parse("0198c9e0-0000-7000-8000-000000000018");
        public bool PersistedSessionMode { get; set; }
        public List<JsonElement> SessionRequests { get; } = [];
        public TaskCompletionSource<CreateEventDraftRequestDto> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HttpResponseMessage> Response { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<CreateEventDraftRequestDto> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (PersistedSessionMode)
            {
                if (request.Method != HttpMethod.Get)
                {
                    SessionRequests.Add(JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(cancellationToken)));
                    return JsonResponse(new BaseCommandResponseOfGuid { Success = true, Id = SessionId },
                        request.Method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.OK);
                }
                var path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith("/session-create-context", StringComparison.Ordinal))
                {
                    return JsonResponse(new EventSessionCreateContextDto
                    {
                        EventId = CreatedId,
                        TenantId = CreatedId,
                        TimeZoneId = "UTC",
                        Defaults = new EventSessionCreateDefaultsDto { RegistrationModeId = 1 },
                        Locations = [],
                        Rooms = [],
                        SessionGroups = []
                    });
                }
                if (path.Contains("eventsessiongroup", StringComparison.Ordinal))
                {
                    return JsonResponse(new HalCollectionResourceOfEventSessionGroupListDto());
                }
                if (path.Contains("eventsession", StringComparison.Ordinal))
                {
                    return JsonResponse(new
                    {
                        id = SessionId,
                        eventId = CreatedId,
                        title = "Existing overnight",
                        concurrencyStamp = CreatedId,
                        startTime = new DateTimeOffset(2030, 6, 1, 23, 0, 0, TimeSpan.Zero),
                        endTime = new DateTimeOffset(2030, 6, 2, 0, 0, 0, TimeSpan.Zero),
                        _links = new Dictionary<string, object> { ["edit"] = new { href = $"/api/eventsession/{SessionId}", method = "PATCH" } }
                    });
                }
                return JsonResponse(new
                {
                    id = CreatedId,
                    tenantId = CreatedId,
                    title = "Saved event",
                    _links = new Dictionary<string, object> { ["add-session"] = new { href = "/api/eventsession", method = "POST" } }
                });
            }
            if (request.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new EventCreationContextDto
                    {
                        CanCreate = true,
                        DefaultPublisherMode = "personal",
                        PublisherOptions = [new EventCreationPublisherOptionDto { PublisherMode = "personal", CanPublish = true, DisplayName = "Publisher" }]
                    })
                };
            }

            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var submitted = JsonSerializer.Deserialize<CreateEventDraftRequestDto>(body, JsonSerializerOptions.Web)!;
            Requests.Add(submitted);
            Started.TrySetResult(submitted);
            return await Response.Task.WaitAsync(Timeout, cancellationToken);
        }

        private static HttpResponseMessage JsonResponse<T>(T value, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = JsonContent.Create(value) };
    }

    private sealed class FailingNavigation : NavigationManager
    {
        public FailingNavigation() => Initialize("http://localhost/", "http://localhost/");
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new InvalidOperationException("Navigation unavailable");
        protected override void SetNavigationLockState(bool value) { }
    }
}
