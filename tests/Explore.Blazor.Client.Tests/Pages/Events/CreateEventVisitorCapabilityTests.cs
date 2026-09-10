
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Explore.Blazor.Client.Pages.Events;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Events;

public sealed class CreateEventVisitorCapabilityTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly BlazorTestContext _ctx = new();
    private readonly CreationHandler _creation = new();
    private readonly IPublicExperienceClient _publicClient;
    private readonly HttpClient _http;

    public CreateEventVisitorCapabilityTests()
    {
        _http = new HttpClient(_creation) { BaseAddress = new Uri("https://event.test/") };
        var management = Substitute.For<IEventManagementReadClient>();
        management.GetEventCreationContextAsync(cancellationToken: Arg.Any<CancellationToken>())
            .Returns(new EventCreationContextDto
            {
                CanCreate = true,
                DefaultPublisherMode = "personal",
                PublisherOptions =
                [
                    new EventCreationPublisherOptionDto
                    {
                        PublisherMode = "personal",
                        CanPublish = true,
                        DisplayName = "Publisher"
                    }
                ]
            });
        _ctx.Services.AddSingleton<IEventService>(new EventService(
            Substitute.For<IEventClient>(),
            new EventLifecycleClient(_http),
            management,
            Substitute.For<IEventParticipationClient>(),
            Substitute.For<IEventPublicActionClient>(),
            NullLogger<EventService>.Instance));
        _publicClient = Substitute.For<IPublicExperienceClient>();
        _publicClient.GetPublicExperienceSettingsAsync(cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Settings(true));
        _ctx.Services.AddSingleton<IPublicExperienceService>(new PublicExperienceService(
            _publicClient, NullLogger<PublicExperienceService>.Instance));
        _ctx.AddMockService<IEventSessionService>();
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
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task Rejection_RefreshesCapabilityAndPreservesSelectionWithoutAllowingStaleSave(
        bool validationException, bool refreshFails)
    {
        var cut = await RenderAccountRequiredAsync();
        await Assert.That(AccountOption(cut).Instance.Disabled).IsFalse();
        await Assert.That(cut.Find("button.create-event__draft-button").HasAttribute("disabled")).IsFalse();

        _publicClient.GetPublicExperienceSettingsAsync(cancellationToken: Arg.Any<CancellationToken>())
            .Returns(_ => refreshFails
                ? Task.FromException<PublicExperienceSettingsDto>(new HttpRequestException("Refresh unavailable"))
                : Task.FromResult(Settings(false)));

        var submission = cut.Find("button.create-event__draft-button").ClickAsync(new MouseEventArgs());
        var submitted = await _creation.Started.Task.WaitAsync(Timeout);
        await Assert.That(submitted.ParticipationConfiguration.IdentityAccessModeId).IsEqualTo(1);
        using var rejection = Rejection(validationException);
        _creation.Response.SetResult(rejection);
        await submission.WaitAsync(Timeout);

        await Assert.That(AccountOption(cut).Instance.Disabled).IsTrue();
        await Assert.That(Select(cut, "Identity access").Instance.Value).IsEqualTo(1);
        await Assert.That(Select(cut, "Participation handling").Instance.Value).IsEqualTo(4);
        await Assert.That(Select(cut, "Advance registration").Instance.Value).IsEqualTo(3);
        await Assert.That(Field(cut, "Event name").Instance.Value).IsEqualTo("Retained event");
        await Assert.That(Field(cut, "Subtitle").Instance.Value).IsEqualTo("Retained subtitle");
        await Assert.That(Field(cut, "Card description").Instance.Value).IsEqualTo("Retained description");
        await Assert.That(cut.FindAll("[data-testid='create-event-account-required-unavailable'][role='note']").Count)
            .IsEqualTo(1);
        await Assert.That(cut.Find("button.create-event__draft-button").HasAttribute("disabled")).IsTrue();
        await Assert.That(cut.Find("button.create-event__submit-button").HasAttribute("disabled")).IsTrue();
        await Assert.That(cut.FindComponents<MudSelect<int?>>().Any(x => x.Instance.Label == "Guest recovery"))
            .IsFalse();

        // Native form submission must also fail closed, even when the disabled button is bypassed.
        await cut.Find("form").SubmitAsync();
        await Assert.That(_creation.Requests.Count).IsEqualTo(1);
        await Assert.That(_ctx.Services.GetRequiredService<NavigationManager>().Uri).IsEqualTo("http://localhost/");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task UnrelatedRejection_WithCurrentCapability_AllowsRetryWithoutChangingTheRequest(bool validationException)
    {
        var cut = await RenderAccountRequiredAsync();
        var submission = cut.Find("button.create-event__draft-button").ClickAsync(new MouseEventArgs());
        await _creation.Started.Task.WaitAsync(Timeout);
        using var rejection = Rejection(validationException, "event_validation_failed");
        _creation.Response.SetResult(rejection);
        await submission.WaitAsync(Timeout);

        await Assert.That(AccountOption(cut).Instance.Disabled).IsFalse();
        await Assert.That(cut.Find("button.create-event__draft-button").HasAttribute("disabled")).IsFalse();
        await cut.Find("button.create-event__draft-button").ClickAsync(new MouseEventArgs());

        await Assert.That(_creation.Requests.Count).IsEqualTo(2);
        await Assert.That(_creation.Requests[1]).IsEqualTo(_creation.Requests[0]);
        await Assert.That(_ctx.Services.GetRequiredService<NavigationManager>().Uri)
            .IsEqualTo($"http://localhost/events/{CreationHandler.CreatedId}/edit");
    }

    private async Task<IRenderedComponent<CreateEvent>> RenderAccountRequiredAsync()
    {
        var cut = _ctx.RenderMudComponent<CreateEvent>();
        await cut.InvokeAsync(() => Field(cut, "Event name").Instance.ValueChanged.InvokeAsync("Retained event"));
        await cut.InvokeAsync(() => Field(cut, "Subtitle").Instance.ValueChanged.InvokeAsync("Retained subtitle"));
        await cut.InvokeAsync(() => Field(cut, "Card description").Instance.ValueChanged.InvokeAsync("Retained description"));
        await cut.InvokeAsync(() => Select(cut, "Participation handling").Instance.ValueChanged.InvokeAsync(4));
        await cut.InvokeAsync(() => Select(cut, "Advance registration").Instance.ValueChanged.InvokeAsync(3));
        await cut.InvokeAsync(() => Select(cut, "Identity access").Instance.ValueChanged.InvokeAsync(1));
        return cut;
    }

    private static IRenderedComponent<MudTextField<string>> Field(IRenderedComponent<CreateEvent> cut, string label) =>
        cut.FindComponents<MudTextField<string>>().Single(x => x.Instance.Label == label);

    private static IRenderedComponent<MudSelect<int?>> Select(IRenderedComponent<CreateEvent> cut, string label) =>
        cut.FindComponents<MudSelect<int?>>().Single(x => x.Instance.Label == label);

    private static IRenderedComponent<MudSelectItem<int?>> AccountOption(IRenderedComponent<CreateEvent> cut) =>
        Select(cut, "Identity access").FindComponents<MudSelectItem<int?>>()
            .Single(x => x.Instance.Value == 1);

    private static PublicExperienceSettingsDto Settings(bool allowed) => new()
    {
        VisitorAccess = new VisitorAccessCapabilityDto
        {
            AllowsAccountRequiredParticipation = allowed,
            AllowsAnonymousParticipation = true,
            AllowsExistingAccountLogin = true,
            AllowsNewNativeAllocation = true,
            SignupDestinations = []
        }
    };

    private static HttpResponseMessage Rejection(
        bool validationException, string code = "event_visitor_account_onboarding_required")
    {
        var problem = new ValidationProblemDetails
        {
            Status = validationException ? 400 : 403,
            Errors = new Dictionary<string, ICollection<string>> { ["ParticipationConfiguration"] = [code] },
            AdditionalProperties = new Dictionary<string, object> { ["code"] = code }
        };
        return new HttpResponseMessage((HttpStatusCode)problem.Status.Value)
        {
            Content = JsonContent.Create(problem)
        };
    }

    private sealed class CreationHandler : HttpMessageHandler
    {
        public static readonly Guid CreatedId = Guid.Parse("0198c9e0-0000-7000-8000-000000000007");
        public TaskCompletionSource<CreateEventDraftRequestDto> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HttpResponseMessage> Response { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Requests.Add(body);
            Started.TrySetResult(JsonSerializer.Deserialize<CreateEventDraftRequestDto>(body, JsonSerializerOptions.Web)!);
            if (Requests.Count == 1)
            {
                return await Response.Task.WaitAsync(Timeout, cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new BaseCommandResponseOfGuid { Id = CreatedId, Success = true })
            };
        }
    }
}
