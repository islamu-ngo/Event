using System.Data;
using System.Text.Json;
using Cerbos.Api.V1.Effect;
using Cerbos.Sdk;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Features.Webhooks.Handlers.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Explore.Persistence.Security;
using Grpc.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Event.Api.IntegrationTests.Authentication;

/// <summary>
/// Actual endpoint handlers with validated Local credentials, persisted P/route/H/W and
/// the real Serializable owner. Only the external PDP transport and failure timing vary.
/// </summary>
[NotInParallel]
public sealed class HumanWebhookEndpointMutationTests
{
    [Test]
    [Arguments("create", "local", false)]
    [Arguments("update", "local", false)]
    [Arguments("rotate", "local", false)]
    [Arguments("create", "local", true)]
    [Arguments("update", "local", true)]
    [Arguments("rotate", "local", true)]
    [Arguments("create", "cerbos", false)]
    [Arguments("update", "cerbos", false)]
    [Arguments("rotate", "cerbos", false)]
    [Arguments("create", "cerbos", true)]
    [Arguments("update", "cerbos", true)]
    [Arguments("rotate", "cerbos", true)]
    public async Task OriginalSelectedDecisionControlsActualEndpointAndAudit(
        string operation, string provider, bool deny)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var seed = await SeedAsync(fixture, tenantOwned: provider == "local" && deny);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context, deny ? Effect.Deny : Effect.Allow);
        var preparation = Preparation(request.Services, fixture.Clock, provider, transport);
        await request.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        var execution = ExecuteAsync(request.Services, preparation, operation, seed);
        if (deny)
            await Assert.ThrowsAsync<AuthorizationException>(() => execution);
        else
        {
            await Assert.That((await execution).IsSuccess).IsTrue();
            await Assert.That(JsonSerializer.Serialize(await execution)
                .Contains("fixture-reference", StringComparison.Ordinal)).IsFalse();
        }
        await AssertPersistedAsync(fixture, seed, operation, !deny);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(transport.Requests.Count).IsEqualTo(provider == "cerbos" ? 1 : 0);
        if (provider == "cerbos")
        {
            var sent = transport.Requests.Single();
            await Assert.That(sent.Principal.Id).IsEqualTo(fixture.Binding.LocalSubjectId.ToString("D"));
            var resource = sent.Resources.Single();
            await Assert.That(resource.Resource.Id).IsEqualTo(
                (operation == "create" ? seed.Consumer.Id : seed.Endpoint.Id).ToString("D"));
            await Assert.That(resource.Actions.Single()).IsEqualTo(operation switch
            {
                "create" => AuthorizationActions.Webhooks.Create,
                "update" => AuthorizationActions.Webhooks.Update,
                _ => AuthorizationActions.Webhooks.RotateSecret
            });
            await Assert.That(resource.Resource.Attr["userId"].StringValue)
                .IsEqualTo(fixture.Binding.LocalSubjectId.ToString("D"));
        }
    }

    [Test]
    [Arguments("create", "revoke_identity")]
    [Arguments("update", "revoke_identity")]
    [Arguments("rotate", "revoke_identity")]
    [Arguments("create", "expire_after_reads")]
    [Arguments("update", "expire_after_reads")]
    [Arguments("rotate", "expire_after_reads")]
    [Arguments("create", "change_consumer")]
    [Arguments("update", "wrong_transition")]
    [Arguments("rotate", "wrong_transition")]
    public async Task FinalAdmissionFailureRollsBackRealEndpointSubscriptionsAndAudit(
        string operation, string change)
    {
        var interceptor = new AfterAuditSaved();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: interceptor);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var seed = await SeedAsync(fixture);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context, Effect.Allow);
        var preparation = Preparation(request.Services, fixture.Clock, "cerbos", transport);
        await request.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        var lifetime = (await request.Services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
            .ObserveAsync(request.Context.User, CancellationToken.None))!;
        interceptor.AfterSave = async (database, token) =>
        {
            await Assert.That(database).IsSameReferenceAs(context);
            await Assert.That(database.Database.CurrentTransaction!.GetDbTransaction().IsolationLevel)
                .IsEqualTo(IsolationLevel.Serializable);
            if (change == "revoke_identity")
                await database.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ProviderKey, Guid.NewGuid().ToString()), token);
            if (change == "change_consumer")
                await database.WebhookConsumers.Where(row => row.Id == seed.Consumer.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ConfigurationVersion, 2), token);
            if (change == "wrong_transition")
                await database.WebhookEndpoints.Where(row => row.Id == seed.Endpoint.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.DeliveryStateVersion,
                        row => row.DeliveryStateVersion + 1), token);
            if (change == "expire_after_reads")
                request.Services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
                {
                    await database.WebhookEndpoints.AsNoTracking()
                        .SingleAsync(row => row.Id == seed.Endpoint.Id, finalToken);
                    fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1)
                        - fixture.Clock.GetUtcNow());
                }, _ => { });
        };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            ExecuteAsync(request.Services, preparation, operation, seed));
        await Assert.That(interceptor.SavedAudits).IsEqualTo(1);
        await AssertPersistedAsync(fixture, seed, operation, committed: false);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(transport.Requests.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments("create")]
    [Arguments("update")]
    [Arguments("rotate")]
    public async Task AcknowledgedWholeAttemptRetryKeepsCreatedIdsAndSecretReferences(string operation)
    {
        var interceptor = new AfterAuditSaved();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: interceptor);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var seed = await SeedAsync(fixture);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context, Effect.Allow);
        var preparation = Preparation(request.Services, fixture.Clock, "cerbos", transport);
        await request.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        var attempts = new List<(Guid EndpointId, Guid SubscriptionId, string SecretRef)>();
        interceptor.AfterSave = (database, _) =>
        {
            var endpoint = database.ChangeTracker.Entries<WebhookEndpoint>().Single().Entity;
            var subscription = database.ChangeTracker.Entries<WebhookEndpointSubscription>()
                .Single(entry => entry.State != EntityState.Detached).Entity;
            attempts.Add((endpoint.Id, subscription.Id, endpoint.SecretRef));
            if (attempts.Count == 1)
                throw new SqliteException("Injected conflict after real writes, before commit.", 5);
            return Task.CompletedTask;
        };
        var result = await ExecuteAsync(request.Services, preparation, operation, seed);
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(attempts.Count).IsEqualTo(2);
        await Assert.That(attempts[1]).IsEqualTo(attempts[0]);
        await Assert.That(result.Id).IsEqualTo(attempts[1].EndpointId);
        await Assert.That(transport.Requests.Count).IsEqualTo(2);
        await AssertPersistedAsync(fixture, seed, operation, committed: true);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("update")]
    [Arguments("rotate")]
    public async Task StaleConfigurationVersionStillRejectsWithoutStateOrAudit(string operation)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var seed = await SeedAsync(fixture);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context, Effect.Allow);
        var preparation = Preparation(request.Services, fixture.Clock, "cerbos", transport);
        await request.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        var result = await ExecuteAsync(request.Services, preparation, operation, seed, expectedVersion: 2);
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("webhook_endpoint_configuration_conflict");
        await AssertPersistedAsync(fixture, seed, operation, committed: false);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("create", "active")]
    [Arguments("update", "active")]
    [Arguments("rotate", "active")]
    [Arguments("create", "expire_after_reads")]
    [Arguments("update", "expire_after_reads")]
    [Arguments("rotate", "expire_after_reads")]
    [Arguments("create", "revoke")]
    [Arguments("update", "revoke")]
    [Arguments("rotate", "revoke")]
    public async Task ForwardedSupportRemainsRequiredThroughActualOwnerCommit(string operation, string change)
    {
        var interceptor = new AfterAuditSaved();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: interceptor);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var seed = await SeedAsync(fixture);
        var expiresAt = fixture.Clock.GetUtcNow().AddMinutes(5);
        Guid sessionId;
        await using (var database = fixture.Native.CreateDatabase())
        {
            foreach (var key in new[] { GovernanceSettingKeys.SupportAccess.Enabled, GovernanceSettingKeys.SupportAccess.AllowWriteMode })
            {
                var setting = await database.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
                if (setting is null)
                    database.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "true" });
                else
                    setting.Value = "true";
            }
            var session = SupportAccessSession.Start(fixture.Binding.LocalSubjectId,
                PlatformDefaults.DefaultTenantId, SupportAccessModeEnum.Write, "maintenance",
                "Endpoint administration", "fixture-ticket", fixture.Clock.GetUtcNow().AddMinutes(-1), expiresAt);
            database.SupportAccessSessions.Add(session);
            await database.SaveChangesAsync();
            sessionId = session.Id;
        }
        request.Context.Request.Headers[SupportAccessHeaderNames.SessionId] = sessionId.ToString("D");
        var support = await request.Services.GetRequiredService<ISupportAccessSessionService>().GetCurrentAsync();
        await Assert.That(support.IsActive).IsTrue();
        await Assert.That(support.SessionId).IsEqualTo(sessionId);
        var lifetime = (await request.Services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
            .ObserveAsync(request.Context.User, CancellationToken.None))!;
        await Assert.That(lifetime.IsValidAt(expiresAt)).IsTrue();
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context, Effect.Allow);
        var preparation = Preparation(request.Services, fixture.Clock, "cerbos", transport);
        await request.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        interceptor.AfterSave = async (database, token) =>
        {
            if (change == "revoke")
                await database.SupportAccessSessions.Where(row => row.Id == sessionId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.StatusId,
                        (int)SupportAccessSessionStatusEnum.Revoked), token);
            if (change == "expire_after_reads")
                request.Services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
                {
                    await database.WebhookEndpoints.AsNoTracking()
                        .SingleAsync(row => row.Id == seed.Endpoint.Id, finalToken);
                    fixture.Clock.Advance(expiresAt - fixture.Clock.GetUtcNow());
                }, _ => { });
        };
        var execution = ExecuteAsync(request.Services, preparation, operation, seed);
        if (change == "active")
            await Assert.That((await execution).IsSuccess).IsTrue();
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        await Assert.That(interceptor.SavedAudits).IsEqualTo(1);
        await AssertPersistedAsync(fixture, seed, operation, committed: change == "active");
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("update", false)]
    [Arguments("rotate", false)]
    [Arguments("update", true)]
    [Arguments("rotate", true)]
    public async Task PendingSnapshotMigrationAndBothAuditsShareTheOriginalOwner(string operation, bool expire)
    {
        var interceptor = new AfterAuditSaved();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: interceptor);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var seed = await SeedAsync(fixture);
        Guid targetId;
        await using (var database = fixture.Native.CreateDatabase())
        {
            var now = fixture.Clock.GetUtcNow();
            var message = WebhookMessage.Create(PlatformDefaults.DefaultTenantId, "test.endpoint",
                Guid.CreateVersion7().ToString("D"), "event", Guid.CreateVersion7(), seed.Consumer.Id,
                "{}"u8, "application/json", "identity", now.UtcDateTime, now.AddDays(1).UtcDateTime, now.UtcDateTime);
            var plan = WebhookDeliveryPlanSnapshot.Create(message.TenantId, message.Id, seed.Consumer.Id,
                WebhookProviderMode.Local, "1", "1", "fixture", "1", now.AddDays(1), now.AddDays(2),
                now.AddDays(3), now.AddDays(2), now.AddDays(2), now);
            var target = WebhookLocalTargetSnapshot.Create(plan, seed.Endpoint, 1, now, null, now);
            database.AddRange(message, plan, target);
            await database.SaveChangesAsync();
            targetId = target.Id;
        }
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context, Effect.Allow);
        var preparation = Preparation(request.Services, fixture.Clock, "cerbos", transport);
        await request.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        var lifetime = (await request.Services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
            .ObserveAsync(request.Context.User, CancellationToken.None))!;
        interceptor.AfterSave = (database, _) =>
        {
            if (expire && interceptor.SavedAudits == 2)
                request.Services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
                {
                    await database.WebhookLocalTargetSnapshots.AsNoTracking()
                        .SingleAsync(row => row.Id == targetId, finalToken);
                    fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1)
                        - fixture.Clock.GetUtcNow());
                }, _ => { });
            return Task.CompletedTask;
        };
        var execution = ExecuteAsync(request.Services, preparation, operation, seed, migrate: true);
        if (expire)
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        else
            await Assert.That((await execution).IsSuccess).IsTrue();
        await Assert.That(interceptor.SavedAudits).IsEqualTo(2);
        await AssertPersistedAsync(fixture, seed, operation, committed: !expire, migrated: !expire);
        await using var observer = fixture.Native.CreateDatabase();
        var stored = await observer.WebhookLocalTargetSnapshots.AsNoTracking().SingleAsync(row => row.Id == targetId);
        await Assert.That(stored.EndpointConfigurationVersion).IsEqualTo(expire ? 1 : 2);
        await Assert.That(stored.DeliveryStatus).IsEqualTo(WebhookLocalDeliveryStatus.Pending);
        await Assert.That(stored.DeliveryFence).IsEqualTo(0L);
        await Assert.That(stored.DestinationUrl).IsEqualTo(!expire && operation == "update"
            ? "https://example.invalid/updated-endpoint" : seed.Endpoint.Url);
        await Assert.That(stored.CredentialReference).IsEqualTo(!expire && operation == "rotate"
            ? "fixture-reference-next" : seed.Endpoint.SecretRef);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    private static Task<BaseCommandResponse<Guid>> ExecuteAsync(IServiceProvider services,
        IHumanWebhookMutationPreparation preparation, string operation, Seed seed, int expectedVersion = 1,
        bool migrate = false) =>
        operation switch
        {
            "create" => ActivatorUtilities.CreateInstance<CreateWebhookEndpointCommandHandler>(services, preparation)
                .ExecuteAsync(new()
                {
                    ConsumerId = seed.Consumer.Id,
                    Url = "https://example.invalid/created-endpoint",
                    SecretRef = "fixture-reference-created",
                    EventTypeIds = [seed.EventTypeId]
                }, CancellationToken.None),
            "update" => ActivatorUtilities.CreateInstance<UpdateWebhookEndpointCommandHandler>(services, preparation)
                .ExecuteAsync(new()
                {
                    EndpointId = seed.Endpoint.Id,
                    Destination = new() { Url = "https://example.invalid/updated-endpoint" },
                    Governance = new()
                    {
                        ExpectedConfigurationVersion = expectedVersion,
                        PendingWorkDecisionId = (int)(migrate ? WebhookPendingWorkDecision.MigrateEligible
                            : WebhookPendingWorkDecision.PreserveExisting),
                        PendingWorkReason = "maintenance"
                    }
                }, CancellationToken.None),
            "rotate" => ActivatorUtilities.CreateInstance<RotateWebhookEndpointSecretCommandHandler>(services, preparation)
                .ExecuteAsync(new()
                {
                    EndpointId = seed.Endpoint.Id,
                    NewSecretRef = "fixture-reference-next",
                    PreviousSecretValidForSeconds = 60,
                    ExpectedConfigurationVersion = expectedVersion,
                    PendingWorkDecisionId = (int)(migrate ? WebhookPendingWorkDecision.MigrateEligible
                        : WebhookPendingWorkDecision.PreserveExisting),
                    PendingWorkReason = "maintenance"
                }, CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

    private static async Task AssertPersistedAsync(LocalIdentityLifecycleHttpFixture fixture,
        Seed seed, string operation, bool committed, bool migrated = false)
    {
        await using var observer = fixture.Native.CreateDatabase();
        var endpoints = await observer.WebhookEndpoints.AsNoTracking()
            .Where(row => row.ConsumerId == seed.Consumer.Id).ToListAsync();
        await Assert.That(endpoints.Count).IsEqualTo(committed && operation == "create" ? 2 : 1);
        var endpoint = endpoints.Single(row => row.Id == seed.Endpoint.Id);
        await Assert.That(endpoint.ConfigurationVersion).IsEqualTo(committed && operation != "create" ? 2 : 1);
        await Assert.That(endpoint.DeliveryStateVersion).IsEqualTo(seed.Endpoint.DeliveryStateVersion);
        await Assert.That(endpoint.Status).IsEqualTo(WebhookEndpointStatus.Active);
        await Assert.That(endpoint.Url).IsEqualTo(committed && operation == "update"
            ? "https://example.invalid/updated-endpoint" : seed.Endpoint.Url);
        await Assert.That(endpoint.SecretVersion).IsEqualTo(committed && operation == "rotate" ? 2 : 1);
        await Assert.That(endpoint.SecretRef).IsEqualTo(committed && operation == "rotate"
            ? "fixture-reference-next" : seed.Endpoint.SecretRef);
        await Assert.That(endpoint.PreviousSecretRef).IsEqualTo(committed && operation == "rotate"
            ? seed.Endpoint.SecretRef : null);
        if (committed && operation == "rotate")
            await Assert.That(endpoint.PreviousSecretValidUntil).IsEqualTo(endpoint.SecretActivatedAt.AddSeconds(60));
        var ids = endpoints.Select(row => row.Id).ToArray();
        var subscriptions = await observer.WebhookEndpointSubscriptions.AsNoTracking()
            .Where(row => row.EventTypeId == seed.EventTypeId).ToListAsync();
        await Assert.That(subscriptions.Count).IsEqualTo(endpoints.Count);
        foreach (var item in subscriptions)
        {
            await Assert.That(ids.Contains(item.EndpointId)).IsTrue();
            await Assert.That(item.EventTypeId).IsEqualTo(seed.EventTypeId);
            await Assert.That(item.TenantId).IsEqualTo(seed.Consumer.TenantId);
            await Assert.That(item.InstanceId).IsEqualTo(seed.Consumer.InstanceId);
        }
        if (!committed)
            await Assert.That(subscriptions.Single().Id).IsEqualTo(seed.SubscriptionId);
        if (committed && operation == "create")
        {
            var created = endpoints.Single(row => row.Id != seed.Endpoint.Id);
            await Assert.That(created.TenantId).IsEqualTo(seed.Consumer.TenantId);
            await Assert.That(created.InstanceId).IsEqualTo(seed.Consumer.InstanceId);
            await Assert.That(created.ConfigurationScopeId).IsEqualTo(seed.Consumer.ConfigurationScopeId);
            await Assert.That(created.ConfigurationVersion).IsEqualTo(1);
            await Assert.That(created.SecretVersion).IsEqualTo(1);
            await Assert.That(created.SecretRef).IsEqualTo("fixture-reference-created");
        }
        var audits = await observer.Set<WebhookAuditEvent>().AsNoTracking()
            .Where(row => row.EffectiveScopeId == seed.Consumer.OwnerId).ToListAsync();
        await Assert.That(audits.Count).IsEqualTo(committed ? migrated ? 2 : 1 : 0);
        foreach (var audit in audits)
        {
            var serialized = JsonSerializer.Serialize(audit);
            await Assert.That(serialized.Contains("fixture-reference", StringComparison.Ordinal)).IsFalse();
        }
        var consumer = await observer.WebhookConsumers.AsNoTracking().SingleAsync(row => row.Id == seed.Consumer.Id);
        await Assert.That(consumer.ConfigurationVersion).IsEqualTo(seed.Consumer.ConfigurationVersion);
    }

    private sealed record Seed(WebhookConsumer Consumer, WebhookEndpoint Endpoint, Guid EventTypeId, Guid SubscriptionId);

    private static async Task<Seed> SeedAsync(LocalIdentityLifecycleHttpFixture fixture, bool tenantOwned = false)
    {
        await using var database = fixture.Native.CreateDatabase();
        var now = fixture.Clock.GetUtcNow().UtcDateTime;
        var consumer = WebhookConsumer.Create(WebhookOwnershipScope.Create(
            tenantOwned ? WebhookConsumerKind.Tenant : WebhookConsumerKind.User,
            PlatformDefaults.DefaultTenantId, null, null, null, tenantOwned ? null : fixture.Binding.LocalSubjectId),
            "endpoint-consumption", WebhookProviderMode.Local, now);
        var endpoint = new WebhookEndpoint
        {
            Id = Guid.CreateVersion7(),
            TenantId = consumer.TenantId,
            ConsumerId = consumer.Id,
            Url = "https://example.invalid/original-endpoint",
            SecretRef = "fixture-reference-original",
            SecretVersion = 1,
            SecretActivatedAt = now,
            ConfigurationVersion = 1,
            Status = WebhookEndpointStatus.Active,
            DeliveryStateVersion = 1,
            MaxAttempts = 3,
            TimeoutSeconds = 10,
            CreatedAt = now
        };
        var eventType = new WebhookEventType
        {
            Id = Guid.CreateVersion7(),
            Name = $"test.endpoint.{Guid.NewGuid():N}",
            GroupName = "test",
            Description = "Endpoint consumer test",
            SchemaJson = "{}",
            SchemaVersion = 1,
            IsEnabled = true,
            IsPublic = true,
            PayloadRetentionDays = 1,
            CreatedAt = now
        };
        var subscription = new WebhookEndpointSubscription
        {
            Id = Guid.CreateVersion7(),
            TenantId = consumer.TenantId,
            EndpointId = endpoint.Id,
            EventTypeId = eventType.Id,
            IsEnabled = true,
            CreatedAt = now
        };
        database.AddRange(consumer, endpoint, eventType, subscription);
        await database.SaveChangesAsync();
        return new(consumer, endpoint, eventType.Id, subscription.Id);
    }

    private static HumanWebhookMutationPreparation Preparation(IServiceProvider services, TimeProvider clock,
        string provider, Transport transport)
    {
        var options = Options.Create(new CerbosSettings { GrpcEndpoint = "http://cerbos.webhook.test:3593" });
        var cerbos = ActivatorUtilities.CreateInstance<CerbosAuthorizationService>(
            services, transport.Client, transport.Factory, options);
        var registration = new ServiceCollection();
        registration.AddHumanWebhookResourceSources(IdentityDatabaseTopology.Colocated);
        var descriptor = registration.Single(row => row.ServiceType == typeof(IHumanWebhookResourceSourceFactory));
        var sources = (IHumanWebhookResourceSourceFactory)descriptor.ImplementationFactory!(services);
        return new(services.GetRequiredService<IHumanCommitScopePreparation>(),
            services.GetRequiredService<IHttpContextAccessor>(),
            services.GetRequiredService<IHumanAuthenticationLifetimeObserver>(),
            services.GetRequiredService<ITenantContext>(), clock, cerbos,
            Options.Create(new AuthorizationProviderDeploymentOptions { Provider = provider }), options,
            () => services.GetRequiredService<IHumanPrincipalSourceFence>(),
            () => services.GetRequiredService<IHumanAuthorizationRouteSource>(),
            () => services.GetRequiredService<IHumanSupportAccessSourceFactory>(),
            () => sources, () => services.GetRequiredService<IPolicySourceSetFence>(),
            () => services.GetRequiredService<IHumanActionCommitFinalizer>(),
            () => services.GetRequiredService<ISerializableAttemptOwner>());
    }

    private sealed class AfterAuditSaved : SaveChangesInterceptor
    {
        private bool _savingAudit;
        internal Func<ExploreDbContext, CancellationToken, Task>? AfterSave { get; set; }
        internal int SavedAudits { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            _savingAudit = AfterSave is not null && eventData.Context!.ChangeTracker
                .Entries<WebhookAuditEvent>().Any(entry => entry.State == EntityState.Added);
            return ValueTask.FromResult(result);
        }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (_savingAudit)
            {
                _savingAudit = false;
                SavedAudits++;
                await AfterSave!((ExploreDbContext)eventData.Context!, cancellationToken);
            }
            return result;
        }
    }

    private sealed class Request : IAsyncDisposable
    {
        private readonly AsyncServiceScope _scope;
        private readonly IHttpContextAccessor _accessor;
        internal DefaultHttpContext Context { get; }
        internal IServiceProvider Services => _scope.ServiceProvider;

        internal Request(IServiceProvider services)
        {
            _scope = services.CreateAsyncScope();
            Context = new DefaultHttpContext { RequestServices = Services };
            Context.Request.Path = "/human-webhook-endpoint-mutations";
            _accessor = Services.GetRequiredService<IHttpContextAccessor>();
            Services.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(PlatformDefaults.DefaultTenantId);
        }

        internal async Task AuthenticateAsync(string token)
        {
            _accessor.HttpContext = Context;
            Context.Request.Headers.Authorization = "Bearer " + token;
            var result = await Context.AuthenticateAsync();
            await Assert.That(result.Succeeded).IsTrue();
            Context.User = result.Principal!;
        }

        public async ValueTask DisposeAsync()
        {
            _accessor.HttpContext = null;
            await _scope.DisposeAsync();
        }
    }

    private sealed class Transport
    {
        internal ICerbosClient Client { get; } = Substitute.For<ICerbosClient>();
        internal ICerbosClientFactory Factory { get; } = Substitute.For<ICerbosClientFactory>();
        internal List<Cerbos.Api.V1.Request.CheckResourcesRequest> Requests { get; } = [];

        internal Transport(ExploreDbContext owner, Effect decision)
        {
            Factory.GetOrCreate(Arg.Any<string>()).Returns(Client);
            Client.CheckResourcesAsync(Arg.Any<Cerbos.Sdk.Builder.CheckResourcesRequest>(), Arg.Any<Metadata>())
                .Returns(call =>
                {
                    if (owner.Database.CurrentTransaction is not null)
                        throw new InvalidOperationException("PDP evaluation under source leases is forbidden.");
                    var sent = call.ArgAt<Cerbos.Sdk.Builder.CheckResourcesRequest>(0).ToCheckResourcesRequest().Clone();
                    Requests.Add(sent);
                    var response = new Cerbos.Api.V1.Response.CheckResourcesResponse { RequestId = sent.RequestId };
                    var result = new Cerbos.Api.V1.Response.CheckResourcesResponse.Types.ResultEntry
                    {
                        Resource = new() { Kind = sent.Resources.Single().Resource.Kind, Id = sent.Resources.Single().Resource.Id }
                    };
                    result.Actions.Add(sent.Resources.Single().Actions.Single(), decision);
                    response.Results.Add(result);
                    return new Cerbos.Sdk.Response.CheckResourcesResponse(response);
                });
        }
    }
}
