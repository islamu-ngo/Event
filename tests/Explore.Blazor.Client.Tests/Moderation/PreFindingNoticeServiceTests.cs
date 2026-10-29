using Explore.Blazor.Client.Services.Moderation;
using System.Net;
using System.Text.Json.Nodes;

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

    [Test]
    [Arguments("notice")]
    [Arguments("allegation")]
    [Arguments("applicableRule")]
    [Arguments("responseInstructions")]
    [Arguments("blankText")]
    [Arguments("oversizedText")]
    [Arguments("caseId")]
    [Arguments("caseRevision")]
    [Arguments("revision")]
    [Arguments("response")]
    public async Task InvalidTypedFieldsCannotBecomeDisplayState(string invalid)
    {
        var payload = JsonNode.Parse(PreFindingNoticeTransport.Notice())!;
        switch (invalid)
        {
            case "notice": payload["notice"] = null; break;
            case "allegation":
            case "applicableRule":
            case "responseInstructions": payload["notice"]![invalid] = null; break;
            case "blankText": payload["notice"]!["allegation"]!["value"] = " "; break;
            case "oversizedText": payload["notice"]!["allegation"]!["value"] = new string('a', 4001); break;
            case "caseId": payload[invalid] = Guid.Empty; break;
            case "caseRevision":
            case "revision": payload[invalid] = 0; break;
            case "response": payload[invalid] = new string('\u0627', 32769); break;
            default: throw new ArgumentOutOfRangeException(nameof(invalid));
        }
        using var transport = new PreFindingNoticeTransport();
        transport.Respond = (_, _) => Task.FromResult(PreFindingNoticeTransport.Json(payload.ToJsonString()));
        using var http = new HttpClient(transport) { BaseAddress = new Uri("https://event.example/") };

        var result = await Service(http).GetAsync(PreFindingNoticeTransport.NoticeId, false, CancellationToken.None);

        await Assert.That(result.View).IsNull();
        await Assert.That(result.Failure).IsEqualTo(PreFindingNoticeFailure.Unavailable);
        await Assert.That(transport.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task TypedOwnerHistoryIsProjectedWithoutGrantingMissingActions()
    {
        using var transport = new PreFindingNoticeTransport();
        transport.Respond = (_, _) => Task.FromResult(PreFindingNoticeTransport.Json(
            PreFindingNoticeTransport.Notice(actions: false, acknowledged: true, responded: true)));
        using var http = new HttpClient(transport) { BaseAddress = new Uri("https://event.example/") };

        var result = await Service(http).GetAsync(PreFindingNoticeTransport.NoticeId, false, CancellationToken.None);

        await Assert.That(result.Failure).IsEqualTo(PreFindingNoticeFailure.None);
        await Assert.That(result.View!.AcknowledgedAt).IsEqualTo(PreFindingNoticeTransport.Now.AddMinutes(1));
        await Assert.That(result.View.RespondedAt).IsEqualTo(PreFindingNoticeTransport.Now.AddMinutes(2));
        await Assert.That(result.View.Response).IsEqualTo("My permitted response");
        await Assert.That(result.View.HasAcknowledgeLink).IsFalse();
        await Assert.That(result.View.HasSubmitResponseLink).IsFalse();
    }

    internal static PreFindingNoticeService Service(HttpClient http) =>
        new(new ModerationPreFindingNoticesClient(http), new FixedTimeProvider(PreFindingNoticeTransport.Now));
}
