using System.Text.Json;
using Explore.Application.Services;

namespace Event.Persistence.IntegrationTests;

public sealed partial class EventResourceDiscoveryQueryTests
{
    [Test]
    [Arguments(false, "current")]
    [Arguments(true, "current")]
    [Arguments(false, "after-preparation")]
    [Arguments(true, "after-preparation")]
    [Arguments(false, "during-representation")]
    [Arguments(true, "during-representation")]
    [Arguments(false, "revoked-status")]
    [Arguments(true, "revoked-status")]
    public async Task NativeManagementAuditQuarantineKeepsHistoryPrivateAtTerminal(
        bool storedFile, string boundary)
    {
        var (scope, actor) = await SeedAsync();
        var resource = await ActionResourceAsync(scope, actor, storedFile ? "download" : "access");
        var audit = await ManagementAuditAsync(resource, actor);
        bool safeStatus = boundary is "current" or "revoked-status";
        if (safeStatus) await ApplyManagementQuarantineAsync(resource);
        await using var context = database.CreateMeasurementContext();
        await using var request = ManagementWorkflow(context, scope.TenantAId, actor);
        await request.InitializeAsync();
        await request.PrepareAsync();
        var workflow = request.Workflow;
        var read = await workflow.GetAuditAsync(resource.Id, 100, default);
        await Assert.That(read.Outcome).IsEqualTo(EventResourceAuthorityOutcome.Allowed);
        await Assert.That(read.Projection).IsNotNull();
        await Assert.That(JsonSerializer.SerializeToElement(read).TryGetProperty("Projection", out _)).IsFalse();
        if (boundary == "after-preparation") await ApplyManagementQuarantineAsync(resource);
        bool prepared = false;
        bool delivered = false;
        var outcome = await read.Projection!.DiscloseAsync(async (value, _) =>
        {
            if (safeStatus)
            {
                await Assert.That(value.Status).IsEqualTo("quarantined");
                await Assert.That(value.MaterialVersionId).IsEqualTo(resource.MaterialVersionId);
                await Assert.That(value.Items.IsEmpty).IsTrue();
            }
            else
            {
                await Assert.That(value.Items.Single().Id).IsEqualTo(audit.Id);
                await Assert.That(value.Items.Single().ResponsibleManagerUserId).IsEqualTo(actor);
            }
            var representation = JsonSerializer.SerializeToElement(value);
            if (!safeStatus)
                await Assert.That(representation.GetProperty("Items")[0].GetProperty("Id").GetGuid())
                    .IsEqualTo(audit.Id);
            await Assert.That(context.Database.CurrentTransaction).IsNull();
            if (boundary == "during-representation") await ApplyManagementQuarantineAsync(resource);
            if (boundary == "revoked-status") await RevokeManagementOrganizerAsync(scope.EventAId);
            prepared = true;
            return representation;
        }, (_, _) => { delivered = true; return Task.CompletedTask; });
        await Assert.That(prepared).IsTrue();
        await Assert.That(delivered).IsEqualTo(boundary == "current");
        if (boundary == "current") await Assert.That(outcome).IsEqualTo(EventResourceAuthorityOutcome.Allowed);
        else if (boundary == "revoked-status")
        {
            await Assert.That(outcome).IsNotEqualTo(EventResourceAuthorityOutcome.Allowed);
            var denied = await workflow.GetAuditAsync(resource.Id, 100, default);
            await Assert.That(denied.Projection).IsNull();
            await Assert.That(denied.Outcome).IsNotEqualTo(EventResourceAuthorityOutcome.Allowed);
        }
        else
        {
            await Assert.That(outcome).IsEqualTo(EventResourceAuthorityOutcome.NotFound);
            var fresh = await DiscloseAsync((await workflow.GetAuditAsync(resource.Id, 100, default)).Projection);
            await Assert.That(fresh.Status).IsEqualTo("quarantined");
            await Assert.That(fresh.MaterialVersionId).IsEqualTo(resource.MaterialVersionId);
            await Assert.That(fresh.Items.IsEmpty).IsTrue();
        }
        await RecordAudienceDatabaseObservationAsync(context);
    }
}
