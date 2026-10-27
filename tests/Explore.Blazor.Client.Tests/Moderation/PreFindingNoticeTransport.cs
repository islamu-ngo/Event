using System.Net;
using System.Text;
using System.Text.Json;

namespace Explore.Blazor.Client.Tests.Moderation;

internal sealed class PreFindingNoticeTransport : HttpMessageHandler
{
    internal static readonly Guid NoticeId = Guid.Parse("0199aabb-1234-7000-8000-000000000001");
    internal static readonly Guid CaseId = Guid.Parse("0199aabb-1234-7000-8000-000000000002");
    internal static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-13T12:00:00Z");
    internal Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond { get; set; } =
        (_, _) => Task.FromResult(Json(Notice()));
    internal List<(string Method, string Path, string? Body)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Requests.Add((request.Method.Method, request.RequestUri!.PathAndQuery,
            request.Content is null ? null : await request.Content.ReadAsStringAsync(token)));
        return await Respond(request, token);
    }

    internal static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/hal+json") };

    internal static string Notice(Guid? id = null, bool actions = true, bool acknowledged = false,
        bool responded = false, DateTimeOffset? expires = null) => $$"""
        {
          "noticeId": "{{id ?? NoticeId}}", "revision": 7,
          "caseId": "{{CaseId}}", "caseRevision": 11,
          "notice": {
            "allegation": { "value": "Approved allegation" },
            "applicableRule": { "value": "Approved rule" },
            "responseInstructions": { "value": "Provide your account of the matter." }
          },
          "retainUntil": "{{(expires ?? Now.AddHours(1)):O}}",
          "acknowledgedAt": {{(acknowledged ? "\"2026-10-13T12:01:00Z\"" : "null")}},
          "respondedAt": {{(responded ? "\"2026-10-13T12:02:00Z\"" : "null")}},
          "response": {{(responded ? "\"My permitted response\"" : "null")}},
          "_links": {{(actions ? """{"acknowledge":{"href":"/api/moderation/pre-finding/notices/acknowledgement","method":"POST"},"submit-response":{"href":"/api/moderation/pre-finding/notices/response","method":"POST"}}""" : "{}")}},
          "confidentialOccurrence": "Never display this"
        }
        """;

    internal static JsonElement Body(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }
}
