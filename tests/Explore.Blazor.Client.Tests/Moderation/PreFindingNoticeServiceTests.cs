using Explore.Blazor.Client.Services.Moderation;
using System.Net;

namespace Explore.Blazor.Client.Tests.Moderation;

public sealed class PreFindingNoticeServiceTests
{
    [Test]
    public async Task ReadProjectsOnlySafeTextAndNeverAcknowledges()
    {
        using var transport = new PreFindingNoticeTransport();
        using var http = new HttpClient(transport) { BaseAddress = new Uri("https://event.example/") };
        var service = Service(http);
        var result = await service.GetAsync(PreFindingNoticeTransport.NoticeId, false, CancellationToken.None);
        await Assert.That(result.View!.Allegation).IsEqualTo("Approved allegation");
        await Assert.That(result.View.HasAcknowledgeLink).IsTrue();
        await Assert.That(transport.Requests.Count).IsEqualTo(1);
        await Assert.That(transport.Requests[0].Method).IsEqualTo("GET");
        await Assert.That(result.View.ToString()).IsEqualTo(nameof(PreFindingNoticeView));
    }

    [Test]
    public async Task AcknowledgeUsesExactRevisionsAndOneUuidV7Invocation()
    {
        using var transport = new PreFindingNoticeTransport();
        using var http = new HttpClient(transport) { BaseAddress = new Uri("https://event.example/") };
        var service = Service(http);
        var view = (await service.GetAsync(PreFindingNoticeTransport.NoticeId, false, CancellationToken.None)).View!;
        transport.Respond = (_, _) => Task.FromResult(PreFindingNoticeTransport.Json(
            $$"""{"success":true,"id":"{{PreFindingNoticeTransport.NoticeId}}"}"""));
        var result = await service.AcknowledgeAsync(view, CancellationToken.None);
        var body = PreFindingNoticeTransport.Body(transport.Requests[1].Body!);
        await Assert.That(body.GetProperty("expectedCaseRevision").GetInt64()).IsEqualTo(11L);
        await Assert.That(body.GetProperty("expectedNoticeRevision").GetInt64()).IsEqualTo(7L);
        await Assert.That(body.GetProperty("caseId").GetGuid()).IsEqualTo(PreFindingNoticeTransport.CaseId);
        await Assert.That(body.GetProperty("noticeId").GetGuid()).IsEqualTo(PreFindingNoticeTransport.NoticeId);
        await Assert.That(body.GetProperty("operationId").GetGuid().Version).IsEqualTo(7);
        await Assert.That(result).IsEqualTo(PreFindingNoticeFailure.None);
        await Assert.That(transport.Requests.Count).IsEqualTo(2);
    }

    [Test]
    public async Task ReviewerAndMissingLinksCannotProduceRecipientWrites()
    {
        using var transport = new PreFindingNoticeTransport();
        using var http = new HttpClient(transport) { BaseAddress = new Uri("https://event.example/") };
        var service = Service(http);
        var reviewer = (await service.GetAsync(PreFindingNoticeTransport.NoticeId, true, CancellationToken.None)).View!;
        await Assert.That(reviewer.HasAcknowledgeLink).IsFalse();
        await Assert.That(await service.RespondAsync(reviewer, "My account", CancellationToken.None))
            .IsEqualTo(PreFindingNoticeFailure.Unavailable);
        transport.Respond = (_, _) => Task.FromResult(PreFindingNoticeTransport.Json(
            PreFindingNoticeTransport.Notice(actions: false)));
        var recipient = (await service.GetAsync(PreFindingNoticeTransport.NoticeId, false, CancellationToken.None)).View!;
        await Assert.That(await service.AcknowledgeAsync(recipient, CancellationToken.None))
            .IsEqualTo(PreFindingNoticeFailure.Unavailable);
        await Assert.That(transport.Requests.All(request => request.Method == "GET")).IsTrue();
    }

    [Test]
    public async Task ResponseBoundsRejectOversizedUtf8BeforeAnyWrite()
    {
        using var transport = new PreFindingNoticeTransport();
        using var http = new HttpClient(transport) { BaseAddress = new Uri("https://event.example/") };
        var service = Service(http);
        var view = (await service.GetAsync(PreFindingNoticeTransport.NoticeId, false, CancellationToken.None)).View!;
        await Assert.That(await service.RespondAsync(view, new string('\u0627', 32769), CancellationToken.None))
            .IsEqualTo(PreFindingNoticeFailure.InvalidResponse);
        await Assert.That(transport.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ConflictHasNoRetryAndNoInventedSuccess()
    {
        using var transport = new PreFindingNoticeTransport();
        using var http = new HttpClient(transport) { BaseAddress = new Uri("https://event.example/") };
        var service = Service(http);
        var view = (await service.GetAsync(PreFindingNoticeTransport.NoticeId, false, CancellationToken.None)).View!;
        transport.Respond = (_, _) => Task.FromResult(PreFindingNoticeTransport.Json(
            """{"title":"Private internal diagnostic"}""", HttpStatusCode.Conflict));
        await Assert.That(await service.RespondAsync(view, "My account", CancellationToken.None))
            .IsEqualTo(PreFindingNoticeFailure.Conflict);
        await Assert.That(transport.Requests.Count).IsEqualTo(2);
    }

    [Test]
    public async Task WrongNoticeAndExpiredPayloadNeverBecomeDisplayState()
    {
        using var transport = new PreFindingNoticeTransport();
        using var http = new HttpClient(transport) { BaseAddress = new Uri("https://event.example/") };
        var service = Service(http);
        transport.Respond = (_, _) => Task.FromResult(PreFindingNoticeTransport.Json(
            PreFindingNoticeTransport.Notice(Guid.CreateVersion7())));
        await Assert.That((await service.GetAsync(PreFindingNoticeTransport.NoticeId, false, CancellationToken.None)).View).IsNull();
        transport.Respond = (_, _) => Task.FromResult(PreFindingNoticeTransport.Json(
            PreFindingNoticeTransport.Notice(expires: PreFindingNoticeTransport.Now)));
        await Assert.That((await service.GetAsync(PreFindingNoticeTransport.NoticeId, false, CancellationToken.None)).View).IsNull();
    }

    internal static PreFindingNoticeService Service(HttpClient http) =>
        new(new ModerationPreFindingNoticesClient(http), new FixedTimeProvider(PreFindingNoticeTransport.Now));
}
