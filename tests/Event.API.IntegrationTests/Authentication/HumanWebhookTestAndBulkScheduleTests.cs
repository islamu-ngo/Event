using System.Data;
using System.Data.Common;
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
using Explore.Application.Features.Webhooks.Requests.Commands;
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
/// Actual queue admission handlers, signed credentials, persisted sources and the original
/// Serializable owner. Only external Cerbos transport and deterministic failure timing vary.
/// </summary>
[NotInParallel]
public sealed class HumanWebhookTestAndBulkScheduleTests
{
    [Test]
    [Arguments("test", "local", false)]
    [Arguments("test", "local", true)]
    [Arguments("bulk", "local", false)]
    [Arguments("bulk", "local", true)]
    [Arguments("test", "cerbos", false)]
    [Arguments("test", "cerbos", true)]
    [Arguments("bulk", "cerbos", false)]
    [Arguments("bulk", "cerbos", true)]
    public async Task SelectedDecisionControlsActualQueueAndAudit(string operation, string provider, bool deny)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var seed = await SeedAsync(fixture, tenantAdmin: !deny, tenantOwned: deny && operation == "test");
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context, deny ? Effect.Deny : Effect.Allow);
        var preparation = Preparation(request.Services, fixture.Clock, provider, transport);
        var command = Command(operation, seed, fixture);
        var execution = ExecuteAsync(request.Services, preparation, command);
        if (deny)
            await Assert.ThrowsAsync<AuthorizationException>(() => execution);
        else
            await Assert.That((await execution).IsSuccess).IsTrue();
        await AssertPersistedAsync(fixture, seed, operation, !deny);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(transport.Requests.Count).IsEqualTo(provider == "cerbos" ? 1 : 0);
        if (provider == "cerbos")
        {
            var sent = transport.Requests.Single();
            await Assert.That(sent.Principal.Id).IsEqualTo(fixture.Binding.LocalSubjectId.ToString("D"));
            var resource = sent.Resources.Single();
            await Assert.That(resource.Resource.Id).IsEqualTo(
                (operation == "test" ? seed.Endpoint.Id : PlatformDefaults.DefaultTenantId).ToString("D"));
            await Assert.That(resource.Actions.Single()).IsEqualTo(operation == "test"
                ? AuthorizationActions.Webhooks.Test : AuthorizationActions.Webhooks.BulkReplay);
        }
    }

    [Test]
    [Arguments("test", "expire")]
    [Arguments("bulk", "expire")]
    [Arguments("test", "identity")]
    [Arguments("bulk", "identity")]
    [Arguments("test", "wrong_transition")]
    [Arguments("bulk", "wrong_transition")]
    [Arguments("test", "retention")]
    [Arguments("test", "credential")]
    [Arguments("test", "route")]
    [Arguments("bulk", "route")]
    public async Task FinalAdmissionRollsBackActualQueueAndAudit(string operation, string failure)
    {
        var interceptor = new AfterAuditSaved();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: interceptor);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var seed = await SeedAsync(fixture);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context, Effect.Allow);
        var preparation = Preparation(request.Services, fixture.Clock, "cerbos", transport);
        var lifetime = (await request.Services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
            .ObserveAsync(request.Context.User, CancellationToken.None))!;
        interceptor.AfterSave = async (database, token) =>
        {
            await Assert.That(database).IsSameReferenceAs(context);
            await Assert.That(database.Database.CurrentTransaction!.GetDbTransaction().IsolationLevel)
                .IsEqualTo(IsolationLevel.Serializable);
            if (failure == "identity")
                await database.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ProviderKey, Guid.NewGuid().ToString()), token);
            if (failure == "credential")
                await database.WebhookEndpoints.Where(row => row.Id == seed.Endpoint.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.SecretVersion, 2), token);
            if (failure == "route")
            {
                foreach (var item in new[]
                {
                    (GovernanceSettingKeys.Cerbos.TenantCustomizationEnabled, "true"),
                    (GovernanceSettingKeys.Cerbos.Mode, "\"custom_endpoint\""),
                    (GovernanceSettingKeys.Cerbos.CustomEndpoint, "\"http://different.webhook.test:3593\"")
                })
                {
                    var setting = await database.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == item.Item1, token);
                    if (setting is null)
                        database.SystemSettings.Add(new SystemSetting { SettingKey = item.Item1, Value = item.Item2 });
                    else
                        setting.Value = item.Item2;
                }
                await database.SaveChangesAsync(token);
            }
            if (failure == "wrong_transition" && operation == "test")
                await database.WebhookEndpoints.Where(row => row.Id == seed.Endpoint.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.DeliveryStateVersion, 2L), token);
            if (failure == "wrong_transition" && operation == "bulk")
                await database.WebhookBulkReplayOperations.Where(row => row.OperationKey == seed.OperationKey)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.StatusId,
                        (int)WebhookBulkReplayStatus.Executing), token);
            if (failure == "retention")
            {
                await Assert.That(lifetime.IsValidAt(fixture.Clock.GetUtcNow())).IsTrue();
                await database.WebhookMessages.Where(row => row.AggregateId == seed.Endpoint.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.PayloadRetentionUntil,
                        fixture.Clock.GetUtcNow().UtcDateTime), token);
            }
            if (failure == "expire")
                request.Services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
                {
                    var endpoint = await database.WebhookEndpoints.AsNoTracking()
                        .SingleAsync(row => row.Id == seed.Endpoint.Id, finalToken);
                    await Assert.That(endpoint.Id).IsEqualTo(seed.Endpoint.Id);
                    var expires = lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1);
                    fixture.Clock.Advance(expires - fixture.Clock.GetUtcNow());
                }, _ => { });
        };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            ExecuteAsync(request.Services, preparation, Command(operation, seed, fixture)));
        await Assert.That(interceptor.SavedAudits).IsEqualTo(1);
        await AssertPersistedAsync(fixture, seed, operation, false);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(transport.Requests.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments("test", false)]
    [Arguments("bulk", false)]
    [Arguments("test", true)]
    [Arguments("bulk", true)]
    public async Task OnlyOriginalAcknowledgedRollbackRepreparesWithStableQueueIds(string operation, bool forged)
    {
        var interceptor = new AfterAuditSaved();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: interceptor);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var seed = await SeedAsync(fixture);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context, Effect.Allow);
        var preparation = Preparation(request.Services, fixture.Clock, "cerbos", transport);
        var products = new List<(Guid Message, Guid Plan, Guid Target, Guid Operation)>();
        interceptor.AfterSave = (database, _) =>
        {
            products.Add(operation == "test"
                ? (database.ChangeTracker.Entries<WebhookMessage>().Single().Entity.Id,
                    database.ChangeTracker.Entries<WebhookDeliveryPlanSnapshot>().Single().Entity.Id,
                    database.ChangeTracker.Entries<WebhookLocalTargetSnapshot>().Single().Entity.Id, Guid.Empty)
                : (Guid.Empty, Guid.Empty, Guid.Empty,
                    database.ChangeTracker.Entries<WebhookBulkReplayOperation>().Single().Entity.Id));
            if (products.Count == 1)
            {
                var conflict = new SqliteException("Conflict after real writes, before actual owner settlement.", 5);
                if (forged)
                    throw new AggregateException(new InvalidOperationException("Borrowed owner receipt.",
                        new HumanMutationAttemptRolledBackException(conflict)));
                throw conflict;
            }
            return Task.CompletedTask;
        };
        var execution = ExecuteAsync(request.Services, preparation, Command(operation, seed, fixture));
        if (forged)
            await Assert.ThrowsAsync<InvalidOperationException>(() => execution);
        else
        {
            var result = await execution;
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(products.Count).IsEqualTo(2);
            await Assert.That(products[1]).IsEqualTo(products[0]);
            await Assert.That(result.Id).IsEqualTo(operation == "test" ? products[0].Message : products[0].Operation);
        }
        await Assert.That(transport.Requests.Count).IsEqualTo(forged ? 1 : 2);
        await AssertPersistedAsync(fixture, seed, operation, !forged);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task BulkDuplicateAndConflictKeepQueueIdentityAndAuditedRejection()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var seed = await SeedAsync(fixture);
        var transport = new Transport(request.Services.GetRequiredService<ExploreDbContext>(), Effect.Allow);
        var preparation = Preparation(request.Services, fixture.Clock, "cerbos", transport);
        var command = (ScheduleWebhookBulkReplayCommand)Command("bulk", seed, fixture);
        var first = await ExecuteAsync(request.Services, preparation, command);
        var duplicate = await ExecuteAsync(request.Services, preparation, command);
        await Assert.That(duplicate.Id).IsEqualTo(first.Id);
        await Assert.That(duplicate.IsSuccess).IsTrue();
        var conflict = await ExecuteAsync(request.Services, preparation, command with { MaxItems = 2 });
        await Assert.That(conflict.FailureCode).IsEqualTo("webhook_bulk_replay_idempotency_conflict");
        await using var observer = fixture.Native.CreateDatabase();
        var stored = await observer.WebhookBulkReplayOperations.AsNoTracking()
            .SingleAsync(row => row.OperationKey == seed.OperationKey);
        await Assert.That(stored.Id).IsEqualTo(first.Id);
        await Assert.That(stored.Status).IsEqualTo(WebhookBulkReplayStatus.Queued);
        await Assert.That(stored.ConcurrencyVersion).IsEqualTo(1L);
        await Assert.That(await observer.Set<WebhookAuditEvent>()
            .CountAsync(row => row.TargetId == first.Id || row.TargetId == seed.OperationKey)).IsEqualTo(2);
        await Assert.That(transport.Requests.Count).IsEqualTo(3);
    }

    [Test]
    public async Task ExistingBulkReceiptCannotReplaceCurrentRequesterPermission()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var seed = await SeedAsync(fixture);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var command = Command("bulk", seed, fixture);
        var allowed = Preparation(request.Services, fixture.Clock, "cerbos", new Transport(context, Effect.Allow));
        await Assert.That((await ExecuteAsync(request.Services, allowed, command)).IsSuccess).IsTrue();
        var denied = Preparation(request.Services, fixture.Clock, "cerbos", new Transport(context, Effect.Deny));
        await Assert.ThrowsAsync<AuthorizationException>(() => ExecuteAsync(request.Services, denied, command));
        await AssertPersistedAsync(fixture, seed, "bulk", true);
    }

    [Test]
    public async Task RejectedBulkAuditAlsoRequiresFinalRequesterAdmission()
    {
        var interceptor = new AfterAuditSaved();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: interceptor);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var seed = await SeedAsync(fixture);
        await using (var database = fixture.Native.CreateDatabase())
            await database.WebhookMessages.Where(row => row.ConsumerId == seed.Consumer.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.PayloadRetentionUntil,
                    fixture.Clock.GetUtcNow().UtcDateTime));
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var preparation = Preparation(request.Services, fixture.Clock, "cerbos", new Transport(context, Effect.Allow));
        var lifetime = (await request.Services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
            .ObserveAsync(request.Context.User, CancellationToken.None))!;
        interceptor.AfterSave = (database, _) =>
        {
            request.Services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
            {
                await database.WebhookEndpoints.AsNoTracking().SingleAsync(row => row.Id == seed.Endpoint.Id, finalToken);
                fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1) - fixture.Clock.GetUtcNow());
            }, _ => { });
            return Task.CompletedTask;
        };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            ExecuteAsync(request.Services, preparation, Command("bulk", seed, fixture)));
        await Assert.That(interceptor.SavedAudits).IsEqualTo(1);
        await AssertPersistedAsync(fixture, seed, "bulk", false);
    }

    [Test]
    [Arguments("test", false)]
    [Arguments("bulk", false)]
    [Arguments("test", true)]
    [Arguments("bulk", true)]
    public async Task QueueSuccessWaitsForActualOwnerAcknowledgement(string operation, bool failCommit)
    {
        var barrier = new CommitBarrier();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: barrier);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var seed = await SeedAsync(fixture);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context, Effect.Allow);
        var preparation = Preparation(request.Services, fixture.Clock, "cerbos", transport);
        barrier.Owner = context;
        barrier.Fail = failCommit;
        var execution = ExecuteAsync(request.Services, preparation, Command(operation, seed, fixture));
        try
        {
            await Task.WhenAny(barrier.Entered.Task, execution).WaitAsync(TimeSpan.FromSeconds(15));
            await Assert.That(barrier.Entered.Task.IsCompletedSuccessfully).IsTrue();
            await Assert.That(execution.IsCompleted).IsFalse();
            await Assert.That(barrier.Acknowledged).IsFalse();
        }
        finally
        {
            barrier.Release.TrySetResult();
        }
        if (failCommit)
            await Assert.ThrowsAsync<HumanMutationOutcomeUnknownException>(
                () => execution.WaitAsync(TimeSpan.FromSeconds(15)));
        else
            await Assert.That((await execution.WaitAsync(TimeSpan.FromSeconds(15))).IsSuccess).IsTrue();
        await Assert.That(barrier.Acknowledged).IsEqualTo(!failCommit);
        await Assert.That(transport.Requests.Count).IsEqualTo(1);
        await AssertPersistedAsync(fixture, seed, operation, !failCommit);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("test", "active")]
    [Arguments("bulk", "active")]
    [Arguments("test", "expire")]
    [Arguments("bulk", "expire")]
    [Arguments("test", "revoke")]
    [Arguments("bulk", "revoke")]
    public async Task ForwardedSupportRestrictsActualCommitAfterFinalReads(string operation, string change)
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
                "Webhook queue administration", "fixture-ticket", fixture.Clock.GetUtcNow().AddMinutes(-1), expiresAt);
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
        interceptor.AfterSave = async (database, token) =>
        {
            if (change == "revoke")
                await database.SupportAccessSessions.Where(row => row.Id == sessionId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.StatusId,
                        (int)SupportAccessSessionStatusEnum.Revoked), token);
            if (change == "expire")
                request.Services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
                {
                    await database.WebhookEndpoints.AsNoTracking()
                        .SingleAsync(row => row.Id == seed.Endpoint.Id, finalToken);
                    fixture.Clock.Advance(expiresAt - fixture.Clock.GetUtcNow());
                }, _ => { });
        };
        var execution = ExecuteAsync(request.Services, preparation, Command(operation, seed, fixture));
        if (change == "active")
            await Assert.That((await execution).IsSuccess).IsTrue();
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        await Assert.That(interceptor.SavedAudits).IsEqualTo(1);
        await AssertPersistedAsync(fixture, seed, operation, change == "active");
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("test")]
    [Arguments("bulk")]
    public async Task SelectedProviderUnavailableNeverFallsBackToLocalGrant(string operation)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var seed = await SeedAsync(fixture);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context, Effect.Allow) { Unavailable = true };
        var preparation = Preparation(request.Services, fixture.Clock, "cerbos", transport);
        await Assert.ThrowsAsync<AuthorizationProviderUnavailableException>(() =>
            ExecuteAsync(request.Services, preparation, Command(operation, seed, fixture)));
        await AssertPersistedAsync(fixture, seed, operation, false);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("test", "disabled")]
    [Arguments("test", "provider")]
    [Arguments("bulk", "disabled")]
    [Arguments("bulk", "retention")]
    [Arguments("bulk", "active_target")]
    public async Task OriginalQueueEligibilityStillRejectsWithoutScheduling(string operation, string condition)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var seed = await SeedAsync(fixture);
        await using (var database = fixture.Native.CreateDatabase())
        {
            if (condition == "disabled")
                await database.WebhookEndpoints.Where(row => row.Id == seed.Endpoint.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.StatusId, (int)WebhookEndpointStatus.Disabled));
            if (condition == "provider")
                await database.WebhookConsumers.Where(row => row.Id == seed.Consumer.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ProviderModeId, (int)WebhookProviderMode.Svix));
            if (condition == "retention")
                await database.WebhookMessages.Where(row => row.ConsumerId == seed.Consumer.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.PayloadRetentionUntil,
                        fixture.Clock.GetUtcNow().UtcDateTime));
            if (condition == "active_target")
                await database.WebhookLocalTargetSnapshots.Where(row => row.Id == seed.TargetId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.DeliveryStatusId,
                        (int)WebhookLocalDeliveryStatus.Delivering));
        }
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var preparation = Preparation(request.Services, fixture.Clock, "cerbos", new Transport(context, Effect.Allow));
        var result = await ExecuteAsync(request.Services, preparation, Command(operation, seed, fixture));
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(operation == "bulk"
            ? "webhook_bulk_replay_no_eligible_work"
            : condition == "disabled" ? "webhook_endpoint_not_active" : "webhook_endpoint_test_provider_managed");
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.WebhookBulkReplayOperations.CountAsync(row => row.OperationKey == seed.OperationKey))
            .IsEqualTo(0);
        await Assert.That(await observer.WebhookMessages.CountAsync(row => row.AggregateId == seed.Endpoint.Id)).IsEqualTo(0);
        await Assert.That(await observer.Set<WebhookAuditEvent>().CountAsync(row =>
            row.TargetId == seed.OperationKey && row.OutcomeId == (int)WebhookAuditOutcome.Rejected))
            .IsEqualTo(operation == "bulk" ? 1 : 0);
        var source = await observer.WebhookLocalTargetSnapshots.AsNoTracking().SingleAsync(row => row.Id == seed.TargetId);
        await Assert.That(source.DeliveryStatus).IsEqualTo(condition == "active_target"
            ? WebhookLocalDeliveryStatus.Delivering : WebhookLocalDeliveryStatus.DeadLettered);
        await Assert.That(source.ConcurrencyVersion).IsEqualTo(3L);
    }

    private static ISecureRequest Command(string operation, Seed seed, LocalIdentityLifecycleHttpFixture fixture) =>
        operation == "test"
            ? new TestWebhookEndpointCommand { EndpointId = seed.Endpoint.Id, SourceTenantId = PlatformDefaults.DefaultTenantId }
            : new ScheduleWebhookBulkReplayCommand
            {
                TenantId = PlatformDefaults.DefaultTenantId,
                // Attribution is deliberately different from the authenticated requester.
                ActorUserId = Guid.CreateVersion7(),
                OperationKey = seed.OperationKey,
                FromUtc = fixture.Clock.GetUtcNow().AddHours(-1).UtcDateTime,
                ToUtc = fixture.Clock.GetUtcNow().AddMinutes(1).UtcDateTime,
                WebhookConsumerId = seed.Consumer.Id,
                WebhookEndpointId = seed.Endpoint.Id,
                MaxItems = 1,
                ReasonCode = "operator.recovery"
            };

    private static Task<BaseCommandResponse<Guid>> ExecuteAsync(IServiceProvider services,
        IHumanWebhookMutationPreparation preparation, ISecureRequest command) => command switch
        {
            TestWebhookEndpointCommand test => ActivatorUtilities.CreateInstance<TestWebhookEndpointCommandHandler>(
                services, preparation).ExecuteAsync(test, CancellationToken.None),
            ScheduleWebhookBulkReplayCommand bulk => ActivatorUtilities.CreateInstance<ScheduleWebhookBulkReplayCommandHandler>(
                services, preparation).ExecuteAsync(bulk, CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        };

    private static async Task AssertPersistedAsync(LocalIdentityLifecycleHttpFixture fixture, Seed seed,
        string operation, bool committed)
    {
        await using var observer = fixture.Native.CreateDatabase();
        var messages = await observer.WebhookMessages.AsNoTracking()
            .Where(row => row.AggregateId == seed.Endpoint.Id).ToListAsync();
        await Assert.That(messages.Count).IsEqualTo(committed && operation == "test" ? 1 : 0);
        var plans = await observer.WebhookDeliveryPlanSnapshots.AsNoTracking()
            .Where(row => row.WebhookConsumerId == seed.Consumer.Id && row.Id != seed.PlanId).ToListAsync();
        var targets = await observer.WebhookLocalTargetSnapshots.AsNoTracking()
            .Where(row => row.WebhookEndpointId == seed.Endpoint.Id && row.Id != seed.TargetId).ToListAsync();
        await Assert.That(plans.Count).IsEqualTo(messages.Count);
        await Assert.That(targets.Count).IsEqualTo(messages.Count);
        if (messages.Count == 1)
        {
            var message = messages.Single();
            var plan = plans.Single();
            var target = targets.Single();
            await Assert.That(message.ConsumerId).IsEqualTo(seed.Consumer.Id);
            await Assert.That(message.GetPayloadBytes()).IsNotNull();
            await Assert.That(plan.WebhookMessageId).IsEqualTo(message.Id);
            await Assert.That(target.WebhookMessageId).IsEqualTo(message.Id);
            await Assert.That(target.DeliveryPlanSnapshotId).IsEqualTo(plan.Id);
            await Assert.That(target.DeliveryStatus).IsEqualTo(WebhookLocalDeliveryStatus.Pending);
            await Assert.That(target.ConcurrencyVersion).IsEqualTo(1L);
            await Assert.That(target.DeliveryFence).IsEqualTo(0L);
            await Assert.That(target.CredentialReference).IsEqualTo(seed.Endpoint.SecretRef);
        }
        var operations = await observer.WebhookBulkReplayOperations.AsNoTracking()
            .Where(row => row.OperationKey == seed.OperationKey).ToListAsync();
        await Assert.That(operations.Count).IsEqualTo(committed && operation == "bulk" ? 1 : 0);
        if (operations.Count == 1)
        {
            await Assert.That(operations.Single().Status).IsEqualTo(WebhookBulkReplayStatus.Queued);
            await Assert.That(operations.Single().ConcurrencyVersion).IsEqualTo(1L);
            await Assert.That(operations.Single().EstimatedSelectedCount).IsEqualTo(1);
        }
        // Count the whole action cohort, not only surviving operation IDs: orphan audits fail.
        await Assert.That(await observer.Set<WebhookAuditEvent>().CountAsync(row =>
            row.TenantId == PlatformDefaults.DefaultTenantId &&
            (row.ActionId == (int)WebhookAuditAction.EndpointTestScheduled ||
             row.ActionId == (int)WebhookAuditAction.BulkReplayScheduled))).IsEqualTo(committed ? 1 : 0);
        var source = await observer.WebhookLocalTargetSnapshots.AsNoTracking().SingleAsync(row => row.Id == seed.TargetId);
        await Assert.That(source.DeliveryStatus).IsEqualTo(WebhookLocalDeliveryStatus.DeadLettered);
        await Assert.That(source.ConcurrencyVersion).IsEqualTo(3L);
        await Assert.That(await observer.WebhookDeliveryAttempts.CountAsync(row => row.EndpointId == seed.Endpoint.Id))
            .IsEqualTo(0);
        var endpoint = await observer.WebhookEndpoints.AsNoTracking().SingleAsync(row => row.Id == seed.Endpoint.Id);
        await Assert.That(endpoint.ConfigurationVersion).IsEqualTo(1);
        await Assert.That(endpoint.DeliveryStateVersion).IsEqualTo(1L);
        await Assert.That(endpoint.Status).IsEqualTo(WebhookEndpointStatus.Active);
    }

    private sealed record Seed(WebhookConsumer Consumer, WebhookEndpoint Endpoint, Guid PlanId, Guid TargetId, Guid OperationKey);

    private static async Task<Seed> SeedAsync(LocalIdentityLifecycleHttpFixture fixture,
        bool tenantAdmin = true, bool tenantOwned = false)
    {
        await using var database = fixture.Native.CreateDatabase();
        var now = fixture.Clock.GetUtcNow();
        var consumer = WebhookConsumer.Create(WebhookOwnershipScope.Create(
            tenantOwned ? WebhookConsumerKind.Tenant : WebhookConsumerKind.User,
            PlatformDefaults.DefaultTenantId, null, null, null, tenantOwned ? null : fixture.Binding.LocalSubjectId),
            "queue-admission", WebhookProviderMode.Local, now.UtcDateTime);
        var endpoint = new WebhookEndpoint
        {
            Id = Guid.CreateVersion7(),
            TenantId = consumer.TenantId,
            ConsumerId = consumer.Id,
            Url = "https://example.invalid/queue-admission",
            SecretRef = "fixture-reference",
            SecretVersion = 1,
            SecretActivatedAt = now.UtcDateTime,
            ConfigurationVersion = 1,
            Status = WebhookEndpointStatus.Active,
            DeliveryStateVersion = 1,
            MaxAttempts = 3,
            TimeoutSeconds = 10,
            CreatedAt = now.UtcDateTime
        };
        var message = WebhookMessage.Create(PlatformDefaults.DefaultTenantId, "test.queue",
            Guid.CreateVersion7().ToString("D"), "event", Guid.CreateVersion7(), consumer.Id,
            "{}"u8, "application/json", "identity", now.UtcDateTime, now.AddHours(1).UtcDateTime, now.UtcDateTime);
        var plan = WebhookDeliveryPlanSnapshot.Create(message.TenantId, message.Id, consumer.Id,
            WebhookProviderMode.Local, "1", "1", "fixture", "1", now.AddHours(1), now.AddDays(2),
            now.AddDays(3), now.AddDays(2), now.AddDays(2), now);
        var target = WebhookLocalTargetSnapshot.Create(plan, endpoint, 1, now, null, now);
        var leaseToken = Guid.CreateVersion7();
        target.ClaimForDelivery("fixture", leaseToken, now.AddMinutes(1), now);
        target.DeadLetter(leaseToken, target.DeliveryFence, now);
        database.AddRange(consumer, endpoint, message, plan, target);
        if (tenantAdmin)
        {
            var member = await database.TenantUsers.SingleOrDefaultAsync(row =>
                row.TenantId == PlatformDefaults.DefaultTenantId && row.UserId == fixture.Binding.LocalSubjectId);
            if (member is null)
            {
                member = new TenantUser
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = PlatformDefaults.DefaultTenantId,
                    Tenant = null!,
                    UserId = fixture.Binding.LocalSubjectId,
                    User = null!,
                    StatusId = (int)TenantUserStatusEnum.Active,
                    JoinedAt = now.UtcDateTime
                };
                database.TenantUsers.Add(member);
            }
            database.TenantUserRoleGrants.Add(new TenantUserRoleGrant
            {
                Id = Guid.CreateVersion7(),
                TenantId = member.TenantId,
                Tenant = null!,
                TenantUserId = member.Id,
                TenantUser = member,
                RoleId = (int)RoleEnum.TenantAdmin,
                Role = null!,
                RoleScopeId = (int)RoleScopeEnum.Tenant
            });
        }
        await database.SaveChangesAsync();
        return new(consumer, endpoint, plan.Id, target.Id, Guid.CreateVersion7());
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
            Context.Request.Path = "/human-webhook-queue-admission";
            _accessor = Services.GetRequiredService<IHttpContextAccessor>();
            Services.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(PlatformDefaults.DefaultTenantId);
        }
        internal Task AuthenticateAsync(string token)
        {
            _accessor.HttpContext = Context;
            Context.Request.Headers.Authorization = "Bearer " + token;
            return CompleteAuthenticationAsync();
        }
        private async Task CompleteAuthenticationAsync()
        {
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
        internal bool Unavailable { get; init; }
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
                    if (Unavailable)
                        throw new RpcException(new Status(StatusCode.Unavailable, "Injected external PDP outage."));
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

    private sealed class CommitBarrier : DbTransactionInterceptor
    {
        internal ExploreDbContext? Owner { get; set; }
        internal bool Fail { get; set; }
        internal bool Acknowledged { get; private set; }
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (!ReferenceEquals(eventData.Context, Owner)) return result;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            if (Fail) throw new InvalidOperationException("Injected original-owner commit failure.");
            return result;
        }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, Owner)) Acknowledged = true;
            return Task.CompletedTask;
        }
    }
}
