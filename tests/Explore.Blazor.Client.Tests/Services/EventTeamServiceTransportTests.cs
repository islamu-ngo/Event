using System.Net;
using System.Text;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Helpers;
using Explore.Blazor.Client.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class EventTeamServiceTransportTests
{
    [Test]
    [Arguments("Pending", true)]
    [Arguments("Active", true)]
    [Arguments("Revoked", true)]
    [Arguments("Expired", true)]
    [Arguments("Active", false)]
    public async Task NativeStringStatusPreservesRosterAndAssignmentAffordance(
        string status,
        bool canAssign)
    {
        var eventId = Guid.Parse("01998880-0000-7000-8000-000000000340");
        var assignmentLink = canAssign
            ? """
              "assign-event-role": {
                "href": "/api/eventteam/by-event/01998880-0000-7000-8000-000000000340/assignments",
                "method": "POST"
              }
              """
            : string.Empty;
        var payload = $$"""
            {
              "_links": { {{assignmentLink}} },
              "_embedded": {
                "items": [{
                  "assignmentId": "01998880-0000-7000-8000-000000000341",
                  "userId": "01998880-0000-7000-8000-000000000342",
                  "userEmail": "owner@example.test",
                  "userFullName": "Event Owner",
                  "roleId": 1,
                  "roleName": "Owner",
                  "roleMasterCode": "event.owner",
                  "status": "{{status}}",
                  "startsAtUtc": "2026-10-01T00:00:00Z",
                  "expiresAtUtc": null,
                  "isEffective": true,
                  "createdAt": "2026-10-01T00:00:00Z",
                  "createdBy": null,
                  "_links": {}
                }]
              }
            }
            """;
        using var handler = new TeamResponseHandler(payload);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://bff.example/") };
        var client = new EventTeamClient(httpClient);

        var generatedResponse = await client.GetEventTeamAsync(eventId, includeInactive: false);
        var generatedMember = generatedResponse._embedded?.Items?.Single()
            ?? throw new InvalidOperationException("Team response has no member.");
        await Assert.That(generatedMember.Status.ToString()).IsEqualTo(status);

        var service = new EventTeamService(client, NullLogger<EventTeamService>.Instance);
        var team = await service.GetTeamMembersAsync(eventId, includeInactive: false);
        var member = team.GetItems().Single();
        await Assert.That(member.Status.ToString()).IsEqualTo(status);
        await Assert.That(member.UserFullName).IsEqualTo("Event Owner");
        await Assert.That(member.IsEffective).IsEqualTo(true);
        await Assert.That(team._links!.ContainsKey("assign-event-role")).IsEqualTo(canAssign);
        if (canAssign)
        {
            await Assert.That(team._links["assign-event-role"].Method).IsEqualTo("POST");
            await Assert.That(team._links["assign-event-role"].Href)
                .IsEqualTo("/api/eventteam/by-event/01998880-0000-7000-8000-000000000340/assignments");
        }
        await Assert.That(handler.PathAndQuery)
            .IsEqualTo($"/api/eventteam/by-event/{eventId}?includeInactive=false");
    }

    private sealed class TeamResponseHandler(string payload) : HttpMessageHandler
    {
        public string? PathAndQuery { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            PathAndQuery = request.RequestUri!.PathAndQuery;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/hal+json")
            });
        }
    }
}
