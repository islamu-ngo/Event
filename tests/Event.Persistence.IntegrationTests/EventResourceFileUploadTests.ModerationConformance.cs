using Explore.Application.DTOs.EventResource;
using Explore.Application.Features.StorageObjects.Requests.Commands;
using Explore.Application.Models.Storage;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Event.Persistence.IntegrationTests;

public sealed partial class EventResourceFileUploadTests
{
    // Aliases: reserve, reserve replay, replacement finalize, finalized replay,
    // cancel, canceled replay. Every case first attaches a real inspected document.
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    public async Task NativeUploadAliasesDiscloseExactlyOnceWithoutQuarantine(int operation)
    {
        var scenario = await PrepareUploadAliasAsync(operation);
        await using var context = database.CreateContext();
        await using var result = await ExecuteUploadAliasAsync(context, scenario, operation);
        await AssertPrivateUploadEnvelopeAsync(result);
        int delivered = 0;
        await Assert.That(await result.Projection!.DiscloseAsync((response, _) =>
        {
            return Task.FromResult(response);
        }, async (response, _) =>
        {
            await Assert.That(response.IsSuccess).IsTrue();
            await Assert.That(response.Id!.SafeDisplayName).IsEqualTo("resource.pdf");
            await Assert.That(response.Id.TenantId).IsEqualTo(scenario.Seed.TenantId);
            delivered++;
        })).IsEqualTo(EventResourceAuthorityOutcome.Allowed);
        await Assert.That(delivered).IsEqualTo(1);
        await Assert.That(await result.Projection.DiscloseAsync(
            (response, _) => Task.FromResult(response),
            (_, _) => { delivered++; return Task.CompletedTask; }))
            .IsEqualTo(EventResourceAuthorityOutcome.NotFound);
        await Assert.That(delivered).IsEqualTo(1);
        await AssertUploadAliasCustodyAsync(scenario, operation);
        await RecordUploadDatabaseObservationAsync(context);
    }

    [Test]
    [Arguments(0, false)]
    [Arguments(1, false)]
    [Arguments(2, false)]
    [Arguments(3, false)]
    [Arguments(4, false)]
    [Arguments(5, false)]
    [Arguments(0, true)]
    [Arguments(1, true)]
    [Arguments(2, true)]
    [Arguments(3, true)]
    [Arguments(4, true)]
    [Arguments(5, true)]
    public async Task NativeUploadAliasesWithholdStoredFieldsWhenQuarantinedAfterPreparation(
        int operation, bool duringRepresentation)
    {
        var scenario = await PrepareUploadAliasAsync(operation);
        await using var context = database.CreateContext();
        await using var result = await ExecuteUploadAliasAsync(context, scenario, operation);
        await AssertPrivateUploadEnvelopeAsync(result);
        UploadQuarantine? cause = null;
        if (!duringRepresentation) cause = await QuarantineUploadMaterialAsync(scenario.Seed);
        bool prepared = false, delivered = false;
        var outcome = await result.Projection!.DiscloseAsync(async (response, _) =>
        {
            prepared = true;
            await Assert.That(response.Id!.SafeDisplayName).IsEqualTo("resource.pdf");
            if (duringRepresentation) cause = await QuarantineUploadMaterialAsync(scenario.Seed);
            return System.Text.Json.JsonSerializer.SerializeToElement(response);
        }, (_, _) => { delivered = true; return Task.CompletedTask; });
        // A quarantined payload is not a successful terminal disclosure. Do not turn
        // a management-authority gap into an expected allow result or a fake lease.
        await Assert.That(outcome == EventResourceAuthorityOutcome.Forbidden
            || outcome == EventResourceAuthorityOutcome.NotFound).IsTrue();
        await Assert.That(prepared).IsTrue();
        await Assert.That(delivered).IsFalse();
        await Assert.That(await result.Projection.DiscloseAsync(
            (response, _) => Task.FromResult(response),
            (_, _) => { delivered = true; return Task.CompletedTask; }))
            .IsEqualTo(EventResourceAuthorityOutcome.NotFound);
        await Assert.That(delivered).IsFalse();
        await AssertUploadQuarantineAsync(scenario.Seed, cause!);
        await AssertUploadAliasCustodyAsync(scenario, operation);
        await RecordUploadDatabaseObservationAsync(context);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    public async Task NativeUploadReserveCancelAndReplayWithholdFieldsWhenAlreadyQuarantined(int operation)
    {
        var scenario = await PrepareUploadAliasAsync(operation);
        var cause = await QuarantineUploadMaterialAsync(scenario.Seed);
        await using var context = database.CreateContext();
        await using var result = await ExecuteUploadAliasAsync(context, scenario, operation);
        bool delivered = false;
        if (result.Projection is { } projection)
        {
            await AssertPrivateUploadEnvelopeAsync(result);
            var outcome = await projection.DiscloseAsync(
                (response, _) => Task.FromResult(System.Text.Json.JsonSerializer.SerializeToElement(response)),
                (_, _) => { delivered = true; return Task.CompletedTask; });
            await Assert.That(outcome == EventResourceAuthorityOutcome.Forbidden
                || outcome == EventResourceAuthorityOutcome.NotFound).IsTrue();
            await Assert.That(await projection.DiscloseAsync(
                (response, _) => Task.FromResult(response),
                (_, _) => { delivered = true; return Task.CompletedTask; }))
                .IsEqualTo(EventResourceAuthorityOutcome.NotFound);
        }
        else
        {
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Response!.Id).IsNull();
        }
        await Assert.That(delivered).IsFalse();
        await AssertUploadQuarantineAsync(scenario.Seed, cause);
        // Denial must not silently release an outstanding reservation or attach anything.
        await using var verify = database.CreateContext();
        var resource = await verify.EventResources.SingleAsync(row => row.Id == scenario.Seed.ResourceId);
        await Assert.That(resource.StorageObjectId).IsEqualTo(scenario.AttachedObjectId);
        var quota = await verify.StorageUsageCounters.SingleAsync(row => row.TenantId == scenario.Seed.TenantId);
        await Assert.That(quota.UsedBytes).IsEqualTo(Pdf.Length);
        await Assert.That(quota.ReservedBytes).IsEqualTo(operation is 1 or 4 ? Pdf.Length : 0);
        await RecordUploadDatabaseObservationAsync(context);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeReplacementFinalizeCannotMutateQuarantinedMaterial(bool duringProviderWrite)
    {
        var scenario = await PrepareUploadAliasAsync(2);
        UploadQuarantine? cause = null;
        if (!duringProviderWrite) cause = await QuarantineUploadMaterialAsync(scenario.Seed);
        await using var context = database.CreateContext();
        var consumer = Workflow(context, scenario.Seed, async () =>
        {
            if (duringProviderWrite) cause = await QuarantineUploadMaterialAsync(scenario.Seed);
        });
        var denied = await FinalizeAsync(consumer, scenario.SessionId);
        await Assert.That(denied.IsSuccess).IsFalse();
        await Assert.That(denied.Id).IsNull();
        await AssertUploadQuarantineAsync(scenario.Seed, cause!);
        await using var verify = database.CreateContext();
        var resource = await verify.EventResources.SingleAsync(row => row.Id == scenario.Seed.ResourceId);
        await Assert.That(resource.StorageObjectId).IsEqualTo(scenario.AttachedObjectId);
        var session = await verify.StorageUploadSessions.SingleAsync(row => row.Id == scenario.SessionId);
        await Assert.That(session.Status).IsEqualTo(duringProviderWrite
            ? StorageUploadSessionStates.Failed : StorageUploadSessionStates.Reserved);
        await Assert.That(session.ProducerSettled).IsEqualTo(duringProviderWrite);
        if (duringProviderWrite)
        {
            var staged = await verify.StorageObjects.SingleAsync(row => row.Id == session.StorageObjectId);
            await Assert.That(staged.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.DeleteRequested);
            await Assert.That(staged.StorageProviderBindingId).IsEqualTo(session.StorageProviderBindingId);
            await Assert.That(staged.ObjectKey).IsEqualTo(session.ObjectKey);
            var tombstone = await verify.StorageObjectDeletionTombstones.SingleAsync(row => row.Id == staged.Id);
            await Assert.That(tombstone.State).IsEqualTo(Explore.Domain.Enums.StorageObjectDeletionState.Ready);
            await Assert.That(tombstone.ProviderBindingId).IsEqualTo(staged.StorageProviderBindingId!.Value);
            await Assert.That(tombstone.ObjectKey).IsEqualTo(staged.ObjectKey);
        }
        else
            await Assert.That(session.StorageObjectId).IsNull();
        var quota = await verify.StorageUsageCounters.SingleAsync(row => row.TenantId == scenario.Seed.TenantId);
        await Assert.That(quota.UsedBytes).IsEqualTo(Pdf.Length);
        await Assert.That(quota.ReservedBytes).IsEqualTo(duringProviderWrite ? 0 : Pdf.Length);
        await Assert.That(await verify.EventResourceAuditEntries.CountAsync(row =>
            row.EventResourceId == scenario.Seed.ResourceId)).IsEqualTo(1);
        await RecordUploadDatabaseObservationAsync(context);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    public async Task NativeUploadAliasesWithholdFieldsWhenTerminalResourceDisappears(int operation)
    {
        var scenario = await PrepareUploadAliasAsync(operation);
        await using var context = database.CreateContext();
        await using var result = await ExecuteUploadAliasAsync(context, scenario, operation);
        await AssertPrivateUploadEnvelopeAsync(result);
        UploadQuarantine? cause = null;
        Guid detachedObjectId = Guid.Empty;
        bool delivered = false;
        var outcome = await result.Projection!.DiscloseAsync(async (response, token) =>
        {
            cause = await QuarantineUploadMaterialAsync(scenario.Seed);
            await using var deleting = database.CreateContext();
            await new EfCoreUnitOfWork(deleting).ExecuteSerializableAsync(async ct =>
            {
                var resource = await deleting.EventResources.SingleAsync(row => row.Id == scenario.Seed.ResourceId, ct);
                detachedObjectId = resource.Delete(resource.ConcurrencyStamp, scenario.Seed.UserId, Now)!.Value;
                await deleting.SaveChangesAsync(ct);
                await Lifecycle(deleting).RetireAsync(scenario.Seed.TenantId, [resource.Id], [detachedObjectId], Now, ct);
                return true;
            }, token);
            return System.Text.Json.JsonSerializer.SerializeToElement(response);
        }, (_, _) => { delivered = true; return Task.CompletedTask; });
        await Assert.That(outcome).IsEqualTo(EventResourceAuthorityOutcome.NotFound);
        await Assert.That(delivered).IsFalse();
        await Assert.That(await result.Projection.DiscloseAsync(
            (response, _) => Task.FromResult(response),
            (_, _) => { delivered = true; return Task.CompletedTask; }))
            .IsEqualTo(EventResourceAuthorityOutcome.NotFound);
        await Assert.That(delivered).IsFalse();
        await using var verify = database.CreateContext();
        await Assert.That(await verify.EventResources.AnyAsync(row => row.Id == scenario.Seed.ResourceId)).IsFalse();
        var retained = await verify.Set<EventResourceModerationQuarantine>().SingleAsync(row => row.Id == cause!.Id);
        await Assert.That(retained.MaterialVersionId).IsEqualTo(cause!.MaterialVersionId);
        await Assert.That(retained.EffectId).IsEqualTo(cause.EffectId);
        await Assert.That(retained.ReleaseId).IsNull();
        var deleted = await new EventResourceRepository(verify).GetReplayIdentityAsync(
            scenario.Seed.TenantId, scenario.Seed.ResourceId, default);
        await Assert.That(deleted!.IsDeleted).IsTrue();
        await Assert.That(deleted.MaterialVersionId).IsEqualTo(retained.MaterialVersionId);
        await Assert.That(deleted.ModerationQuarantineCount).IsEqualTo(1);
        await Assert.That(deleted.StorageObjectId).IsNull();
        var retired = await verify.StorageObjects.SingleAsync(row => row.Id == detachedObjectId);
        await Assert.That(retired.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.DeleteRequested);
        var tombstone = await verify.StorageObjectDeletionTombstones.SingleAsync(row => row.Id == detachedObjectId);
        await Assert.That(tombstone.ProviderBindingId).IsEqualTo(retired.StorageProviderBindingId);
        await Assert.That(tombstone.ObjectKey).IsEqualTo(retired.ObjectKey);
        await Assert.That(tombstone.ProviderObjectVersion).IsEqualTo(retired.ProviderVersionId);
        var quota = await verify.StorageUsageCounters.SingleAsync(row => row.TenantId == scenario.Seed.TenantId);
        await Assert.That(quota.UsedBytes).IsEqualTo(0);
        await Assert.That(quota.ReservedBytes).IsEqualTo(0);
        await Assert.That(quota.ObjectCount).IsEqualTo(0);
        await RecordUploadDatabaseObservationAsync(context);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    public async Task NativeUploadAliasesHonorCancellationDuringRepresentation(int operation)
    {
        var scenario = await PrepareUploadAliasAsync(operation);
        await using var context = database.CreateContext();
        await using var result = await ExecuteUploadAliasAsync(context, scenario, operation);
        await AssertPrivateUploadEnvelopeAsync(result);
        using var cancellation = new CancellationTokenSource();
        bool delivered = false;
        await Assert.That(async () => await result.Projection!.DiscloseAsync((response, _) =>
        {
            cancellation.Cancel();
            return Task.FromResult(response);
        }, (_, _) => { delivered = true; return Task.CompletedTask; }, cancellation.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(delivered).IsFalse();
        await Assert.That(await result.Projection!.DiscloseAsync(
            (response, _) => Task.FromResult(response),
            (_, _) => { delivered = true; return Task.CompletedTask; }))
            .IsEqualTo(EventResourceAuthorityOutcome.NotFound);
        await Assert.That(delivered).IsFalse();
        await AssertUploadAliasCustodyAsync(scenario, operation);
        await RecordUploadDatabaseObservationAsync(context);
    }

    private async Task<UploadAliasScenario> PrepareUploadAliasAsync(int operation)
    {
        var seed = await SeedAsync();
        await using var context = database.CreateContext();
        var consumer = Workflow(context, seed);
        var first = await consumer.ReserveAsync(seed.ResourceId, Intent(seed.Version), default);
        await Assert.That(first.IsSuccess).IsTrue();
        var attached = await FinalizeAsync(consumer, first.Id!.Id);
        await Assert.That(attached.IsSuccess).IsTrue();
        await using var reload = database.CreateContext();
        var resource = await reload.EventResources.SingleAsync(row => row.Id == seed.ResourceId);
        await Assert.That(resource.MaterialVersionId).IsNotNull();
        await Assert.That(resource.MaterialVersionId).IsNotEqualTo(Guid.Empty);
        seed = seed with { Version = resource.ConcurrencyStamp };
        var intent = Intent(seed.Version);
        Guid sessionId = first.Id.Id;
        if (operation is 1 or 2 or 4 or 5)
        {
            var pending = await consumer.ReserveAsync(seed.ResourceId, intent, default);
            await Assert.That(pending.IsSuccess).IsTrue();
            sessionId = pending.Id!.Id;
            if (operation == 5)
                await Assert.That((await consumer.CancelAsync(sessionId, default)).IsSuccess).IsTrue();
        }
        return new(seed, intent, sessionId, attached.Id!.StorageObjectId!.Value);
    }

    private async Task<StorageUploadSessionResult> ExecuteUploadAliasAsync(
        ExploreDbContext context, UploadAliasScenario scenario, int operation)
    {
        var workflow = Workflow(context, scenario.Seed).Workflow;
        if (operation is 0 or 1)
            return await workflow.ReserveAsync(scenario.Seed.ResourceId, scenario.Intent, default);
        if (operation is 4 or 5)
            return await workflow.CancelAsync(scenario.SessionId, default);
        using var content = new MemoryStream(Pdf);
        return await workflow.FinalizeAsync(new FinalizeStorageUploadSessionCommand
        {
            UploadSessionId = scenario.SessionId, Content = content,
            ContentLength = Pdf.Length, ContentType = "application/pdf"
        }, default);
    }

    private static async Task AssertPrivateUploadEnvelopeAsync(StorageUploadSessionResult result)
    {
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Response).IsNull();
        await Assert.That(result.Projection).IsNotNull();
        var serialized = System.Text.Json.JsonSerializer.SerializeToElement(result);
        await Assert.That(serialized.TryGetProperty("Id", out _)).IsFalse();
        await Assert.That(serialized.TryGetProperty("Response", out _)).IsFalse();
        await Assert.That(serialized.TryGetProperty("Projection", out _)).IsFalse();
    }

    private async Task<UploadQuarantine> QuarantineUploadMaterialAsync(Seed seed)
    {
        await using var context = database.CreateContext();
        var resource = await context.EventResources.Include(row => row.ModerationQuarantines)
            .SingleAsync(row => row.Id == seed.ResourceId && row.TenantId == seed.TenantId);
        var material = resource.MaterialVersionId
            ?? throw new InvalidOperationException("A finalized native upload must have current material.");
        var stamp = resource.ConcurrencyStamp;
        await Assert.That(resource.StorageObjectId).IsNotNull();
        var cause = resource.ApplyModerationQuarantine(Guid.CreateVersion7(), Guid.CreateVersion7(), 0,
            material, stamp, new DateTimeOffset(Now));
        await context.SaveChangesAsync();
        return new(cause.Id, cause.EffectId, material, stamp);
    }

    private async Task AssertUploadQuarantineAsync(Seed seed, UploadQuarantine expected)
    {
        await using var context = database.CreateContext();
        var resource = await context.EventResources.Include(row => row.ModerationQuarantines)
            .SingleAsync(row => row.Id == seed.ResourceId && row.TenantId == seed.TenantId);
        await Assert.That(resource.HasModerationQuarantine).IsTrue();
        await Assert.That(resource.ModerationQuarantineCount).IsEqualTo(1);
        await Assert.That(resource.MaterialVersionId).IsEqualTo(expected.MaterialVersionId);
        await Assert.That(resource.ConcurrencyStamp).IsEqualTo(expected.Stamp);
        var cause = resource.ModerationQuarantines.Single();
        await Assert.That(cause.Id).IsEqualTo(expected.Id);
        await Assert.That(cause.EffectId).IsEqualTo(expected.EffectId);
        await Assert.That(cause.MaterialVersionId).IsEqualTo(resource.MaterialVersionId);
        await Assert.That(cause.ReleaseId).IsNull();
        await Assert.That(resource.HasPublishablePayload()).IsFalse();
    }

    private async Task AssertUploadAliasCustodyAsync(UploadAliasScenario scenario, int operation)
    {
        await using var context = database.CreateContext();
        var resource = await context.EventResources.SingleAsync(row => row.Id == scenario.Seed.ResourceId);
        var attached = await context.StorageObjects.SingleAsync(row => row.Id == resource.StorageObjectId);
        await Assert.That(attached.OwningResourceId).IsEqualTo(resource.Id);
        await Assert.That(attached.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.Active);
        await Assert.That(attached.HasBoundDocumentInspection).IsTrue();
        if (operation == 2)
            await Assert.That(attached.Id).IsNotEqualTo(scenario.AttachedObjectId);
        else
            await Assert.That(attached.Id).IsEqualTo(scenario.AttachedObjectId);
        var quota = await context.StorageUsageCounters.SingleAsync(row => row.TenantId == scenario.Seed.TenantId);
        await Assert.That(quota.UsedBytes).IsEqualTo(Pdf.Length);
        await Assert.That(quota.ObjectCount).IsEqualTo(1);
        await Assert.That(quota.ReservedBytes).IsEqualTo(operation is 0 or 1 ? Pdf.Length : 0);
        await Assert.That(await context.EventResourceAuditEntries.CountAsync(row =>
            row.EventResourceId == resource.Id)).IsEqualTo(operation == 2 ? 2 : 1);
        if (operation is 4 or 5)
            await Assert.That((await context.StorageUploadSessions.SingleAsync(row =>
                row.Id == scenario.SessionId)).Status).IsEqualTo(StorageUploadSessionStates.Canceled);
    }

    private static async Task RecordUploadDatabaseObservationAsync(ExploreDbContext context)
    {
        if (Environment.GetEnvironmentVariable("ISLAMU_MODERATION_MEASUREMENT_JOURNAL") != "1") return;
        var identity = context.GetService<IDbContextOptions>()
            .FindExtension<PrimaryDatabasePublicIdentityOptionsExtension>()
            ?? throw new InvalidOperationException("Upload measurement requires native primary database composition.");
        var provider = identity.Provider switch
        {
            Explore.Secrets.Database.PrimaryDatabaseProvider.PostgreSql => "Npgsql.EntityFrameworkCore.PostgreSQL",
            Explore.Secrets.Database.PrimaryDatabaseProvider.Sqlite => "Microsoft.EntityFrameworkCore.Sqlite",
            Explore.Secrets.Database.PrimaryDatabaseProvider.SqlServer => "Microsoft.EntityFrameworkCore.SqlServer",
            Explore.Secrets.Database.PrimaryDatabaseProvider.MariaDb or Explore.Secrets.Database.PrimaryDatabaseProvider.MySql
                => "Pomelo.EntityFrameworkCore.MySql",
            _ => null
        };
        if (provider is null || context.Database.ProviderName != provider)
            throw new InvalidOperationException("Upload measurement must observe its actual composed EF provider.");
        var connection = context.Database.GetDbConnection();
        bool alreadyOpen = connection.State == System.Data.ConnectionState.Open;
        if (!alreadyOpen) await context.Database.OpenConnectionAsync();
        try
        {
            Event.Tests.Shared.Moderation.ModerationMeasurementJournalAttribute.RecordDatabaseObservation(
                TUnit.Core.TestContext.Current!, identity.Provider.ToString(), connection.ServerVersion, identity.PublicDigest);
        }
        finally
        {
            if (!alreadyOpen) await context.Database.CloseConnectionAsync();
        }
    }

    private sealed record UploadAliasScenario(
        Seed Seed, CreateEventResourceUploadSessionDto Intent, Guid SessionId, Guid AttachedObjectId);
    private sealed record UploadQuarantine(Guid Id, Guid EffectId, Guid MaterialVersionId, Guid Stamp);
}
