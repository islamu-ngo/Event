using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Services.Webhooks;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Webhooks;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Repositories;

/// <summary>
/// Phase-three incoming authority packet. The shared fixture migrates PostgreSQL;
/// only administrator controls use its unrestricted connection. Worker acquisition
/// uses generated, non-elevated Global credentials and the production pool.
/// </summary>
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class IncomingWebhookNativeAuthorityTests(PostgreSqlContainerFixture fixture)
{
    private const string GlobalRole = PostgresIncomingWebhookQueueContract.GlobalRole;
    private static readonly DateTime Now = new(2026, 10, 8, 10, 0, 0, DateTimeKind.Utc);

    [Test]
    [Arguments("routine-grantee")]
    [Arguments("overload")]
    [Arguments("public-column")]
    [Arguments("schema-create")]
    [Arguments("outsider-membership")]
    [Arguments("membership-options")]
    [Arguments("owner-inherit")]
    public async Task IncomingPrivilegeDriftRejectsEveryRoutineAndReinstallation(string drift)
    {
        await fixture.ResetAsync();
        await using var authority = await CreateAuthorityAsync();
        await using var outsider = await CreateAuthorityAsync(entitled: false);
        await using var global = await authority.Factory.CreateDbContextAsync();
        await using var administrator = fixture.CreateDbContext();
        var entity = administrator.Model.FindEntityType(typeof(IncomingWebhookMessage))!;
        var store = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(
            entity.GetTableName()!, entity.GetSchema());
        string table = $"\"{entity.GetSchema()}\".\"{entity.GetTableName()}\"";
        string column = entity.FindProperty(nameof(IncomingWebhookMessage.PayloadHash))!.GetColumnName(store)!;
        string claim = PostgresIncomingWebhookQueueContract.ClaimFunctionSql(administrator, false);
        string signature = $"{claim}(text, integer, timestamp with time zone, interval)";
        string owner = PostgresIncomingWebhookQueueContract.OwnerRole;
        string login = fixture.NativeQueueEnrollment.GlobalLogin;
        (string corrupt, string restore) = drift switch
        {
            "routine-grantee" => ($"GRANT EXECUTE ON FUNCTION {signature} TO \"{outsider.Role}\"",
                $"REVOKE EXECUTE ON FUNCTION {signature} FROM \"{outsider.Role}\""),
            "overload" => ($"CREATE FUNCTION {claim}(integer) RETURNS integer LANGUAGE sql AS 'SELECT 1'",
                $"DROP FUNCTION {claim}(integer)"),
            "public-column" => ($"GRANT SELECT (\"{column}\") ON {table} TO PUBLIC",
                $"REVOKE SELECT (\"{column}\") ON {table} FROM PUBLIC"),
            "schema-create" => ($"GRANT CREATE ON SCHEMA \"{entity.GetSchema()}\" TO {owner}",
                $"REVOKE CREATE ON SCHEMA \"{entity.GetSchema()}\" FROM {owner}"),
            "outsider-membership" => ($"GRANT {GlobalRole} TO \"{outsider.Role}\" WITH INHERIT TRUE, SET FALSE, ADMIN FALSE",
                $"REVOKE {GlobalRole} FROM \"{outsider.Role}\""),
            "membership-options" => ($"GRANT {GlobalRole} TO \"{login}\" WITH INHERIT TRUE, SET TRUE, ADMIN FALSE",
                $"GRANT {GlobalRole} TO \"{login}\" WITH INHERIT TRUE, SET FALSE, ADMIN FALSE"),
            "owner-inherit" => ($"ALTER ROLE {owner} INHERIT", $"ALTER ROLE {owner} NOINHERIT"),
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        await Assert.That((await ClaimAsync(authority.Factory, false, Now, 1)).Count).IsEqualTo(0);
        await administrator.Database.ExecuteSqlRawAsync(corrupt);
        try
        {
            foreach (bool effects in new[] { false, true })
            {
                await Assert.That(async () => await global.Database.ExecuteSqlRawAsync(
                    $"SELECT * FROM {PostgresIncomingWebhookQueueContract.ClaimFunctionSql(global, effects)}(NULL, NULL, NULL, NULL)"))
                    .Throws<PostgresException>()
                    .And.Satisfies(exception => exception != null && exception.SqlState == "55000");
                await Assert.That(async () => await global.Database.ExecuteSqlRawAsync(
                    $"SELECT {PostgresIncomingWebhookQueueContract.RenewFunctionSql(global, effects)}(NULL, NULL, NULL, NULL, NULL, NULL, NULL)"))
                    .Throws<PostgresException>()
                    .And.Satisfies(exception => exception != null && exception.SqlState == "55000");
            }
            await Assert.That(async () => await PostgresIncomingWebhookQueueContract.ApplyAsync(
                administrator, fixture.NativeQueueEnrollment))
                .Throws<PostgresException>()
                .And.Satisfies(exception => exception != null && exception.SqlState == "55000");
        }
        finally
        {
            await administrator.Database.ExecuteSqlRawAsync(restore);
        }
        await Assert.That((await ClaimAsync(authority.Factory, false, Now, 1)).Count).IsEqualTo(0);
    }

    [Test]
    public async Task IncomingInheritedGlobalLoginCannotImpersonateEnrolledSession()
    {
        await fixture.ResetAsync();
        await using var authority = await CreateAuthorityAsync();
        await using var outsider = await CreateAuthorityAsync(entitled: false);
        await using var administrator = fixture.CreateDbContext();
        await administrator.Database.ExecuteSqlRawAsync(
            $"GRANT \"{fixture.NativeQueueEnrollment.GlobalLogin}\" TO \"{outsider.Role}\" WITH INHERIT TRUE, SET TRUE, ADMIN FALSE");
        try
        {
            await Assert.That((await ClaimAsync(authority.Factory, false, Now, 1)).Count).IsEqualTo(0);
            await RequirePrivilegeDenialAsync(async () => await ClaimAsync(outsider.Factory, false, Now, 1));
            await using var connection = await outsider.Factory.CreateDbContextAsync();
            await connection.Database.OpenConnectionAsync();
            await connection.Database.ExecuteSqlRawAsync($"SET ROLE \"{fixture.NativeQueueEnrollment.GlobalLogin}\"");
            await RequirePrivilegeDenialAsync(async () => await connection.Database.ExecuteSqlRawAsync(
                $"SELECT * FROM {PostgresIncomingWebhookQueueContract.ClaimFunctionSql(connection, false)}('outsider', 1, now(), interval '1 minute')"));
        }
        finally
        {
            await administrator.Database.ExecuteSqlRawAsync(
                $"REVOKE \"{fixture.NativeQueueEnrollment.GlobalLogin}\" FROM \"{outsider.Role}\"");
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RestrictedGlobalDrainClaimsBothTenantsWithoutReadingPrivateEnvelope(bool effects)
    {
        await fixture.ResetAsync();
        var seeded = await SeedAsync();
        await using var authority = await CreateAuthorityAsync();
        await using var global = await authority.Factory.CreateDbContextAsync();
        await RequireRestrictedAsync(global);
        await RequirePrivateReadDenialsAsync(global);

        // A real state transition proves the rows are eligible before the worker
        // attempts the same operation using its independent restricted pool.
        await using (var control = fixture.CreateDbContext())
        {
            var claims = await ClaimAsync(control, effects, Now, 2);
            await Assert.That(claims.Select(claim => claim.TenantId))
                .IsEquivalentTo(new[] { seeded.TenantA, seeded.TenantB });
            await Assert.That(claims.All(claim => claim.Fence == 1 && claim.Generation == 1)).IsTrue();
        }

        await using var services = CreateServices(authority.Factory);
        var executor = new DiscoveryObserver();
        var settings = Options.Create(new IncomingWebhookProcessingSettings
        {
            BatchSize = 2,
            LeaseSeconds = 30,
            MaxConcurrentItems = 2
        });
        var time = new FixedTimeProvider(Now.AddSeconds(31));
        IncomingWebhookDrainResult result;
        if (effects)
        {
            using var metrics = new BusinessMetrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
            result = await new IncomingWebhookEffectDrainService(
                services.GetRequiredService<IServiceScopeFactory>(), executor, settings, time, metrics,
                NullLogger<IncomingWebhookEffectDrainService>.Instance).ProcessBatchAsync(CancellationToken.None);
        }
        else
        {
            result = await new IncomingWebhookDrainService(
                services.GetRequiredService<IServiceScopeFactory>(), executor, settings, time,
                NullLogger<IncomingWebhookDrainService>.Instance).ProcessBatchAsync(CancellationToken.None);
        }

        await Assert.That(result.ClaimedCount).IsEqualTo(2);
        await Assert.That(result.LeaseLostCount).IsEqualTo(2);
        await Assert.That(result.CompletedCount).IsEqualTo(0);
        await Assert.That(executor.Claims.Select(claim => claim.TenantId))
            .IsEquivalentTo(new[] { seeded.TenantA, seeded.TenantB });
        await Assert.That(executor.Claims.All(claim => claim.Fence == 2 && claim.Generation == 1)).IsTrue();
        await Assert.That(executor.Claims.Select(claim => claim.Id))
            .IsEquivalentTo(effects ? seeded.EffectIds : seeded.MessageIds);
        await VerifyEnvelopeAndReceiptsAsync(seeded);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RestrictedGlobalRenewalPreservesEveryExactClaimPredicate(bool effects)
    {
        await fixture.ResetAsync();
        var seeded = await SeedAsync();
        IReadOnlyList<ClaimIdentity> claims;
        await using (var control = fixture.CreateDbContext())
        {
            claims = await ClaimAsync(control, effects, Now, 2);
            await Assert.That(await RenewAsync(
                control, effects, claims[0], Now.AddSeconds(1), Now.AddSeconds(40))).IsTrue();
            await Assert.That(await RenewAsync(
                control, effects, claims[0] with { Fence = claims[0].Fence + 1 },
                Now.AddSeconds(2), Now.AddSeconds(50))).IsFalse();
        }

        await using var authority = await CreateAuthorityAsync();
        await using var global = await authority.Factory.CreateDbContextAsync();
        await RequireRestrictedAsync(global);
        await RequirePrivateReadDenialsAsync(global);
        var claim = claims.Single(value => value.TenantId == seeded.TenantA);
        var other = claims.Single(value => value.TenantId == seeded.TenantB);
        await Assert.That(await RenewAsync(authority.Factory, effects, claim, Now.AddSeconds(2), Now.AddSeconds(60))).IsTrue();
        foreach (var forged in new[]
        {
            claim with { TenantId = other.TenantId },
            claim with { Id = other.Id },
            claim with { Token = Guid.CreateVersion7() },
            claim with { Fence = claim.Fence + 1 },
            claim with { Generation = claim.Generation + 1 }
        })
        {
            await Assert.That(await RenewAsync(
                authority.Factory, effects, forged, Now.AddSeconds(3), Now.AddSeconds(70))).IsFalse();
        }
        await Assert.That(await RenewAsync(authority.Factory, effects, claim, Now.AddSeconds(3), Now.AddSeconds(60))).IsFalse();
        await Assert.That(await RenewAsync(authority.Factory, effects, claim, Now.AddSeconds(60), Now.AddSeconds(90))).IsFalse();
        await VerifyEnvelopeAndReceiptsAsync(seeded);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RestrictedGlobalRecoveryAdvancesFenceAndRejectsCancelledWorkerIdentity(bool effects)
    {
        await fixture.ResetAsync();
        var seeded = await SeedAsync();
        IReadOnlyList<ClaimIdentity> original;
        await using (var control = fixture.CreateDbContext())
        {
            original = await ClaimAsync(control, effects, Now, 2);
            await Assert.That(original.Count).IsEqualTo(2);
            await Assert.That((await ClaimAsync(control, effects, Now.AddSeconds(29), 2)).Count).IsEqualTo(0);
        }

        await using var authority = await CreateAuthorityAsync();
        await using var global = await authority.Factory.CreateDbContextAsync();
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            await Assert.That(async () =>
                await ClaimAsync(authority.Factory, effects, Now.AddSeconds(30), 1, cancellation.Token))
                .Throws<OperationCanceledException>();
        }
        var recovered = await ClaimAsync(authority.Factory, effects, Now.AddSeconds(30), 1);
        await Assert.That(recovered.Count).IsEqualTo(1);
        var current = recovered.Single();
        var old = original.Single(value => value.Id == current.Id);
        await Assert.That(current.TenantId).IsEqualTo(old.TenantId);
        await Assert.That(current.Fence).IsEqualTo(old.Fence + 1);
        await Assert.That(current.Generation).IsEqualTo(old.Generation);
        await Assert.That(current.Token).IsNotEqualTo(old.Token);
        await Assert.That(await RenewAsync(authority.Factory, effects, old, Now.AddSeconds(31), Now.AddSeconds(90))).IsFalse();
        await Assert.That(await RenewAsync(authority.Factory, effects, current, Now.AddSeconds(31), Now.AddSeconds(90))).IsTrue();
        await using (var verification = fixture.CreateDbContext())
        {
            if (!effects)
            {
                var outcomes = await verification.IncomingWebhookProcessingAttempts
                    .Where(attempt => attempt.IncomingWebhookMessageId == current.Id)
                    .OrderBy(attempt => attempt.RecordedAt).ThenBy(attempt => attempt.OutcomeId)
                    .Select(attempt => attempt.OutcomeId).ToListAsync();
                await Assert.That(outcomes).IsEquivalentTo(new[]
                {
                    (int)IncomingWebhookProcessingAttemptOutcome.Claimed,
                    (int)IncomingWebhookProcessingAttemptOutcome.LeaseExpired,
                    (int)IncomingWebhookProcessingAttemptOutcome.Claimed
                });
            }
        }
        await VerifyEnvelopeAndReceiptsAsync(seeded);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ConcurrentGlobalClaimsAcquireDistinctOwnershipWithoutDoubleAdvancingFence(bool effects)
    {
        await fixture.ResetAsync();
        var seeded = await SeedAsync();
        await using var authority = await CreateAuthorityAsync();
        var claims = await Task.WhenAll(
            ClaimAsync(authority.Factory, effects, Now, 1),
            ClaimAsync(authority.Factory, effects, Now, 1));
        await Assert.That(claims.SelectMany(batch => batch).Select(claim => claim.Id))
            .IsEquivalentTo(effects ? seeded.EffectIds : seeded.MessageIds);
        await Assert.That(claims.All(batch => batch.Count == 1)).IsTrue();
        await Assert.That(claims.SelectMany(batch => batch).All(claim => claim.Fence == 1)).IsTrue();
        await Assert.That((await ClaimAsync(authority.Factory, effects, Now, 2)).Count).IsEqualTo(0);
        await VerifyEnvelopeAndReceiptsAsync(seeded);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task UnentitledGlobalLoginCannotClaimOrRenewIncomingOwnership(bool effects)
    {
        await fixture.ResetAsync();
        await SeedAsync();
        IReadOnlyList<ClaimIdentity> claims;
        await using (var control = fixture.CreateDbContext())
            claims = await ClaimAsync(control, effects, Now, 2);
        await using var authority = await CreateAuthorityAsync(entitled: false);
        await RequirePrivilegeDenialAsync(async () =>
            await ClaimAsync(authority.Factory, effects, Now.AddSeconds(30), 1));
        await RequirePrivilegeDenialAsync(async () =>
            await RenewAsync(authority.Factory, effects, claims[0], Now.AddSeconds(1), Now.AddSeconds(60)));
    }

    [Test]
    public async Task GlobalAndFunctionOwnerCannotReadEnvelopeOrPromoteIntoMigrationAuthority()
    {
        await fixture.ResetAsync();
        await SeedAsync();
        await using var authority = await CreateAuthorityAsync();
        await using var global = await authority.Factory.CreateDbContextAsync();
        await RequireRestrictedAsync(global);
        await RequirePrivateReadDenialsAsync(global);
        foreach (string role in new[]
        {
            PostgresIncomingWebhookQueueContract.OwnerRole,
            PostgresIncomingWebhookQueueContract.MigratorRole,
            "postgres"
        })
        {
            await RequirePrivilegeDenialAsync(async () => await global.Database.ExecuteSqlRawAsync($"SET ROLE {role}"));
        }
        foreach (var fields in new[]
        {
            (Type: typeof(IncomingWebhookMessage), Private: nameof(IncomingWebhookMessage.PayloadHash)),
            (Type: typeof(IncomingWebhookEffectOutbox), Private: nameof(IncomingWebhookEffectOutbox.PayloadSha256))
        })
        {
            var entity = global.Model.FindEntityType(fields.Type)!;
            var mapping = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(
                entity.GetTableName()!, entity.GetSchema());
            string table = $"\"{entity.GetSchema()}\".\"{entity.GetTableName()}\"";
            string privateColumn = entity.FindProperty(fields.Private)!.GetColumnName(mapping)!;
            string id = entity.FindProperty("Id")!.GetColumnName(mapping)!;
            await using var command = global.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"""
                SELECT has_table_privilege('{PostgresIncomingWebhookQueueContract.OwnerRole}', '{table}', 'SELECT'),
                    has_column_privilege('{PostgresIncomingWebhookQueueContract.OwnerRole}', '{table}', '{id}', 'SELECT'),
                    has_column_privilege('{PostgresIncomingWebhookQueueContract.OwnerRole}', '{table}', '{privateColumn}', 'SELECT'),
                    has_column_privilege('{PostgresIncomingWebhookQueueContract.OwnerRole}', '{table}', '{privateColumn}', 'UPDATE')
                """;
            await using var reader = await command.ExecuteReaderAsync();
            await Assert.That(await reader.ReadAsync()).IsTrue();
            await Assert.That(reader.GetBoolean(0)).IsFalse();
            await Assert.That(reader.GetBoolean(1)).IsTrue();
            await Assert.That(reader.GetBoolean(2)).IsFalse();
            await Assert.That(reader.GetBoolean(3)).IsFalse();
        }
        foreach (bool effects in new[] { false, true })
        {
            string? state = null;
            try
            {
                await global.Database.ExecuteSqlRawAsync(
                    $"SELECT * FROM {PostgresIncomingWebhookQueueContract.ClaimFunctionSql(global, effects)}({{0}}, {{1}}, {{2}}, {{3}})",
                    "native-invalid", IIncomingWebhookQueueRepository.MaximumBatchSize + 1, Now, TimeSpan.FromSeconds(30));
            }
            catch (PostgresException exception) { state = exception.SqlState; }
            await Assert.That(state).IsEqualTo(PostgresErrorCodes.InvalidParameterValue);
        }
    }

    [Test]
    public async Task ClaimEvidenceFailureRollsBackBothLeaseMutationAndAttemptBeforeRecovery()
    {
        await fixture.ResetAsync();
        var seeded = await SeedAsync();
        await using var authority = await CreateAuthorityAsync();
        await using var administrator = fixture.CreateDbContext();
        var entity = administrator.Model.FindEntityType(typeof(IncomingWebhookProcessingAttempt))!;
        string schema = entity.GetSchema()!;
        string table = $"\"{schema}\".\"{entity.GetTableName()}\"";
        string function = $"\"{schema}\".\"incoming_native_claim_fault\"";
        await administrator.Database.ExecuteSqlRawAsync($"""
            CREATE FUNCTION {function}() RETURNS trigger LANGUAGE plpgsql AS $fault$
            BEGIN RAISE EXCEPTION 'incoming claim evidence fault' USING ERRCODE = '22000'; END $fault$;
            CREATE TRIGGER incoming_native_claim_fault BEFORE INSERT ON {table}
                FOR EACH ROW EXECUTE FUNCTION {function}();
            """);
        try
        {
            string? state = null;
            try { await ClaimAsync(authority.Factory, effects: false, Now, 2); }
            catch (PostgresException exception) { state = exception.SqlState; }
            await Assert.That(state).IsEqualTo("22000");
            var messages = await administrator.IncomingWebhookMessages.AsNoTracking()
                .Where(message => seeded.MessageIds.Contains(message.Id)).ToListAsync();
            await Assert.That(messages.All(message =>
                message.Status == IncomingWebhookMessageStatus.Verified && message.ProcessingFence == 0 &&
                message.ProcessingLeaseToken == null && message.AttemptCount == 0)).IsTrue();
            await Assert.That(await administrator.IncomingWebhookProcessingAttempts
                .CountAsync(attempt => seeded.MessageIds.Contains(attempt.IncomingWebhookMessageId))).IsEqualTo(0);
        }
        finally
        {
            await administrator.Database.ExecuteSqlRawAsync($"""
                DROP TRIGGER incoming_native_claim_fault ON {table};
                DROP FUNCTION {function}();
                """);
        }
        var recovered = await ClaimAsync(authority.Factory, effects: false, Now, 2);
        await Assert.That(recovered.Count).IsEqualTo(2);
        await Assert.That(recovered.All(claim => claim.Fence == 1 && claim.Generation == 1)).IsTrue();
        await VerifyEnvelopeAndReceiptsAsync(seeded);
    }

    private static async Task RequirePrivilegeDenialAsync(Func<Task> action)
    {
        string? state = null;
        try { await action(); }
        catch (PostgresException exception) { state = exception.SqlState; }
        await Assert.That(state).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
    }

    private static ServiceProvider CreateServices(GlobalExploreDbContextFactory factory)
    {
        var services = new ServiceCollection();
        services.AddMetrics();
        services.AddSingleton<IGlobalExploreDbContextFactory>(factory);
        services.AddScoped<IIncomingWebhookQueueRepository, IncomingWebhookQueueRepository>();
        return services.BuildServiceProvider();
    }

    private static async Task<IReadOnlyList<ClaimIdentity>> ClaimAsync(
        IGlobalExploreDbContextFactory factory, bool effects, DateTime now, int take,
        CancellationToken cancellationToken = default)
    {
        var repository = new IncomingWebhookQueueRepository(factory);
        if (effects)
        {
            var claims = await repository.ClaimEffectsAsync(
                new IncomingWebhookEffectClaimRequest("native-incoming", take, now, TimeSpan.FromSeconds(30)),
                cancellationToken);
            return claims.Select(claim => new ClaimIdentity(
                claim.EffectOutboxId, claim.TenantId, claim.LeaseToken, claim.ProcessingFence, claim.ProcessingGeneration))
                .ToArray();
        }
        var messages = await repository.ClaimMessagesAsync(
            new IncomingWebhookClaimRequest("native-incoming", take, now, TimeSpan.FromSeconds(30)), cancellationToken);
        return messages.Select(claim => new ClaimIdentity(
            claim.IncomingWebhookMessageId, claim.TenantId, claim.LeaseToken, claim.ProcessingFence, claim.ProcessingGeneration))
            .ToArray();
    }

    private static Task<bool> RenewAsync(
        IGlobalExploreDbContextFactory factory, bool effects, ClaimIdentity claim, DateTime observed, DateTime expires) =>
        effects
            ? new IncomingWebhookQueueRepository(factory).TryRenewEffectAsync(
                new IncomingWebhookEffectClaim(claim.Id, claim.TenantId, claim.Token, claim.Fence, claim.Generation),
                observed, expires, CancellationToken.None)
            : new IncomingWebhookQueueRepository(factory).TryRenewMessageAsync(
                new IncomingWebhookClaim(claim.Id, claim.TenantId, claim.Token, claim.Fence, claim.Generation),
                observed, expires, CancellationToken.None);

    private static async Task<IReadOnlyList<ClaimIdentity>> ClaimAsync(
        ExploreDbContext context, bool effects, DateTime now, int take,
        CancellationToken cancellationToken = default)
    {
        if (effects)
        {
            var claims = await new IncomingWebhookEffectOutboxRepository(context).ClaimDueAsync(
                new IncomingWebhookEffectClaimRequest("native-incoming", take, now, TimeSpan.FromSeconds(30)),
                cancellationToken);
            return claims.Select(claim => new ClaimIdentity(
                claim.EffectOutboxId, claim.TenantId, claim.LeaseToken, claim.ProcessingFence, claim.ProcessingGeneration))
                .ToArray();
        }
        var messages = await new IncomingWebhookMessageRepository(context).ClaimDueAsync(
            new IncomingWebhookClaimRequest("native-incoming", take, now, TimeSpan.FromSeconds(30)),
            cancellationToken);
        return messages.Select(claim => new ClaimIdentity(
            claim.IncomingWebhookMessageId, claim.TenantId, claim.LeaseToken, claim.ProcessingFence, claim.ProcessingGeneration))
            .ToArray();
    }

    private static Task<bool> RenewAsync(
        ExploreDbContext context, bool effects, ClaimIdentity claim, DateTime observed, DateTime expires) =>
        effects
            ? new IncomingWebhookEffectOutboxRepository(context).TryRenewClaimAsync(
                new IncomingWebhookEffectClaim(claim.Id, claim.TenantId, claim.Token, claim.Fence, claim.Generation),
                observed, expires, CancellationToken.None)
            : new IncomingWebhookMessageRepository(context).TryRenewClaimAsync(
                claim.TenantId, claim.Id, claim.Token, claim.Fence, claim.Generation,
                observed, expires, CancellationToken.None);

    private async Task<Seeded> SeedAsync()
    {
        await using var context = fixture.CreateDbContext();
        var tenantA = Tenant("native-incoming-a");
        var tenantB = Tenant("native-incoming-b");
        context.AddRange(tenantA, tenantB);
        var messages = new[] { Message(tenantA.Id), Message(tenantB.Id) };
        var effects = messages.Select(message => IncomingWebhookEffectOutbox.CreatePending(
            message.TenantId, message.Id, message.Provider, message.ProviderMessageId,
            "native.incoming.effect", message.PayloadHash, Now)).ToArray();
        context.AddRange(messages);
        context.AddRange(effects);
        // Future, terminal, and payload-conflict rows must never be mistaken for due work.
        foreach (Guid tenant in new[] { tenantA.Id, tenantB.Id })
        {
            var future = Message(tenant);
            context.Add(future);
            context.Entry(future).Property(row => row.NextAttemptAt).CurrentValue = Now.AddHours(1);
            var terminal = Message(tenant);
            terminal.Claim("terminal-control", Guid.CreateVersion7(), Now.AddMinutes(1), Now);
            terminal.Ignore(terminal.ProcessingLeaseToken!.Value, terminal.ProcessingFence,
                terminal.ProcessingGeneration, "native_terminal", "Terminal control.", Now);
            terminal.ClearPayload(Now.AddDays(7));
            context.Add(terminal);
            var conflict = Message(tenant);
            conflict.ClassifyDuplicate("sha256:" + new string('b', 64), Now);
            context.Add(conflict);
            var futureEffect = IncomingWebhookEffectOutbox.CreatePending(
                tenant, future.Id, future.Provider, future.ProviderMessageId,
                "native.incoming.effect", future.PayloadHash, Now);
            context.Add(futureEffect);
            context.Entry(futureEffect).Property(row => row.NextAttemptAt).CurrentValue = Now.AddHours(1);
            var terminalEffect = IncomingWebhookEffectOutbox.CreatePending(
                tenant, terminal.Id, terminal.Provider, terminal.ProviderMessageId,
                "native.incoming.effect", terminal.PayloadHash, Now);
            terminalEffect.AcknowledgeResolution("native_terminal", Now);
            context.Add(terminalEffect);
        }
        await context.SaveChangesAsync();
        return new Seeded(tenantA.Id, tenantB.Id,
            messages.Select(message => message.Id).ToArray(), effects.Select(effect => effect.Id).ToArray(),
            messages.Select(message => message.PayloadHash).ToArray());
    }

    private async Task VerifyEnvelopeAndReceiptsAsync(Seeded seeded)
    {
        await using var verification = fixture.CreateDbContext();
        var messages = await verification.IncomingWebhookMessages.AsNoTracking()
            .Where(message => seeded.MessageIds.Contains(message.Id)).OrderBy(message => message.Id).ToListAsync();
        await Assert.That(messages.Count).IsEqualTo(2);
        await Assert.That(messages.Select(message => message.PayloadHash)).IsEquivalentTo(seeded.Hashes);
        await Assert.That(messages.All(message => message.PayloadBytes.Span.SequenceEqual(Encoding.UTF8.GetBytes("{}"))))
            .IsTrue();
        await Assert.That(await verification.IncomingWebhookEffectReceipts.CountAsync()).IsEqualTo(0);
    }

    private static Tenant Tenant(string prefix) => new()
    {
        Id = Guid.CreateVersion7(),
        FullName = prefix,
        Slug = $"{prefix}-{Guid.CreateVersion7():N}",
        TenantStatusId = (int)TenantStatusEnum.Active,
        TenantStatus = null!,
        CreatedAt = Now
    };

    private static IncomingWebhookMessage Message(Guid tenant)
    {
        byte[] payload = Encoding.UTF8.GetBytes("{}");
        return IncomingWebhookMessage.CreateVerified(
            tenant, "native-incoming", Guid.CreateVersion7().ToString("N"), null, "native.effect",
            payload, "sha256:" + Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
            "application/json", "utf-8", null, Now, Now, Now.AddDays(7), "native-incoming-v1",
            Now.AddDays(30), Now.AddDays(90), Now.AddDays(7), Now.AddDays(30));
    }

    private async Task<Authority> CreateAuthorityAsync(bool entitled = true)
    {
        await using var model = fixture.CreateDbContext();
        await PostgresIncomingWebhookQueueContract.ApplyAsync(model, fixture.NativeQueueEnrollment);
        await PostgresIncomingWebhookQueueContract.ApplyAsync(model, fixture.NativeQueueEnrollment);
        string schema = model.Model.FindEntityType(typeof(IncomingWebhookMessage))!.GetSchema()!;
        if (entitled)
            return new Authority(fixture.ConnectionString, schema, fixture.NativeQueueEnrollment.GlobalLogin,
                fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), fixtureOwned: true);
        string role = $"incoming_global_{Guid.CreateVersion7():N}";
        string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        await using var administrator = new NpgsqlConnection(fixture.ConnectionString);
        await administrator.OpenAsync();
        await using var command = administrator.CreateCommand();
        command.CommandText = $"""
            CREATE ROLE "{role}" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD '{password}';
            GRANT USAGE ON SCHEMA "{schema}" TO "{role}";
            """;
        await command.ExecuteNonQueryAsync();
        var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString);
        return new Authority(fixture.ConnectionString, schema, role, new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Global,
            Provider = PrimaryDatabaseProvider.PostgreSql,
            Host = connection.Host,
            Port = connection.Port,
            Database = connection.Database,
            Schema = schema,
            Username = role,
            Password = password,
            TlsMode = PrimaryDatabaseTlsMode.Disabled
        });
    }

    private static async Task RequireRestrictedAsync(ExploreDbContext context)
    {
        await Assert.That(context.TenantContext).IsNull();
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolreplication
            FROM pg_catalog.pg_roles WHERE rolname = current_user
            """;
        await Assert.That((bool)(await command.ExecuteScalarAsync())!).IsTrue();
    }

    private static async Task RequirePrivateReadDenialsAsync(ExploreDbContext context)
    {
        foreach (Type type in new[] { typeof(IncomingWebhookMessage), typeof(IncomingWebhookEffectOutbox),
            typeof(IncomingWebhookEffectReceipt), typeof(UserPii) })
        {
            var entity = context.Model.FindEntityType(type)!;
            string? state = null;
            try
            {
                await context.Database.ExecuteSqlRawAsync(
                    $"SELECT * FROM \"{entity.GetSchema()}\".\"{entity.GetTableName()}\"");
            }
            catch (PostgresException exception) { state = exception.SqlState; }
            await Assert.That(state).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        }
    }

    private sealed class Authority(
        string administratorConnection, string schema, string role, PrimaryDatabaseConnectionOptions options,
        bool fixtureOwned = false)
        : IAsyncDisposable
    {
        public string Role => role;
        public GlobalExploreDbContextFactory Factory { get; } = new(options, poolSize: 1);

        public async ValueTask DisposeAsync()
        {
            using var pool = new NpgsqlConnection(PrimaryDatabaseConfiguration.BuildConnectionString(options).ConnectionString);
            NpgsqlConnection.ClearPool(pool);
            if (fixtureOwned)
                return;
            await using var administrator = new NpgsqlConnection(administratorConnection);
            await administrator.OpenAsync();
            await using var command = administrator.CreateCommand();
            command.CommandText = $"""
                REVOKE USAGE ON SCHEMA "{schema}" FROM "{role}";
                DROP ROLE "{role}";
                """;
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed record ClaimIdentity(Guid Id, Guid TenantId, Guid Token, long Fence, int Generation);
    private sealed record Seeded(Guid TenantA, Guid TenantB, Guid[] MessageIds, Guid[] EffectIds, string[] Hashes);
    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
    }

    // This observer deliberately reports lease loss instead of pretending to
    // settle effects. Real tenant execution and atomic settlement remain covered
    // by IncomingWebhookCrashIsolationTests and IncomingWebhookAtomicEffectTests.
    private sealed class DiscoveryObserver : IIncomingWebhookClaimExecutor, IIncomingWebhookEffectClaimExecutor
    {
        public ConcurrentBag<ClaimIdentity> Claims { get; } = [];
        public Task<IncomingWebhookClaimExecutionResult> ExecuteAsync(IncomingWebhookClaim claim, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Claims.Add(new(claim.IncomingWebhookMessageId, claim.TenantId, claim.LeaseToken,
                claim.ProcessingFence, claim.ProcessingGeneration));
            return Task.FromResult(IncomingWebhookClaimExecutionResult.LeaseLost());
        }
        public Task<IncomingWebhookClaimExecutionResult> ExecuteAsync(IncomingWebhookEffectClaim claim, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Claims.Add(new(claim.EffectOutboxId, claim.TenantId, claim.LeaseToken,
                claim.ProcessingFence, claim.ProcessingGeneration));
            return Task.FromResult(IncomingWebhookClaimExecutionResult.LeaseLost());
        }
    }
}
