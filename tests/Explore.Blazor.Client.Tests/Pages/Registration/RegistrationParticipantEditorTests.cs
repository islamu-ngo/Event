using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Pages.Registration;

namespace Explore.Blazor.Client.Tests.Pages.Registration;

public sealed class RegistrationParticipantEditorTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly IRegistrationOrderService _service;

    public RegistrationParticipantEditorTests() =>
        _service = _ctx.AddMockService<IRegistrationOrderService>();

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task Render_UsesLineModesForRequiredChildAndOptionalAdultFields()
    {
        var eventId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        _service.GetCurrentParticipantsAsync(eventId, orderId, Arg.Any<CancellationToken>())
            .Returns(Participants(orderId, """
                { "ticketTypeName":"Child", "quantity":1, "participantDataCollectionModeId":4, "participantDataCollectionModeCode":"PER_TICKET_REQUIRED", "requiresGuardian":true },
                { "ticketTypeName":"Adult", "quantity":1, "participantDataCollectionModeId":3, "participantDataCollectionModeCode":"PER_TICKET_OPTIONAL", "requiresGuardian":false }
                """));

        var cut = _ctx.RenderMudComponent<RegistrationParticipantEditor>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.OrderId, orderId));

        cut.WaitForElement("[data-testid='participant-Child-1-name']");
        await Assert.That(cut.Find("[data-testid='participant-Child-1-name']").HasAttribute("required")).IsTrue();
        await Assert.That(cut.Find("[data-testid='participant-Adult-1-name']").HasAttribute("required")).IsFalse();
        await Assert.That(cut.Markup).Contains("Guardian required");
    }

    [Test]
    public async Task Render_DeferredUnnamedEmployeeShowsDeadlineAndReminder()
    {
        var eventId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var deadline = TestTime.UtcNow.AddDays(5);
        _service.GetCurrentParticipantsAsync(eventId, orderId, Arg.Any<CancellationToken>())
            .Returns(Participants(orderId,
                "{ \"ticketTypeName\":\"Employee\", \"quantity\":1, \"participantDataCollectionModeId\":5, \"participantDataCollectionModeCode\":\"DEFERRED_ASSIGNMENT\", \"requiresGuardian\":false }",
                $"\"assignmentDeadline\":\"{deadline:O}\""));

        var cut = _ctx.RenderMudComponent<RegistrationParticipantEditor>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.OrderId, orderId));

        cut.WaitForElement("[data-testid='participant-deadline']");
        await Assert.That(cut.Markup).Contains("Assignment outstanding");
        await Assert.That(cut.Markup).Contains(deadline.ToString("d"));
    }

    [Test]
    public async Task Render_MultipleDeferredLinesUseOneSharedDeadlineControl()
    {
        var eventId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        _service.GetCurrentParticipantsAsync(eventId, orderId, Arg.Any<CancellationToken>())
            .Returns(Participants(orderId, """
                { "ticketTypeName":"Employee", "quantity":1, "participantDataCollectionModeId":5, "participantDataCollectionModeCode":"DEFERRED_ASSIGNMENT", "requiresGuardian":false },
                { "ticketTypeName":"Volunteer", "quantity":1, "participantDataCollectionModeId":5, "participantDataCollectionModeCode":"DEFERRED_ASSIGNMENT", "requiresGuardian":false }
                """));

        var cut = _ctx.RenderMudComponent<RegistrationParticipantEditor>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.OrderId, orderId));

        cut.WaitForElement("#participant-shared-deadline");
        await Assert.That(cut.FindAll("#participant-shared-deadline").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("label[for='participant-shared-deadline']").Count).IsEqualTo(1);
    }

    [Test]
    public async Task CopyBuyerDetailsToAll_LeavesParticipantFieldsEditable()
    {
        var eventId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        _service.GetCurrentParticipantsAsync(eventId, orderId, Arg.Any<CancellationToken>())
            .Returns(Participants(orderId,
                "{ \"ticketTypeName\":\"Adult\", \"quantity\":2, \"participantDataCollectionModeId\":3, \"participantDataCollectionModeCode\":\"PER_TICKET_OPTIONAL\", \"requiresGuardian\":false }"));

        var cut = _ctx.RenderMudComponent<RegistrationParticipantEditor>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.OrderId, orderId));

        cut.WaitForElement("[data-testid='buyer-name']");
        await cut.Find("[data-testid='buyer-name']").ChangeAsync(new ChangeEventArgs { Value = "Buyer name" });
        await cut.Find("[data-testid='copy-buyer-all']").ClickAsync(new());

        var copied = cut.Find("[data-testid='participant-Adult-1-name']");
        await Assert.That(copied.GetAttribute("value")).IsEqualTo("Buyer name");
        await Assert.That(copied.HasAttribute("readonly")).IsFalse();
        await Assert.That(copied.HasAttribute("disabled")).IsFalse();
    }

    private static HalResourceOfRegistrationOrderParticipantsDto Participants(
        Guid orderId,
        string lines,
        string assignmentExtra = "")
    {
        JsonArray lineNodes = JsonNode.Parse($"[{lines}]")!.AsArray();
        var assignments = new JsonArray();
        foreach (JsonObject line in lineNodes.Cast<JsonObject>())
        {
            var lineId = Guid.CreateVersion7();
            line["id"] = lineId;
            int quantity = line["quantity"]!.GetValue<int>();
            for (var ordinal = 1; ordinal <= quantity; ordinal++)
            {
                var assignment = new JsonObject
                {
                    ["id"] = Guid.CreateVersion7(),
                    ["registrationOrderLineId"] = lineId,
                    ["ordinal"] = ordinal,
                    ["participantId"] = null,
                    ["assignmentStatusId"] = 1
                };
                if (!string.IsNullOrEmpty(assignmentExtra))
                {
                    JsonObject extra = JsonNode.Parse($"{{{assignmentExtra}}}")!.AsObject();
                    foreach ((string key, JsonNode? value) in extra)
                    {
                        assignment[key] = value?.DeepClone();
                    }
                }

                assignments.Add(assignment);
            }
        }

        var resource = new JsonObject
        {
            ["registrationOrderId"] = orderId,
            ["lines"] = lineNodes,
            ["participants"] = new JsonArray(),
            ["assignments"] = assignments,
            ["_links"] = JsonNode.Parse("""{"add-participant":{"href":"/participants","method":"POST"},"assign-tickets":{"href":"/assignments","method":"PUT"},"defer-tickets":{"href":"/assignments/deferred","method":"PUT"}}""")
        };
        return JsonSerializer.Deserialize<HalResourceOfRegistrationOrderParticipantsDto>(resource.ToJsonString())!;
    }
}
