using AngleSharp.Dom;
using Bunit;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Components.Admissions;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Contracts.Services.Admissions;
using Explore.Blazor.Client.Services.Admissions;
using Explore.Blazor.Client.Services.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Explore.Blazor.Client.Tests;

public sealed class TicketTransferComponentTests :
    IDisposable
{
    private readonly BlazorTestContext _context = new();

    public void Dispose() => _context.Dispose();

    [Test]
    public async Task RenderedActionsComeOnlyFromHalRelations()
    {
        ITicketTransferService service =
            Substitute.For<ITicketTransferService>();
        _context.Services.AddSingleton(service);
        HalResourceOfTicketTransferDto resource =
            Resource(
                "offered",
                "recipient_action_required",
                "accept-ticket-transfer");

        var cut = _context.Render<TicketTransferPanel>(
            parameters => parameters
                .Add(
                    value => value.EventId,
                    Guid.CreateVersion7())
                .Add(
                    value =>
                        value.AdmissionTicketId,
                    resource.AdmissionTicketId)
                .Add(
                    value => value.TransferId,
                    resource.Id)
                .Add(
                    value =>
                        value.RecipientParticipantId,
                    Guid.CreateVersion7())
                .Add(
                    value => value.InitialResource,
                    resource));

        await Assert.That(cut.FindAll(
                "button[data-relation=" +
                "'accept-ticket-transfer']"))
            .HasSingleItem();
        await Assert.That(cut.FindAll(
                "button[data-relation=" +
                "'cancel-ticket-transfer']"))
            .IsEmpty();
        await Assert.That(cut.FindAll(
                "[data-support-code=" +
                "'recipient_action_required']"))
            .HasSingleItem();
        await Assert.That(cut.Markup)
            .DoesNotContain(">offered<");
    }

    [Test]
    public async Task ActionDisablesUntilCompletionThenFocusesRenderedError()
    {
        var completion = new TaskCompletionSource<
            TicketTransferCredentialResponse?>(
            TaskCreationOptions
                .RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(
            TaskCreationOptions
                .RunContinuationsAsynchronously);
        ITicketTransferService service =
            Substitute.For<ITicketTransferService>();
        service.AcceptAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                entered.TrySetResult();
                return completion.Task;
            });
        _context.Services.AddSingleton(service);
        IAccessibilityFocusService focus =
            _context.Services.GetRequiredService<
                IAccessibilityFocusService>();
        var focused = new TaskCompletionSource(
            TaskCreationOptions
                .RunContinuationsAsynchronously);
        focus.FocusAsync(
                Arg.Any<string>(),
                Arg.Any<bool>())
            .Returns(_ =>
            {
                focused.TrySetResult();
                return Task.CompletedTask;
            });
        HalResourceOfTicketTransferDto resource =
            Resource(
                "offered",
                "recipient_action_required",
                "accept-ticket-transfer");
        var cut = _context.Render<TicketTransferPanel>(
            parameters => parameters
                .Add(
                    value => value.EventId,
                    Guid.CreateVersion7())
                .Add(
                    value =>
                        value.AdmissionTicketId,
                    resource.AdmissionTicketId)
                .Add(
                    value => value.TransferId,
                    resource.Id)
                .Add(
                    value =>
                        value.RecipientParticipantId,
                    Guid.CreateVersion7())
                .Add(
                    value => value.Capability,
                    Guid.CreateVersion7()
                        .ToString("N"))
                .Add(
                    value => value.InitialResource,
                    resource));
        IElement button = cut.Find(
            "button[data-relation=" +
            "'accept-ticket-transfer']");

        Task click =
            cut.InvokeAsync(() => button.Click());
        await entered.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        await Assert.That(cut.Find(
                "button[data-relation=" +
                "'accept-ticket-transfer']")
                .HasAttribute("disabled"))
            .IsTrue();
        completion.SetResult(null);
        await focused.Task.WaitAsync(
            TimeSpan.FromSeconds(5));
        await click;

        IElement outcome = cut.Find(
            "[id^='ticket-transfer-outcome-']");
        await Assert.That(
                outcome.GetAttribute("role"))
            .IsEqualTo("alert");
        await Assert.That(
                outcome.GetAttribute("aria-live"))
            .IsEqualTo("assertive");
        await Assert.That(
                outcome.GetAttribute("tabindex"))
            .IsEqualTo("-1");
        await focus.Received(1).FocusAsync(
            Arg.Is<string>(value =>
                value.StartsWith(
                    "#ticket-transfer-outcome-",
                    StringComparison.Ordinal)));
    }

    [Test]
    public async Task OneTimeClaimRendersOnlyAfterSuccessfulOffer()
    {
        string claim =
            Guid.CreateVersion7().ToString("N");
        ITicketTransferService service =
            Substitute.For<ITicketTransferService>();
        HalResourceOfTicketTransferDto resource =
            Resource(
                "offered",
                "recipient_action_required",
                "cancel-ticket-transfer");
        service.OfferAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(new TicketTransferOfferResponse
            {
                Transfer = resource,
                ClaimCapability = claim,
            });
        _context.Services.AddSingleton(service);
        var links = new Dictionary<string, HalLink>
        {
            ["offer-ticket-transfer"] =
                new()
                {
                    Href = "/transfer-offer",
                    Method = HttpMethod.Post.Method,
                },
        };
        var cut = _context.Render<TicketTransferPanel>(
            parameters => parameters
                .Add(
                    value => value.EventId,
                    Guid.CreateVersion7())
                .Add(
                    value =>
                        value.AdmissionTicketId,
                    resource.AdmissionTicketId)
                .Add(
                    value => value.TicketLinks,
                    links));

        await cut.InvokeAsync(() => cut.Find(
                "button[data-relation=" +
                "'offer-ticket-transfer']")
            .Click());

        await Assert.That(cut.FindAll(
                ".ticket-transfer__secret output"))
            .HasSingleItem();
        await Assert.That(cut.Find(
                ".ticket-transfer__secret output")
                .TextContent)
            .IsEqualTo(claim);
        await Assert.That(cut.Markup)
            .Contains("shown only now");
    }

    [Test]
    public async Task ServiceFailsClosedButPropagatesCancellation()
    {
        IBffClient bff =
            Substitute.For<IBffClient>();
        bff.GetWithTicketTransferCapabilityAsync<
                HalResourceOfTicketTransferDto>(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<
                HalResourceOfTicketTransferDto?>(
                    new HttpRequestException()));
        var service =
            new TicketTransferService(bff);

        HalResourceOfTicketTransferDto? failed =
            await service.GetAsync(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7()
                    .ToString("N"),
                CancellationToken.None);

        await Assert.That(failed).IsNull();

        using var cancellation =
            new CancellationTokenSource();
        cancellation.Cancel();
        bff.GetWithTicketTransferCapabilityAsync<
                HalResourceOfTicketTransferDto>(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                cancellation.Token)
            .Returns(Task.FromCanceled<
                HalResourceOfTicketTransferDto?>(
                    cancellation.Token));

        await Assert.ThrowsAsync<
            OperationCanceledException>(async () =>
            await service.GetAsync(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                null,
                cancellation.Token));
    }

    private static HalResourceOfTicketTransferDto
        Resource(
            string statusCode,
            string supportCode,
            params string[] relations) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            AdmissionTicketId =
                Guid.CreateVersion7(),
            StatusCode = statusCode,
            SupportCode = supportCode,
            TransferHop = 1,
            ExpiresAt = TestTime.UtcNow
                .AddHours(1),
            CredentialGeneration = 1,
            _links = relations.ToDictionary(
                relation => relation,
                relation => new HalLink
                {
                    Href = $"/transfer/{relation}",
                    Method =
                        HttpMethod.Post.Method,
                }),
        };

}
