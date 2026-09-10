using AngleSharp.Dom;
using Explore.Blazor.Client.Components.Waitlist;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Contracts.Services.Waitlist;

namespace Explore.Blazor.Client.Tests;

public sealed class FairReturnWaitlistComponentTests
{
    [Test]
    public async Task HalRelationsAloneControlRenderedBoundedStateAndActions()
    {
        using var context = new BlazorTestContext();
        IFairReturnWaitlistService service = Substitute.For<IFairReturnWaitlistService>();
        service.GetAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(Resource(
                "active",
                position: 3,
                "priority_window",
                "join-fair-return-waitlist"));
        context.Services.AddSingleton(service);

        IRenderedComponent<FairReturnWaitlistPanel> cut = Render(context);

        await Assert.That(cut.FindAll("[data-testid='fair-return-waitlist']"))
            .HasSingleItem();
        await Assert.That(cut.Find(".fair-return-waitlist__facts").TextContent)
            .Contains("3");
        await Assert.That(cut.FindAll(
            "button[data-relation='join-fair-return-waitlist']"))
            .HasSingleItem();
        await Assert.That(cut.FindAll(
            "button[data-relation='leave-fair-return-waitlist']"))
            .IsEmpty();
        await Assert.That(cut.FindAll(
            "button[data-relation='accept-fair-return-offer']"))
            .IsEmpty();
        await Assert.That(cut.Markup).DoesNotContain(">priority_window<");
    }

    [Test]
    public async Task JoinDisablesUntilExactCompletionThenFocusesOutcome()
    {
        using var context = new BlazorTestContext();
        var completion = new TaskCompletionSource<HalResourceOfFairReturnWaitlistDto?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        IFairReturnWaitlistService service = Substitute.For<IFairReturnWaitlistService>();
        service.GetAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(Resource(
                "eligible",
                position: 0,
                "capacity_unavailable",
                "join-fair-return-waitlist"));
        service.JoinAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                entered.TrySetResult();
                return completion.Task;
            });
        context.Services.AddSingleton(service);
        IAccessibilityFocusService focus =
            context.Services.GetRequiredService<IAccessibilityFocusService>();
        var focused = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        focus.FocusAsync(Arg.Any<string>(), Arg.Any<bool>())
            .Returns(_ =>
            {
                focused.TrySetResult();
                return Task.CompletedTask;
            });
        IRenderedComponent<FairReturnWaitlistPanel> cut = Render(context);
        IElement join = cut.Find(
            "button[data-relation='join-fair-return-waitlist']");

        Task click = cut.InvokeAsync(() => join.Click());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(cut.Find(
                "button[data-relation='join-fair-return-waitlist']")
            .HasAttribute("disabled")).IsTrue();
        completion.SetResult(Resource(
            "waiting",
            position: 1,
            "capacity_unavailable",
            "leave-fair-return-waitlist"));
        await focused.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await click;

        cut.WaitForAssertion(() =>
        {
            IElement outcome = cut.Find("[id^='waitlist-outcome-']");
            Assert.That(outcome.TextContent).Contains("Updated");
        });
        IElement outcome = cut.Find("[id^='waitlist-outcome-']");
        await Assert.That(outcome.GetAttribute("role")).IsEqualTo("status");
        await Assert.That(outcome.GetAttribute("aria-live")).IsEqualTo("polite");
        await Assert.That(outcome.GetAttribute("tabindex")).IsEqualTo("-1");
        await Assert.That(cut.FindAll(
            "button[data-relation='leave-fair-return-waitlist']"))
            .HasSingleItem();
        await focus.Received(1).FocusAsync(
            Arg.Is<string>(selector => selector.StartsWith(
                "#waitlist-outcome-",
                StringComparison.Ordinal)));
    }

    [Test]
    public async Task AmbiguousRetryReusesOperationUntilCompletion()
    {
        var lease = new WaitlistMutationOperationLease();

        Guid first = lease.Acquire("event:order:line:join:");
        Guid replay = lease.Acquire("event:order:line:join:");
        await Assert.That(replay).IsEqualTo(first);
        await Assert.That(first.Version).IsEqualTo(7);

        Guid different = lease.Acquire("event:order:line:leave:");
        await Assert.That(different).IsNotEqualTo(first);
        lease.Complete("event:order:line:join:");
        await Assert.That(lease.Acquire("event:order:line:leave:"))
            .IsEqualTo(different);

        lease.Complete("event:order:line:leave:");
        await Assert.That(lease.Acquire("event:order:line:leave:"))
            .IsNotEqualTo(different);
    }

    private static IRenderedComponent<FairReturnWaitlistPanel> Render(
        BlazorTestContext context) =>
        context.Render<FairReturnWaitlistPanel>(parameters => parameters
            .Add(component => component.EventId, Guid.CreateVersion7())
            .Add(component => component.RegistrationOrderId, Guid.CreateVersion7())
            .Add(component => component.RegistrationOrderLineId, Guid.CreateVersion7())
            .Add(component => component.Capability, Guid.CreateVersion7().ToString("N")));

    private static HalResourceOfFairReturnWaitlistDto Resource(
        string statusCode,
        int position,
        string reasonCode,
        params string[] relations) => new()
        {
            Id = Guid.CreateVersion7(),
            StatusCode = statusCode,
            Position = position,
            ReasonCode = reasonCode,
            _links = relations.ToDictionary(
            relation => relation,
            relation => new HalLink
            {
                Href = $"/waitlist/{relation}",
                Method = HttpMethod.Post.Method
            })
        };
}
