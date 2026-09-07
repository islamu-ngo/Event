using AngleSharp.Dom;
using Explore.Blazor.Client.Components.Admissions;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Contracts.Services.Admissions;

namespace Explore.Blazor.Client.Tests;

public sealed class ParticipantReadinessComponentTests : IDisposable
{
    private readonly BlazorTestContext _context = new();

    public void Dispose() => _context.Dispose();

    [Test]
    public async Task UnavailableReadinessIsGenericAndActionless()
    {
        IRenderedComponent<ParticipantReadinessPanel> cut =
            Render(new ReadinessServiceFake());

        await Assert.That(cut.FindAll("[data-testid='participant-readiness']"))
            .HasSingleItem();
        await Assert.That(cut.FindAll(
            "[role='alert'][data-readiness-state='unavailable']"))
            .HasSingleItem();
        await Assert.That(cut.FindAll("button")).IsEmpty();
    }

    [Test]
    public async Task PendingStateUsesBoundedCopyWithoutLocalActions()
    {
        var service = new ReadinessServiceFake
        {
            Resource = Resource("participant_completion_pending", "action_required")
        };
        IRenderedComponent<ParticipantReadinessPanel> cut = Render(service);

        await Assert.That(cut.FindAll(
            "section[aria-labelledby^='participant-readiness-title-']"))
            .HasSingleItem();
        await Assert.That(cut.FindAll(
            "[role='status'][data-readiness-code='participant_completion_pending']"))
            .HasSingleItem();
        await Assert.That(cut.Markup)
            .DoesNotContain(">participant_completion_pending<");
        await Assert.That(cut.FindAll("button")).IsEmpty();
    }

    [Test]
    public async Task HalRelationsAloneControlRenderedActions()
    {
        var service = new ReadinessServiceFake
        {
            Resource = Resource(
                "participant_completion_pending",
                "action_required",
                "complete-participant-readiness")
        };
        IRenderedComponent<ParticipantReadinessPanel> cut = Render(service);

        await Assert.That(cut.FindAll(
            "button[data-relation='complete-participant-readiness']"))
            .HasSingleItem();
        await Assert.That(cut.FindAll(
            "button[data-relation='approve-participant-readiness']"))
            .IsEmpty();
        await Assert.That(cut.FindAll(
            "button[data-relation='revoke-participant-readiness']"))
            .IsEmpty();
    }

    [Test]
    public async Task ActionDisablesUntilExactCompletionThenFocusesError()
    {
        var completion = new TaskCompletionSource<HalResourceOfParticipantReadinessDto?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new ReadinessServiceFake
        {
            Resource = Resource(
                "participant_completion_pending",
                "action_required",
                "complete-participant-readiness"),
            ActionCompletion = completion,
            ActionEntered = entered
        };
        IRenderedComponent<ParticipantReadinessPanel> cut = Render(service);
        IAccessibilityFocusService focus =
            _context.Services.GetRequiredService<IAccessibilityFocusService>();
        var focused = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        focus.FocusAsync(Arg.Any<string>(), Arg.Any<bool>())
            .Returns(_ =>
            {
                focused.TrySetResult();
                return Task.CompletedTask;
            });
        IElement button = cut.Find(
            "button[data-relation='complete-participant-readiness']");

        Task click = cut.InvokeAsync(() => button.Click());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(cut.Find(
                "button[data-relation='complete-participant-readiness']")
            .HasAttribute("disabled")).IsTrue();
        completion.SetResult(null);
        await focused.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await click;

        IElement outcome = cut.Find("[id^='participant-readiness-outcome-']");
        await Assert.That(outcome.TextContent)
            .Contains("Admission readiness could not be updated.");
        await Assert.That(outcome.GetAttribute("role")).IsEqualTo("alert");
        await Assert.That(outcome.GetAttribute("aria-live")).IsEqualTo("assertive");
        await Assert.That(outcome.GetAttribute("tabindex")).IsEqualTo("-1");
        await focus.Received(1).FocusAsync(
            Arg.Is<string>(selector => selector.StartsWith(
                "#participant-readiness-outcome-",
                StringComparison.Ordinal)));
    }

    [Test]
    public async Task RevokedStateOffersDignifiedSupportWithoutDetails()
    {
        var service = new ReadinessServiceFake
        {
            Resource = Resource("revoked", "contact_organizer")
        };
        IRenderedComponent<ParticipantReadinessPanel> cut = Render(service);

        await Assert.That(cut.FindAll("[data-support-code='contact_organizer']"))
            .HasSingleItem();
        await Assert.That(cut.FindAll("button")).IsEmpty();
        await Assert.That(cut.Markup).DoesNotContain("consent");
        await Assert.That(cut.Markup).DoesNotContain("payment");
    }

    private IRenderedComponent<ParticipantReadinessPanel> Render(
        IParticipantReadinessService service)
    {
        _context.Services.AddSingleton(service);
        return _context.Render<ParticipantReadinessPanel>(parameters => parameters
            .Add(component => component.EventId, Guid.CreateVersion7())
            .Add(component => component.OrderId, Guid.CreateVersion7())
            .Add(component => component.ParticipantId, Guid.CreateVersion7())
            .Add(component => component.AssignmentId, Guid.CreateVersion7())
            .Add(component => component.GuestCapability, Guid.CreateVersion7().ToString("N")));
    }

    private static HalResourceOfParticipantReadinessDto Resource(
        string statusCode,
        string supportCode,
        params string[] relations) => new()
    {
        RegistrationTicketAssignmentId = Guid.CreateVersion7(),
        StatusCode = statusCode,
        SupportCode = supportCode,
        ActiveAdmissionAvailable = false,
        _links = relations.ToDictionary(
            relation => relation,
            relation => new HalLink
            {
                Href = $"/readiness/{relation}",
                Method = HttpMethod.Post.Method
            })
    };

    private sealed class ReadinessServiceFake : IParticipantReadinessService
    {
        public HalResourceOfParticipantReadinessDto? Resource { get; init; }
        public TaskCompletionSource<HalResourceOfParticipantReadinessDto?>?
            ActionCompletion { get; init; }
        public TaskCompletionSource? ActionEntered { get; init; }

        public Task<HalResourceOfParticipantReadinessDto?> GetAsync(
            Guid eventId,
            Guid orderId,
            Guid participantId,
            Guid assignmentId,
            string? guestCapability,
            CancellationToken cancellationToken) => Task.FromResult(Resource);

        public Task<HalResourceOfParticipantReadinessDto?> CompleteAsync(
            Guid eventId,
            Guid orderId,
            Guid participantId,
            Guid assignmentId,
            CancellationToken cancellationToken) => CompleteAction();

        public Task<HalResourceOfParticipantReadinessDto?> ApproveAsync(
            Guid eventId,
            Guid orderId,
            Guid participantId,
            Guid assignmentId,
            CancellationToken cancellationToken) => CompleteAction();

        public Task<HalResourceOfParticipantReadinessDto?> RevokeAsync(
            Guid eventId,
            Guid orderId,
            Guid participantId,
            Guid assignmentId,
            CancellationToken cancellationToken) => CompleteAction();

        private Task<HalResourceOfParticipantReadinessDto?> CompleteAction()
        {
            ActionEntered?.TrySetResult();
            return ActionCompletion?.Task ?? Task.FromResult(Resource);
        }
    }
}
