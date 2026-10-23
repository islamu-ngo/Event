using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Native = Explore.Persistence.Security.PostgresSupportAdministrationContract;

namespace Event.Persistence.IntegrationTests;

public sealed partial class SupportAdministrationNativeContractTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task OriginalSupportSourceReportsExactOwnTerminationWithoutRenewingTheHorizon(bool force, bool write)
    {
        var source = await SeedAsync();
        var accepted = Mutation();
        var start = Start(source) with { ModeId = (int)(write ? SupportAccessModeEnum.Write : SupportAccessModeEnum.ReadOnly) };
        await StageAsync(start, accepted);
        var request = new Native.Request(force ? "ForceStop" : "OwnStop", source.Actor, source.Tenant,
            accepted.SessionId, EndReasonText: "Original support completed");
        await using var caller = Open(PrimaryDatabaseRole.Runtime, source.Tenant);
        await caller.Database.OpenConnectionAsync();
        string original = await Native.ReadAsync(caller, fixture.NativeQueueEnrollment, request, default);
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var initialSupport = await ReadSupport(caller, source, accepted.SessionId!.Value);
        await Native.AcquireAsync(caller, fixture.NativeQueueEnrollment, request, original, default);
        await Native.CompileAsync(caller, fixture.NativeQueueEnrollment, request,
            Mutation() with { SessionId = accepted.SessionId }, default);
        string applied = await Native.ApplyPlanAsync(caller, fixture.NativeQueueEnrollment, request, default);
        var finalSupport = await ReadSupport(caller, source, accepted.SessionId.Value);
        var initial = initialSupport["q0"]![0]!;
        var final = finalSupport["q0"]![0]!;
        await Assert.That(final["StatusId"]!.GetValue<int>())
            .IsEqualTo((int)(force ? SupportAccessSessionStatusEnum.Revoked : SupportAccessSessionStatusEnum.Stopped));
        await Assert.That(final["EndedAtUtc"]).IsNotNull();
        foreach (string property in new[] { "Id", "ActorUserId", "TargetTenantId", "TargetTenantUserId",
                     "ModeId", "StartedAtUtc", "ExpiresAtUtc", "ReasonCode", "TicketReference" })
            await Assert.That(JsonNode.DeepEquals(initial[property], final[property])).IsTrue();
        await Assert.That(JsonNode.DeepEquals(initial["ConcurrencyStamp"], final["ConcurrencyStamp"])).IsFalse();
        await Native.CompleteAsync(caller, fixture.NativeQueueEnrollment, request, applied, default);
        await owner.CommitAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LimitedSessionAndAuditPagesOrderBeforeApplyingTheBound(bool auditPage)
    {
        var source = await SeedAsync();
        var first = Mutation() with { TransitionAt = Instant().AddMinutes(-5) };
        await StageAsync(Start(source), first);
        var stopped = Mutation() with { SessionId = first.SessionId, TransitionAt = first.TransitionAt!.Value.AddMinutes(1) };
        await StageAsync(new("OwnStop", source.Actor, source.Tenant, first.SessionId), stopped);
        var second = Mutation() with { TransitionAt = first.TransitionAt.Value.AddMinutes(2) };
        await StageAsync(Start(source), second);
        var request = auditPage
            ? new Native.Request("Audit", source.Actor, source.Tenant, first.SessionId, TargetTenantId: source.Tenant, Limit: 1)
            : new Native.Request("List", source.Actor, source.Tenant, TargetTenantId: source.Tenant, Limit: 1);
        var page = await StageAsync(request, new());
        var rows = page["Data"]![auditPage ? "Audits" : "Sessions"]!.AsArray();
        await Assert.That(rows.Count).IsEqualTo(1);
        await Assert.That(rows[0]!["Id"]!.GetValue<string>())
            .IsEqualTo((auditPage ? stopped.AuditId : second.SessionId)!.Value.ToString("D"));
    }

    [Test]
    public async Task RoleMembershipCannotTurnTheRuntimeExecutorIntoTheDataOwner()
    {
        var source = await SeedAsync();
        await using var caller = Open(PrimaryDatabaseRole.Runtime, source.Tenant);
        await caller.Database.OpenConnectionAsync();
        await Native.RequireReadyAsync(caller, fixture.NativeQueueEnrollment);
        await using var owner = await caller.Database.BeginTransactionAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            caller.Database.ExecuteSqlRawAsync($"SET ROLE {Native.OwnerRole}"));
        await Assert.That(error!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        await owner.RollbackAsync();
    }

    [Test]
    public async Task BroadenedSourceRoleGraphCannotBeAcceptedAsDataPermission()
    {
        await SeedAsync();
        await using var setup = fixture.CreateDbContext();
        await using var transaction = await setup.Database.BeginTransactionAsync();
        await setup.Database.ExecuteSqlRawAsync(
            $"GRANT {PostgresHumanSupportAccessSourceContract.RuntimeRole} TO {Native.RuntimeRole} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE");
        await Assert.ThrowsAsync<PostgresException>(() => Native.RequireReadyAsync(setup, fixture.NativeQueueEnrollment));
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task ProtectedAuditPreservesStoredJsonMetadataAsDomainText()
    {
        var source = await SeedAsync();
        var accepted = Mutation();
        await StageAsync(Start(source), accepted);
        Guid auditId;
        await using (var setup = fixture.CreateDbContext())
        {
            var audit = SupportAccessAuditEvent.Create(accepted.SessionId!.Value,
                SupportAccessAuditEventTypeEnum.RequestObserved, source.Actor, source.Tenant, "observed",
                Instant(), sanitizedMetadataJson: """{"allowed":true}""");
            auditId = audit.Id;
            setup.Add(audit);
            await setup.SaveChangesAsync();
        }
        var page = await StageAsync(new("Audit", source.Actor, source.Tenant, accepted.SessionId,
            TargetTenantId: source.Tenant), new());
        var row = page["Data"]!["Audits"]!.AsArray().Single(item => item!["Id"]!.GetValue<string>() == auditId.ToString("D"))!;
        using var metadata = JsonDocument.Parse(row["SanitizedMetadataJson"]!.GetValue<string>());
        await Assert.That(metadata.RootElement.GetProperty("allowed").GetBoolean()).IsTrue();
    }

    [Test]
    public async Task NativeDomainHydrationNeverEnlistsTrackedWritesOnTheOriginalContext()
    {
        var source = await SeedAsync();
        var accepted = Mutation();
        await StageAsync(Start(source), accepted);
        await using var caller = Open(PrimaryDatabaseRole.Runtime, source.Tenant);
        await caller.Database.OpenConnectionAsync();
        string image = await Native.ReadAsync(caller, fixture.NativeQueueEnrollment,
            new("List", source.Actor, source.Tenant, TargetTenantId: source.Tenant), default);
        using var document = JsonDocument.Parse(image);
        var row = document.RootElement.GetProperty("Data").GetProperty("Sessions")[0];
        var first = SupportAdministrationPostgresSource.Materialize<SupportAccessSession>(caller, row);
        var second = SupportAdministrationPostgresSource.Materialize<SupportAccessSession>(caller, row);
        await Assert.That(first.Id).IsEqualTo(accepted.SessionId!.Value);
        await Assert.That(second.CreatedBy).IsEqualTo(source.Actor);
        await Assert.That(first.IsActiveAt(accepted.TransitionAt!.Value)).IsTrue();
        await Assert.That(caller.ChangeTracker.Entries().Any()).IsFalse();
        await Assert.That(caller.ChangeTracker.HasChanges()).IsFalse();
    }

    private static async Task<JsonNode> ReadSupport(Explore.Persistence.ExploreDbContext context, Seeded source, Guid session)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText =
            $"SELECT {PostgresHumanSupportAccessSourceContract.AcquireFunctionSql(context)}(@actor,1,@tenant,@session,@tenant)::text";
        command.Parameters.Add(new NpgsqlParameter("actor", source.Actor));
        command.Parameters.Add(new NpgsqlParameter("tenant", source.Tenant));
        command.Parameters.Add(new NpgsqlParameter("session", session));
        return JsonNode.Parse((string)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("Original support source facts missing.")))!;
    }
}
