using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Domain.Enums;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Native = Explore.Persistence.Security.PostgresSupportAdministrationContract;

namespace Event.Persistence.IntegrationTests;

public sealed partial class SupportAdministrationNativeContractTests
{
    [Test]
    [Arguments(PrimaryDatabaseRole.Global)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyInstanceMutation)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyTenantMutation)]
    [Arguments(PrimaryDatabaseRole.Migrator)]
    public async Task OtherLoginsCannotBorrowTheRuntimeDataCapability(PrimaryDatabaseRole role)
    {
        var source = await SeedAsync();
        await using var caller = Open(role, source.Tenant);
        await caller.Database.OpenConnectionAsync();
        await Assert.ThrowsAsync<PostgresException>(() => RawAsync(caller, "read", Start(source)));
    }

    [Test]
    [Arguments("tenant")]
    [Arguments("actor")]
    [Arguments("operation")]
    [Arguments("target")]
    [Arguments("reason")]
    public async Task AcquiredQuestionCannotBeReplacedBeforeCompilation(string component)
    {
        var source = await SeedAsync();
        var request = Start(source);
        await using var caller = Open(PrimaryDatabaseRole.Runtime, source.Tenant);
        await caller.Database.OpenConnectionAsync();
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        string before = await Native.ReadAsync(caller, fixture.NativeQueueEnrollment, request, default);
        await Native.AcquireAsync(caller, fixture.NativeQueueEnrollment, request, before, default);
        var replacement = component switch
        {
            "tenant" => request with { TenantId = Guid.CreateVersion7() },
            "actor" => request with { ActorId = Guid.CreateVersion7() },
            "operation" => new Native.Request("List", source.Actor, source.Tenant, TargetTenantId: source.Tenant),
            "target" => request with { TargetTenantId = Guid.CreateVersion7() },
            _ => request with { ReasonText = "Replacement purpose" }
        };
        await Assert.ThrowsAsync<PostgresException>(() =>
            Native.CompileAsync(caller, fixture.NativeQueueEnrollment, replacement, Mutation(), default));
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments("ReasonText")]
    [Arguments("ActorUserId")]
    [Arguments("TargetTenantId")]
    [Arguments("ExpiresAtUtc")]
    [Arguments("ApprovedByUserId")]
    [Arguments("SanitizedMetadataJson")]
    public async Task CompilerRejectsAllCallerSuppliedRowPatchFields(string property)
    {
        var source = await SeedAsync();
        var request = Start(source);
        await using var caller = Open(PrimaryDatabaseRole.Runtime, source.Tenant);
        await caller.Database.OpenConnectionAsync();
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        string before = await Native.ReadAsync(caller, fixture.NativeQueueEnrollment, request, default);
        await Native.AcquireAsync(caller, fixture.NativeQueueEnrollment, request, before, default);
        var payload = JsonSerializer.SerializeToNode(Mutation())!.AsObject();
        payload[property] = "caller selected replacement";
        await Assert.ThrowsAsync<PostgresException>(() => RawAsync(caller, "compile", request, payload.ToJsonString()));
        await owner.RollbackAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MissingPrivateCompletionRollsBackThePhysicalSessionAndAudit(bool apply)
    {
        var source = await SeedAsync();
        var request = Start(source);
        var accepted = Mutation();
        await using var caller = Open(PrimaryDatabaseRole.Runtime, source.Tenant);
        await caller.Database.OpenConnectionAsync();
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        string before = await Native.ReadAsync(caller, fixture.NativeQueueEnrollment, request, default);
        await Native.AcquireAsync(caller, fixture.NativeQueueEnrollment, request, before, default);
        await Native.CompileAsync(caller, fixture.NativeQueueEnrollment, request, accepted, default);
        if (apply) await Native.ApplyPlanAsync(caller, fixture.NativeQueueEnrollment, request, default);
        var error = await Assert.ThrowsAsync<PostgresException>(() => owner.CommitAsync());
        await Assert.That(error!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        await using var observer = fixture.CreateDbContext();
        await Assert.That(await observer.SupportAccessSessions.AnyAsync(row => row.Id == accepted.SessionId)).IsFalse();
        await Assert.That(await observer.SupportAccessAuditEvents.AnyAsync(row => row.Id == accepted.AuditId)).IsFalse();
    }

    [Test]
    public async Task CompletingAnUndisclosedOrAlteredFullImageFailsClosed()
    {
        var source = await SeedAsync();
        var request = Start(source);
        await using var caller = Open(PrimaryDatabaseRole.Runtime, source.Tenant);
        await caller.Database.OpenConnectionAsync();
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        string before = await Native.ReadAsync(caller, fixture.NativeQueueEnrollment, request, default);
        await Native.AcquireAsync(caller, fixture.NativeQueueEnrollment, request, before, default);
        await Native.CompileAsync(caller, fixture.NativeQueueEnrollment, request, Mutation(), default);
        var image = JsonNode.Parse(await Native.ApplyPlanAsync(caller, fixture.NativeQueueEnrollment, request, default))!;
        image["Data"]!["Sessions"]![0]!["ReasonText"] = "Unaccepted disclosure";
        await Assert.ThrowsAsync<PostgresException>(() =>
            Native.CompleteAsync(caller, fixture.NativeQueueEnrollment, request, image.ToJsonString(), default));
        await owner.RollbackAsync();
    }

    [Test]
    [Arguments("compile")]
    [Arguments("apply")]
    [Arguments("acquire")]
    public async Task PhysicalAttemptAndCompiledPlanCannotReplay(string action)
    {
        var source = await SeedAsync();
        var request = new Native.Request("List", source.Actor, source.Tenant, TargetTenantId: source.Tenant);
        await using var caller = Open(PrimaryDatabaseRole.Runtime, source.Tenant);
        await caller.Database.OpenConnectionAsync();
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        string before = await Native.ReadAsync(caller, fixture.NativeQueueEnrollment, request, default);
        await Native.AcquireAsync(caller, fixture.NativeQueueEnrollment, request, before, default);
        await Native.CompileAsync(caller, fixture.NativeQueueEnrollment, request, new(), default);
        if (action != "compile")
        {
            string applied = await Native.ApplyPlanAsync(caller, fixture.NativeQueueEnrollment, request, default);
            if (action == "acquire")
                await Native.CompleteAsync(caller, fixture.NativeQueueEnrollment, request, applied, default);
        }
        await Assert.ThrowsAsync<PostgresException>(() => action switch
        {
            "compile" => Native.CompileAsync(caller, fixture.NativeQueueEnrollment, request, new(), default),
            "apply" => Native.ApplyPlanAsync(caller, fixture.NativeQueueEnrollment, request, default),
            _ => Native.AcquireAsync(caller, fixture.NativeQueueEnrollment, request, before, default)
        });
        await owner.RollbackAsync();
    }

    [Test]
    [Arguments("List")]
    [Arguments("Audit")]
    public async Task ReadOnlyQuestionsCannotCompileLifecycleEffects(string operation)
    {
        var source = await SeedAsync();
        var accepted = Mutation();
        await StageAsync(Start(source), accepted);
        var request = new Native.Request(operation, source.Actor, source.Tenant,
            operation == "Audit" ? accepted.SessionId : null, TargetTenantId: source.Tenant);
        await Assert.ThrowsAsync<PostgresException>(() => StageAsync(request, Mutation()));
    }

    [Test]
    public async Task ExpiredOwnStopUsesTheOriginalExpiryAndForceStopRejectsExpiry()
    {
        var source = await SeedAsync();
        var accepted = Mutation() with { TransitionAt = Instant().AddMinutes(-20) };
        await StageAsync(Start(source), accepted);
        var force = new Native.Request("ForceStop", source.Actor, source.Tenant, accepted.SessionId);
        await Assert.ThrowsAsync<PostgresException>(() => StageAsync(force, Mutation() with { SessionId = accepted.SessionId }));
        var expired = await StageAsync(force with { Operation = "OwnStop" }, Mutation() with { SessionId = accepted.SessionId });
        await Assert.That(expired["Data"]!["Sessions"]![0]!["StatusId"]!.GetValue<int>())
            .IsEqualTo((int)SupportAccessSessionStatusEnum.Expired);
        await Assert.That(expired["Data"]!["Audits"]![0]!["Outcome"]!.GetValue<string>()).IsEqualTo("expired");
        var unchanged = await StageAsync(force with { Operation = "OwnStop" }, new());
        await Assert.That(unchanged["Data"]!["Audits"]!.AsArray().Count).IsEqualTo(0);
    }

    [Test]
    public async Task ForwardedReadUsesExactActorTenantAndDomainLifetimeWithoutMutationAuthority()
    {
        var source = await SeedAsync();
        var accepted = Mutation();
        await StageAsync(Start(source), accepted);
        await using var caller = Open(PrimaryDatabaseRole.Runtime, source.Tenant);
        await caller.Database.OpenConnectionAsync();
        var request = new Native.Request("Forwarded", source.Actor, source.Tenant, accepted.SessionId,
            TargetTenantId: source.Tenant);
        var image = JsonNode.Parse(await Native.ReadAsync(caller, fixture.NativeQueueEnrollment, request, default))!;
        await Assert.That(image["Data"]!["Sessions"]!.AsArray().Count).IsEqualTo(1);
        var wrongActor = JsonNode.Parse(await Native.ReadAsync(caller, fixture.NativeQueueEnrollment,
            request with { ActorId = Guid.CreateVersion7() }, default))!;
        await Assert.That(wrongActor["Data"]!["Sessions"]!.AsArray().Count).IsEqualTo(0);
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.ThrowsAsync<PostgresException>(() =>
            Native.AcquireAsync(caller, fixture.NativeQueueEnrollment, request, image.ToJsonString(), default));
        await owner.RollbackAsync();
    }
}
