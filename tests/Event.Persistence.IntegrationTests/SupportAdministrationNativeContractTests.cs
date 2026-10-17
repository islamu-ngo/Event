using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using Native = Explore.Persistence.Security.PostgresSupportAdministrationContract;

namespace Event.Persistence.IntegrationTests;

/// <summary>
/// Genuine restricted PostgreSQL DATA controls. These do not substitute for genuine
/// authenticated handler acceptance. Parent installation and all execution are deferred.
/// </summary>
[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed partial class SupportAdministrationNativeContractTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task RuntimeStartsAndOwnStopsWithExactLifecycleEvidence()
    {
        var source = await SeedAsync();
        var accepted = Mutation();
        var started = await StageAsync(Start(source), accepted);
        var session = started["Data"]!["Sessions"]![0]!;
        await Assert.That(session["StatusId"]!.GetValue<int>()).IsEqualTo((int)SupportAccessSessionStatusEnum.Active);
        await Assert.That(session["CreatedBy"]!.GetValue<string>()).IsEqualTo(source.Actor.ToString("D"));
        await Assert.That(session["ApprovedByUserId"]).IsNull();
        var stop = new Native.Request("OwnStop", source.Actor, source.Tenant, accepted.SessionId,
            EndReasonText: "Finished");
        var stopped = await StageAsync(stop, Mutation() with { SessionId = accepted.SessionId });
        var terminal = stopped["Data"]!["Sessions"]![0]!;
        await Assert.That(terminal["StatusId"]!.GetValue<int>()).IsEqualTo((int)SupportAccessSessionStatusEnum.Stopped);
        await Assert.That(terminal["EndReasonId"]!.GetValue<int>()).IsEqualTo((int)SupportAccessEndReasonEnum.UserStopped);
        foreach (string property in new[] { "ActorUserId", "TargetTenantId", "StartedAtUtc", "ExpiresAtUtc",
                     "ReasonCode", "ReasonText", "TicketReference", "CreatedAt", "CreatedBy" })
            await Assert.That(JsonNode.DeepEquals(session[property], terminal[property])).IsTrue();
        var audit = stopped["Data"]!["Audits"]![0]!;
        await Assert.That(audit["Outcome"]!.GetValue<string>()).IsEqualTo("stopped");
        await Assert.That(audit["ActorUserId"]!.GetValue<string>()).IsEqualTo(source.Actor.ToString("D"));
        await Assert.That(audit["SanitizedMetadataJson"]).IsNull();
        await Assert.That(audit["TraceId"]).IsNull();
    }

    [Test]
    public async Task ForceStopAttributesAuditToOperatorWithoutChangingOriginalActor()
    {
        var source = await SeedAsync();
        var accepted = Mutation();
        await StageAsync(Start(source), accepted);
        var other = await SeedAsync();
        var revoked = await StageAsync(new("ForceStop", other.Actor, source.Tenant, accepted.SessionId,
            EndReasonText: "Operator termination"), Mutation() with { SessionId = accepted.SessionId });
        await Assert.That(revoked["Data"]!["Sessions"]![0]!["ActorUserId"]!.GetValue<string>())
            .IsEqualTo(source.Actor.ToString("D"));
        await Assert.That(revoked["Data"]!["Audits"]![0]!["ActorUserId"]!.GetValue<string>())
            .IsEqualTo(other.Actor.ToString("D"));
        await Assert.That(revoked["Data"]!["Sessions"]![0]!["StatusId"]!.GetValue<int>())
            .IsEqualTo((int)SupportAccessSessionStatusEnum.Revoked);
    }

    [Test]
    public async Task OwnStopCannotBorrowAnotherActorsSession()
    {
        var source = await SeedAsync();
        var accepted = Mutation();
        await StageAsync(Start(source), accepted);
        var other = await SeedAsync();
        await Assert.ThrowsAsync<PostgresException>(() => StageAsync(
            new("OwnStop", other.Actor, source.Tenant, accepted.SessionId),
            Mutation() with { SessionId = accepted.SessionId }));
    }

    [Test]
    public async Task AuditDisclosureRequiresTheOriginalTargetTenantQuestion()
    {
        var source = await SeedAsync();
        var accepted = Mutation();
        await StageAsync(Start(source), accepted);
        var wrong = await StageAsync(new("Audit", source.Actor, source.Tenant, accepted.SessionId,
            TargetTenantId: Guid.CreateVersion7()), new());
        await Assert.That(wrong["Data"]!["Audits"]!.AsArray().Count).IsEqualTo(0);
        var right = await StageAsync(new("Audit", source.Actor, source.Tenant, accepted.SessionId,
            TargetTenantId: source.Tenant), new());
        await Assert.That(right["Data"]!["Audits"]!.AsArray().Count).IsEqualTo(1);
        var listed = await StageAsync(new("List", source.Actor, source.Tenant,
            TargetTenantId: source.Tenant, Limit: 1), new());
        await Assert.That(listed["Data"]!["Sessions"]!.AsArray().Count).IsEqualTo(1);
    }

    private async Task<Seeded> SeedAsync()
    {
        await using var setup = fixture.CreateDbContext();
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Support native",
            Slug = $"support-native-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var actor = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii { Email = string.Empty, FirstName = "Support", LastName = "Native" }
        };
        setup.AddRange(tenant, actor);
        await setup.SaveChangesAsync();
        return new(actor.Id, tenant.Id);
    }

    private static Native.Request Start(Seeded source) => new("Start", source.Actor, source.Tenant,
        TargetTenantId: source.Tenant, ModeId: (int)SupportAccessModeEnum.ReadOnly,
        DurationMinutes: 10, ReasonCode: "support", ReasonText: "Native lifecycle", TicketReference: "control");

    private static Native.Accepted Mutation() => new(Instant(), Guid.CreateVersion7(),
        Guid.CreateVersion7(), Guid.CreateVersion7());

    private static DateTimeOffset Instant()
    {
        var now = DateTimeOffset.UtcNow;
        return new(now.Ticks - now.Ticks % 10, TimeSpan.Zero);
    }

    private ExploreDbContext Open(PrimaryDatabaseRole role, Guid tenant)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(role));
        options.AddInterceptors(PostgresTenantSessionInterceptor.Instance);
        var context = new ExploreDbContext(options.Options) { TenantContext = new TenantIdentity(tenant) };
        context.BindTenantLease();
        return context;
    }

    private async Task<JsonNode> StageAsync(Native.Request request, Native.Accepted accepted)
    {
        await using var caller = Open(PrimaryDatabaseRole.Runtime, request.TenantId);
        await caller.Database.OpenConnectionAsync();
        string before = await Native.ReadAsync(caller, fixture.NativeQueueEnrollment, request, CancellationToken.None);
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Native.AcquireAsync(caller, fixture.NativeQueueEnrollment, request, before, CancellationToken.None);
        await Native.CompileAsync(caller, fixture.NativeQueueEnrollment, request, accepted, CancellationToken.None);
        string applied = await Native.ApplyPlanAsync(caller, fixture.NativeQueueEnrollment, request, CancellationToken.None);
        await Native.CompleteAsync(caller, fixture.NativeQueueEnrollment, request, applied, CancellationToken.None);
        await owner.CommitAsync();
        return JsonNode.Parse(applied)!;
    }

    private static async Task<string> RawAsync(ExploreDbContext caller, string function,
        Native.Request request, string? argument = null)
    {
        await using var command = caller.Database.GetDbConnection().CreateCommand();
        command.Transaction = caller.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"SELECT {Native.FunctionSql(caller, function)}(@request{(argument is null ? "" : ",@argument")})::text";
        command.Parameters.Add(new NpgsqlParameter("request", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(request) });
        if (argument is not null)
            command.Parameters.Add(new NpgsqlParameter("argument", NpgsqlDbType.Jsonb) { Value = argument });
        return (string)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Native support image missing."));
    }

    private sealed record Seeded(Guid Actor, Guid Tenant);
    private sealed record TenantIdentity(Guid TenantId) : ITenantContext;
}
