using Cerbos.Api.V1.Effect;
using Cerbos.Sdk;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Webhooks;
using Explore.Application.Exceptions;
using Explore.Application.Features.Webhooks.Requests.Commands;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Event.Api.IntegrationTests.Authentication;

/// <summary>
/// Original validated Local credentials, persisted P/route/H/W, and the real committing owner.
/// Only external Cerbos transport is substituted. These sources are intentionally authored
/// before the preparation implementation; execution belongs to programme final assurance.
/// </summary>
[NotInParallel]
public sealed partial class HumanWebhookPreparationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ActualManualRetryUsesSelectedOwnerAndRejectsSelectedProviderDenial(bool deny)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var (consumer, endpoint) = await SeedAsync(fixture);
        Guid attemptId, targetId;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            var now = fixture.Clock.GetUtcNow().UtcDateTime;
            var expiry = fixture.Clock.GetUtcNow().AddDays(1);
            var message = WebhookMessage.Create(PlatformDefaults.DefaultTenantId, "test.prepared",
                Guid.CreateVersion7().ToString("D"), "event", Guid.CreateVersion7(), consumer.Id,
                "{}"u8, "application/json", "identity", now, expiry.UtcDateTime, now);
            var plan = WebhookDeliveryPlanSnapshot.Create(message.TenantId, message.Id, consumer.Id,
                WebhookProviderMode.Local, "1", "1", "prepared", "1", expiry, expiry.AddDays(1),
                expiry.AddDays(2), expiry.AddDays(1), expiry.AddDays(1), now);
            var target = WebhookLocalTargetSnapshot.Create(plan, endpoint, 1, now, null, now);
            var attempt = new WebhookDeliveryAttempt
            {
                Id = Guid.CreateVersion7(),
                TenantId = message.TenantId,
                MessageId = message.Id,
                EndpointId = endpoint.Id,
                AttemptNumber = 1,
                Outcome = WebhookDeliveryAttemptOutcome.Failed,
                ScheduledAt = now,
                CreatedAt = now
            };
            seed.AddRange(message, plan, target, attempt);
            await seed.SaveChangesAsync();
            await seed.WebhookLocalTargetSnapshots.Where(row => row.Id == target.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.DeliveryStatusId,
                    (int)WebhookLocalDeliveryStatus.DeadLettered));
            attemptId = attempt.Id;
            targetId = target.Id;
        }
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context) { Decision = deny ? Effect.Deny : Effect.Allow };
        var preparation = CreatePreparation(request.Services, fixture.Clock, "cerbos", transport);
        await request.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        var handler = ActivatorUtilities.CreateInstance<RetryWebhookDeliveryAttemptCommandHandler>(
            request.Services, preparation);
        var execution = handler.ExecuteAsync(new() { AttemptId = attemptId }, CancellationToken.None);
        if (deny) await Assert.ThrowsAsync<AuthorizationException>(() => execution);
        else await Assert.That((await execution).IsSuccess).IsTrue();
        await using var observer = fixture.Native.CreateDatabase();
        var stored = await observer.WebhookLocalTargetSnapshots.AsNoTracking().SingleAsync(row => row.Id == targetId);
        await Assert.That(stored.DeliveryStatusId).IsEqualTo((int)(deny
            ? WebhookLocalDeliveryStatus.DeadLettered : WebhookLocalDeliveryStatus.RetryDue));
        await Assert.That(await observer.Set<WebhookAuditEvent>().AsNoTracking()
            .CountAsync(row => row.TargetId == attemptId)).IsEqualTo(deny ? 0 : 1);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(transport.Requests.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments("acquired")]
    [Arguments("not_acquired")]
    [Arguments("another_command")]
    public async Task SchedulerOwnedAcquisitionCannotCommitWithoutTheOriginalPreparedCommand(string acquisition)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var (_, endpoint) = await SeedAsync(fixture);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context);
        var preparation = CreatePreparation(request.Services, fixture.Clock, "cerbos", transport);
        var command = new ArchiveWebhookEndpointCommand { EndpointId = endpoint.Id };
        string key = $"webhook-acquisition-{Guid.NewGuid():N}";
        var execution = preparation.ExecuteAsync(command, async (prepared, token) =>
        {
            if (acquisition != "not_acquired")
                await prepared.AcquireAsync(acquisition == "another_command" ? command with { } : command, token);
            context.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "acquired" });
            await context.SaveChangesAsync(token);
            return true;
        });
        if (acquisition == "acquired") await Assert.That(await execution).IsTrue();
        else if (acquisition == "another_command") await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        else await Assert.ThrowsAsync<InvalidOperationException>(() => execution);
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SystemSettings.AsNoTracking().AnyAsync(row => row.SettingKey == key))
            .IsEqualTo(acquisition == "acquired");
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(transport.Requests.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ActualPublicationHandlersRequireSelectedRequesterDecisionBeforeStateAndAudit(bool abandon, bool deny)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var (consumer, _) = await SeedAsync(fixture);
        Guid tenantId = consumer.TenantId!.Value;
        Guid publicationId;
        long originalVersion;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            var now = fixture.Clock.GetUtcNow().UtcDateTime;
            var message = WebhookMessage.Create(tenantId, "test.publication", Guid.CreateVersion7().ToString("D"),
                "event", Guid.CreateVersion7(), consumer.Id, "{}"u8, "application/json", "identity", now, now.AddDays(1), now);
            var plan = WebhookDeliveryPlanSnapshot.Create(tenantId, message.Id, consumer.Id,
                WebhookProviderMode.Composite, "1", "1", "prepared", "1", now.AddDays(1), now.AddDays(2),
                now.AddDays(3), now.AddDays(2), now.AddDays(2), now);
            var profile = WebhookProviderCapabilityProfile.Create(WebhookProviderKind.Svix, "1",
                WebhookProviderCapability.None, "1", now);
            var binding = WebhookConsumerProviderBinding.CreateLegacyUnverified(
                consumer.TenantId, consumer.Id, Guid.CreateVersion7(), "fixture", "application", profile);
            var publication = WebhookProviderPublication.CreateLegacy(Guid.CreateVersion7(), message, plan, binding,
                "application", null, WebhookProviderPublicationStatus.ManualReconciliation, 0, "fixture", null, now);
            seed.AddRange(message, plan, binding, publication);
            await seed.SaveChangesAsync();
            publicationId = publication.Id;
            originalVersion = publication.ConcurrencyVersion;
        }
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context) { Decision = deny ? Effect.Deny : Effect.Allow };
        var preparation = CreatePreparation(request.Services, fixture.Clock, "cerbos", transport);
        await request.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        Task<BaseCommandResponse<Guid>> Execute() => abandon
            ? ActivatorUtilities.CreateInstance<AbandonWebhookProviderPublicationCommandHandler>(
                request.Services, preparation, fixture.Clock).ExecuteAsync(new()
                {
                    TenantId = tenantId,
                    PublicationId = publicationId,
                    ActorUserId = fixture.Binding.LocalSubjectId,
                    ExpectedConcurrencyVersion = originalVersion,
                    ReasonCode = "maintenance"
                }, CancellationToken.None)
            : ActivatorUtilities.CreateInstance<ReconcileWebhookProviderPublicationCommandHandler>(
                request.Services, preparation, fixture.Clock).ExecuteAsync(new()
                {
                    TenantId = tenantId,
                    PublicationId = publicationId,
                    ActorUserId = fixture.Binding.LocalSubjectId,
                    ExpectedConcurrencyVersion = originalVersion,
                    ExternalProviderMessageId = "provider-message",
                    ReasonCode = "maintenance"
                }, CancellationToken.None);
        if (deny) await Assert.ThrowsAsync<AuthorizationException>(Execute);
        else await Assert.That((await Execute()).IsSuccess).IsTrue();
        await using var observer = fixture.Native.CreateDatabase();
        var final = await observer.WebhookProviderPublications.AsNoTracking().SingleAsync(row => row.Id == publicationId);
        await Assert.That(final.Status).IsEqualTo(deny ? WebhookProviderPublicationStatus.ManualReconciliation
            : abandon ? WebhookProviderPublicationStatus.Abandoned : WebhookProviderPublicationStatus.ProviderQueued);
        await Assert.That(final.ConcurrencyVersion).IsEqualTo(deny ? originalVersion : originalVersion + 1);
        await Assert.That(await observer.Set<WebhookAuditEvent>().AsNoTracking()
            .CountAsync(row => row.TargetId == publicationId)).IsEqualTo(deny ? 0 : 1);
        await Assert.That(transport.Requests.Count).IsEqualTo(1);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("local")]
    [Arguments("cerbos")]
    public async Task ActualArchivePauseAndResumeHandlersUseTheOriginalRequesterOwner(string provider)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var (_, endpoint) = await SeedAsync(fixture);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context);
        var preparation = CreatePreparation(request.Services, fixture.Clock, provider, transport);
        await request.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        var pause = ActivatorUtilities.CreateInstance<PauseWebhookEndpointCommandHandler>(
            request.Services, preparation, fixture.Clock);
        var paused = await pause.ExecuteAsync(new PauseWebhookEndpointCommand
        {
            EndpointId = endpoint.Id,
            ActorUserId = fixture.Binding.LocalSubjectId,
            ExpectedDeliveryStateVersion = endpoint.DeliveryStateVersion,
            ReasonCode = "maintenance"
        }, CancellationToken.None);
        await Assert.That(paused.IsSuccess).IsTrue();
        var resume = ActivatorUtilities.CreateInstance<ResumeWebhookEndpointCommandHandler>(
            request.Services, preparation, fixture.Clock);
        var resumed = await resume.ExecuteAsync(new ResumeWebhookEndpointCommand
        {
            EndpointId = endpoint.Id,
            ActorUserId = fixture.Binding.LocalSubjectId,
            ExpectedDeliveryStateVersion = endpoint.DeliveryStateVersion + 1,
            ReasonCode = "maintenance"
        }, CancellationToken.None);
        await Assert.That(resumed.IsSuccess).IsTrue();
        var archive = ActivatorUtilities.CreateInstance<ArchiveWebhookEndpointCommandHandler>(
            request.Services, preparation);
        await Assert.That((await archive.ExecuteAsync(new ArchiveWebhookEndpointCommand
        { EndpointId = endpoint.Id }, CancellationToken.None)).IsSuccess).IsTrue();
        await Assert.That((await archive.ExecuteAsync(new ArchiveWebhookEndpointCommand
        { EndpointId = endpoint.Id }, CancellationToken.None)).IsSuccess).IsTrue();
        await using var observer = fixture.Native.CreateDatabase();
        var final = await observer.WebhookEndpoints.AsNoTracking().SingleAsync(row => row.Id == endpoint.Id);
        await Assert.That(final.Status).IsEqualTo(WebhookEndpointStatus.Archived);
        await Assert.That(final.DeliveryStateVersion).IsEqualTo(endpoint.DeliveryStateVersion + 3);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(transport.Requests.Count).IsEqualTo(provider == "cerbos" ? 4 : 0);
    }

    [Test]
    public async Task FailedBusinessResponseRollsBackAnAlreadyPersistedWebhookTransition()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var (_, endpoint) = await SeedAsync(fixture);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context);
        var preparation = CreatePreparation(request.Services, fixture.Clock, "cerbos", transport);
        var rejected = BaseCommandResponse.Failure<Guid>("webhook_endpoint_conflict");
        var result = await preparation.ExecuteAsync(new ArchiveWebhookEndpointCommand
        { EndpointId = endpoint.Id }, async token =>
    {
        await request.Services.GetRequiredService<IWebhookEndpointRepository>()
            .ArchiveAsync(endpoint.TenantId, endpoint.Id, fixture.Clock.GetUtcNow().UtcDateTime, token);
        return rejected;
    });
        await Assert.That(result).IsSameReferenceAs(rejected);
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That((await observer.WebhookEndpoints.AsNoTracking()
            .SingleAsync(row => row.Id == endpoint.Id)).Status).IsEqualTo(WebhookEndpointStatus.Active);
        await Assert.That(transport.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ActualArchiveHandlerCannotReplaceSelectedCerbosDenialWithLocalOwnership()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var (_, endpoint) = await SeedAsync(fixture);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context) { Decision = Effect.Deny };
        var preparation = CreatePreparation(request.Services, fixture.Clock, "cerbos", transport);
        await request.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        var handler = ActivatorUtilities.CreateInstance<ArchiveWebhookEndpointCommandHandler>(
            request.Services, preparation);
        await Assert.ThrowsAsync<AuthorizationException>(() => handler.ExecuteAsync(
            new ArchiveWebhookEndpointCommand { EndpointId = endpoint.Id }, CancellationToken.None));
        await using var observer = fixture.Native.CreateDatabase();
        var final = await observer.WebhookEndpoints.AsNoTracking().SingleAsync(row => row.Id == endpoint.Id);
        await Assert.That(final.Status).IsEqualTo(WebhookEndpointStatus.Active);
        await Assert.That(final.DeliveryStateVersion).IsEqualTo(endpoint.DeliveryStateVersion);
        await Assert.That(transport.Requests.Count).IsEqualTo(1);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("local", "archive")]
    [Arguments("cerbos", "archive")]
    [Arguments("local", "replace_parent")]
    [Arguments("cerbos", "replace_parent")]
    [Arguments("local", "wrong_transition")]
    [Arguments("cerbos", "wrong_transition")]
    [Arguments("local", "revoke_identity")]
    [Arguments("cerbos", "revoke_identity")]
    [Arguments("local", "expire_after_reads")]
    [Arguments("cerbos", "expire_after_reads")]
    [Arguments("local", "change_route")]
    [Arguments("cerbos", "change_route")]
    public async Task OwnArchiveIsAdmittedButCustodyAuthorityAndUnexpectedTransitionsRollBack(string provider, string change)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var (consumer, endpoint) = await SeedAsync(fixture);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context);
        var preparation = CreatePreparation(request.Services, fixture.Clock, provider, transport);
        var command = new ArchiveWebhookEndpointCommand { EndpointId = endpoint.Id };
        var prepared = await preparation.PrepareAsync(command);
        var lifetime = (await request.Services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
            .ObserveAsync(request.Context.User, CancellationToken.None))!;
        Guid replacement;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            var other = WebhookConsumer.Create(consumer.Ownership, "replacement", WebhookProviderMode.Local,
                fixture.Clock.GetUtcNow().UtcDateTime);
            seed.WebhookConsumers.Add(other);
            await seed.SaveChangesAsync();
            replacement = other.Id;
        }
        var execution = request.Services.GetRequiredService<ISerializableAttemptOwner>().ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(command, token);
            await request.Services.GetRequiredService<IWebhookEndpointRepository>()
                .ArchiveAsync(endpoint.TenantId, endpoint.Id, fixture.Clock.GetUtcNow().UtcDateTime, token);
            if (change == "replace_parent")
                await context.WebhookEndpoints.Where(row => row.Id == endpoint.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ConsumerId, replacement), token);
            if (change == "wrong_transition")
                await context.WebhookEndpoints.Where(row => row.Id == endpoint.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.StatusId, (int)WebhookEndpointStatus.Disabled), token);
            if (change == "revoke_identity")
                await context.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ProviderKey, Guid.NewGuid().ToString()), token);
            if (change == "change_route")
            {
                foreach (var item in new[]
                {
                    (GovernanceSettingKeys.Cerbos.TenantCustomizationEnabled, "true"),
                    (GovernanceSettingKeys.Cerbos.Mode, "\"custom_endpoint\""),
                    (GovernanceSettingKeys.Cerbos.CustomEndpoint, "\"http://different.webhook.test:3593\"")
                })
                {
                    var setting = await context.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == item.Item1, token);
                    if (setting is null)
                        context.SystemSettings.Add(new SystemSetting { SettingKey = item.Item1, Value = item.Item2 });
                    else setting.Value = item.Item2;
                }
                await context.SaveChangesAsync(token);
            }
            request.Services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
            {
                await context.WebhookEndpoints.AsNoTracking().SingleAsync(row => row.Id == endpoint.Id, finalToken);
                if (change == "expire_after_reads")
                    fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1) - fixture.Clock.GetUtcNow());
            }, _ => { });
            return true;
        });
        if (change == "archive") await Assert.That(await execution).IsTrue();
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        await using var observer = fixture.Native.CreateDatabase();
        var actual = await observer.WebhookEndpoints.AsNoTracking().SingleAsync(row => row.Id == endpoint.Id);
        await Assert.That(actual.Status).IsEqualTo(change == "archive" ? WebhookEndpointStatus.Archived : WebhookEndpointStatus.Active);
        await Assert.That(actual.ConsumerId).IsEqualTo(consumer.Id);
        await Assert.That(transport.Requests.Count).IsEqualTo(provider == "cerbos" ? 1 : 0);
        await Assert.ThrowsAsync<InvalidOperationException>(() => prepared.AcquireAsync(command));
    }

    [Test]
    public async Task EqualCommandAndReplacedHttpPrincipalCannotBorrowOriginalCapability()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var (_, endpoint) = await SeedAsync(fixture);
        var preparation = CreatePreparation(request.Services, fixture.Clock, "local",
            new Transport(request.Services.GetRequiredService<ExploreDbContext>()));
        var command = new ArchiveWebhookEndpointCommand { EndpointId = endpoint.Id };
        var prepared = await preparation.PrepareAsync(command);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            request.Services.GetRequiredService<ISerializableAttemptOwner>().ExecuteAsync(async token =>
            {
                await prepared.AcquireAsync(command with { }, token);
                return true;
            }));
        prepared = await preparation.PrepareAsync(command);
        request.Context.User = request.Context.User.Clone();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            request.Services.GetRequiredService<ISerializableAttemptOwner>().ExecuteAsync(async token =>
            {
                await prepared.AcquireAsync(command, token);
                return true;
            }));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CallbackReceiptEvenWhenNestedCannotAuthorizeReplay(bool nested)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var (_, endpoint) = await SeedAsync(fixture);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context);
        var preparation = CreatePreparation(request.Services, fixture.Clock, "cerbos", transport);
        int invocations = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => preparation.ExecuteAsync<bool>(
            new ArchiveWebhookEndpointCommand { EndpointId = endpoint.Id }, async token =>
            {
                invocations++;
                await request.Services.GetRequiredService<IWebhookEndpointRepository>()
                    .ArchiveAsync(endpoint.TenantId, endpoint.Id, fixture.Clock.GetUtcNow().UtcDateTime, token);
                var receipt = new HumanMutationAttemptRolledBackException(new SqliteException("Untrusted receipt", 5));
                if (nested) throw new AggregateException(new InvalidOperationException("nested", receipt));
                throw receipt;
            }));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That((await observer.WebhookEndpoints.SingleAsync(row => row.Id == endpoint.Id)).Status)
            .IsEqualTo(WebhookEndpointStatus.Active);
        await Assert.That(invocations).IsEqualTo(1);
        await Assert.That(transport.Requests.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments("local")]
    [Arguments("cerbos")]
    public async Task CancellationAdmitsOnlyQueuedToCancelledWithOriginalFilterCustody(string provider)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var (consumer, _) = await SeedAsync(fixture, tenantAdmin: true);
        Guid id;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            var now = fixture.Clock.GetUtcNow().UtcDateTime;
            var operation = WebhookBulkReplayOperation.Create(PlatformDefaults.DefaultTenantId, Guid.CreateVersion7(),
                "sha256:" + new string('b', 64), now.AddHours(-1), now, consumer.Id, null, null, 1, "test",
                new WebhookBulkReplayPreviewSnapshot(1, 0, 0, 0, 0, 0, 0, 0, 0), now);
            seed.WebhookBulkReplayOperations.Add(operation);
            await seed.SaveChangesAsync();
            id = operation.Id;
        }
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context);
        var preparation = CreatePreparation(request.Services, fixture.Clock, provider, transport);
        var command = new CancelWebhookBulkReplayCommand
        {
            TenantId = PlatformDefaults.DefaultTenantId,
            OperationId = id,
            ExpectedConcurrencyVersion = 1,
            ActorUserId = Guid.CreateVersion7(),
            ReasonCode = "cancel"
        };
        await preparation.ExecuteAsync(command, async token =>
        {
            var operation = await context.WebhookBulkReplayOperations.SingleAsync(row => row.Id == id, token);
            operation.Cancel("cancel", fixture.Clock.GetUtcNow().UtcDateTime);
            await context.SaveChangesAsync(token);
            return true;
        });
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That((await observer.WebhookBulkReplayOperations.SingleAsync(row => row.Id == id)).Status)
            .IsEqualTo(WebhookBulkReplayStatus.Cancelled);
        if (provider == "cerbos")
        {
            var sent = transport.Requests.Single().Resources.Single();
            await Assert.That(sent.Actions.Single()).IsEqualTo(AuthorizationActions.Webhooks.BulkReplay);
            await Assert.That(sent.Resource.Attr.ContainsKey("ownerKindId")).IsFalse();
        }
    }

    [Test]
    public async Task DelegatedOwnerDoesNotGainStrictActionsAndInstanceShortcutNeverOverridesCerbosDenial()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var (consumer, endpoint) = await SeedAsync(fixture);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context) { Decision = Effect.Deny };
        var local = CreatePreparation(request.Services, fixture.Clock, "local", transport);
        await local.PrepareAsync(new ArchiveWebhookEndpointCommand { EndpointId = endpoint.Id });
        await Assert.ThrowsAsync<AuthorizationException>(() => local.PrepareAsync(
            new RepairWebhookProviderBindingCommand { ConsumerId = consumer.Id, ExternalApplicationId = "application", ReasonCode = "test" }));
        await Assert.ThrowsAsync<AuthorizationException>(() => local.PrepareAsync(new ScheduleWebhookBulkReplayCommand
        {
            TenantId = PlatformDefaults.DefaultTenantId,
            OperationKey = Guid.CreateVersion7(),
            WebhookConsumerId = consumer.Id,
            ReasonCode = "test"
        }));
        await using (var seed = fixture.Native.CreateDatabase())
        {
            int role = await seed.Roles.Where(row => row.MasterCode == "platform.admin"
                && row.RoleScopeId == (int)RoleScopeEnum.Platform).Select(row => row.Id).SingleAsync();
            seed.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = fixture.Binding.LocalSubjectId,
                User = null!,
                RoleId = role,
                Role = null!,
                GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
            });
            await seed.SaveChangesAsync();
        }
        await local.PrepareAsync(new RepairWebhookProviderBindingCommand
        { ConsumerId = consumer.Id, ExternalApplicationId = "application", ReasonCode = "test" });
        var cerbos = CreatePreparation(request.Services, fixture.Clock, "cerbos", transport);
        await Assert.ThrowsAsync<AuthorizationException>(() => cerbos.PrepareAsync(
            new ArchiveWebhookEndpointCommand { EndpointId = endpoint.Id }));
        await Assert.That(transport.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task PortalCheckpointRetainsOriginalRequesterAndConsumerAcrossRemoteAwaitBoundaries()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var (consumer, _) = await SeedAsync(fixture);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context);
        var preparation = CreatePreparation(request.Services, fixture.Clock, "cerbos", transport);
        var command = new OpenSvixAppPortalCommand { ConsumerId = consumer.Id, SessionId = "audit-not-authority" };
        var checkpoint = await preparation.PreparePortalAsync(command);
        await checkpoint.ValidateAsync(command);
        await Assert.That(checkpoint.Consumer.ConsumerId).IsEqualTo(consumer.Id);
        await using (var writer = fixture.Native.CreateDatabase())
            await writer.WebhookConsumers.Where(row => row.Id == consumer.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.ConfigurationVersion, row => row.ConfigurationVersion + 1));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => checkpoint.ValidateAsync(command));
        await Assert.That(transport.Requests.Count).IsEqualTo(1);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.ThrowsAsync<NotSupportedException>(() => preparation.PrepareAsync(command));
    }

    [Test]
    public async Task CerbosReceivesTheOriginalUnsortedPrincipalAndPersistedWebhookFacts()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var (consumer, endpoint) = await SeedAsync(fixture);
        var transport = new Transport(request.Services.GetRequiredService<ExploreDbContext>());
        var preparation = CreatePreparation(request.Services, fixture.Clock, "cerbos", transport);
        await preparation.ExecuteAsync(new ArchiveWebhookEndpointCommand { EndpointId = endpoint.Id }, _ => Task.FromResult(true));
        var original = await request.Services.GetRequiredService<CerbosPrincipalBuilder>()
            .BuildSdkPrincipalAsync(fixture.Binding.LocalSubjectId);
        var expected = Cerbos.Sdk.Builder.CheckResourcesRequest.NewInstance().WithPrincipal(original)
            .ToCheckResourcesRequest().Principal;
        var sent = transport.Requests.Single();
        await Assert.That(sent.Principal.Equals(expected)).IsTrue();
        await Assert.That(sent.Resources.Single().Actions.Single()).IsEqualTo(AuthorizationActions.Webhooks.Delete);
        await Assert.That(sent.Resources.Single().Resource.Id).IsEqualTo(endpoint.Id.ToString("D"));
        await Assert.That(sent.Resources.Single().Resource.Attr["userId"].StringValue)
            .IsEqualTo(consumer.OwnerUserId!.Value.ToString("D"));
    }

    [Test]
    public async Task ExternalLocalIdentityFailsBeforeAnyProviderRequest()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(topology: IdentityDatabaseTopology.External);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var (_, endpoint) = await SeedAsync(fixture);
        var transport = new Transport(request.Services.GetRequiredService<ExploreDbContext>());
        var preparation = CreatePreparation(request.Services, fixture.Clock, "cerbos", transport, IdentityDatabaseTopology.External);
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            preparation.PrepareAsync(new ArchiveWebhookEndpointCommand { EndpointId = endpoint.Id }));
        await Assert.That(transport.Requests.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task DelegatedMembershipMatchesOriginalAdminContextIncludingPendingPlacementAndDeletedOwner(
        bool groupOwner, bool deletedOwner)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        Guid owner = Guid.CreateVersion7();
        WebhookConsumer consumer;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            Guid placement = Guid.CreateVersion7();
            if (groupOwner)
            {
                var group = new Group
                {
                    Id = owner,
                    FullName = "Webhook group",
                    IsDeleted = deletedOwner,
                    ConcurrencyStamp = Guid.CreateVersion7(),
                    CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime
                };
                var participation = new GroupTenant
                {
                    Id = placement,
                    TenantId = PlatformDefaults.DefaultTenantId,
                    Tenant = null!,
                    GroupId = owner,
                    Group = group,
                    ApprovalStatusId = (int)ApprovalStatusEnum.Pending,
                    ApprovalStatus = null!,
                    ConcurrencyStamp = Guid.CreateVersion7()
                };
                seed.GroupMembers.Add(new GroupMember
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = participation.TenantId,
                    Tenant = null!,
                    GroupTenantId = placement,
                    GroupTenant = participation,
                    UserId = fixture.Binding.LocalSubjectId,
                    User = null!,
                    RoleId = (int)RoleEnum.GroupAdmin,
                    Role = null!
                });
            }
            else
            {
                var organization = new Organization
                {
                    Id = owner,
                    Pii = new OrganizationPii { FullName = "Webhook organization" },
                    IsDeleted = deletedOwner,
                    ConcurrencyStamp = Guid.CreateVersion7(),
                    CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime
                };
                var participation = new OrganizationTenant
                {
                    Id = placement,
                    TenantId = PlatformDefaults.DefaultTenantId,
                    Tenant = null!,
                    OrganizationId = owner,
                    Organization = organization,
                    ApprovalStatusId = (int)ApprovalStatusEnum.Pending,
                    ApprovalStatus = null!,
                    ConcurrencyStamp = Guid.CreateVersion7()
                };
                seed.OrganizationMembers.Add(new OrganizationMember
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = participation.TenantId,
                    Tenant = null!,
                    OrganizationTenantId = placement,
                    OrganizationTenant = participation,
                    UserId = fixture.Binding.LocalSubjectId,
                    User = null!,
                    RoleId = (int)RoleEnum.OrgAdmin,
                    Role = null!
                });
            }
            consumer = WebhookConsumer.Create(WebhookOwnershipScope.Create(
                groupOwner ? WebhookConsumerKind.Group : WebhookConsumerKind.Organization,
                PlatformDefaults.DefaultTenantId, null, groupOwner ? null : owner, groupOwner ? owner : null, null),
                "delegated", WebhookProviderMode.Local, fixture.Clock.GetUtcNow().UtcDateTime);
            seed.WebhookConsumers.Add(consumer);
            await seed.SaveChangesAsync();
        }
        var original = await request.Services.GetRequiredService<FallbackAuthorizationService>().AuthorizeAsync(
            new AuthorizationRequest(ResourceKinds.Webhook, consumer.Id.ToString("D"), AuthorizationActions.Webhooks.Create,
                Facts: WebhookOwnershipAuthorizationFacts.From(consumer.Ownership)));
        await Assert.That(original.IsAllowed).IsEqualTo(!deletedOwner);
        var preparation = CreatePreparation(request.Services, fixture.Clock, "local",
            new Transport(request.Services.GetRequiredService<ExploreDbContext>()));
        var command = new CreateWebhookEndpointCommand
        {
            ConsumerId = consumer.Id,
            Url = "https://example.invalid/new-endpoint",
            SecretRef = "fixture-reference"
        };
        if (deletedOwner)
            await Assert.ThrowsAsync<AuthorizationException>(() => preparation.PrepareAsync(command));
        else
        {
            var prepared = await preparation.PrepareAsync(command);
            await using (var writer = fixture.Native.CreateDatabase())
            {
                if (groupOwner)
                    await writer.GroupMembers.Where(row => row.UserId == fixture.Binding.LocalSubjectId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
                else
                    await writer.OrganizationMembers.Where(row => row.UserId == fixture.Binding.LocalSubjectId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
            }
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                request.Services.GetRequiredService<ISerializableAttemptOwner>().ExecuteAsync(async token =>
                {
                    await prepared.AcquireAsync(command, token);
                    return true;
                }));
        }
    }

    [Test]
    [Arguments("local", false, false)]
    [Arguments("cerbos", false, false)]
    [Arguments("local", true, false)]
    [Arguments("cerbos", true, false)]
    [Arguments("local", false, true)]
    [Arguments("cerbos", false, true)]
    public async Task ExactManualRetryAllowsOnlyTerminalTargetTransitionAndKeepsOriginalPlan(
        string provider, bool replacePlan, bool expirePayload)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var (consumer, endpoint) = await SeedAsync(fixture);
        Guid attemptId, targetId, replacementPlan;
        var expiry = fixture.Clock.GetUtcNow().AddMinutes(1);
        await using (var seed = fixture.Native.CreateDatabase())
        {
            var now = fixture.Clock.GetUtcNow().UtcDateTime;
            var message = WebhookMessage.Create(PlatformDefaults.DefaultTenantId, "test.prepared", Guid.CreateVersion7().ToString("D"),
                "event", Guid.CreateVersion7(), consumer.Id, "{}"u8, "application/json", "identity", now, expiry.UtcDateTime, now);
            var otherMessage = WebhookMessage.Create(PlatformDefaults.DefaultTenantId, "test.prepared", Guid.CreateVersion7().ToString("D"),
                "event", Guid.CreateVersion7(), consumer.Id, "{}"u8, "application/json", "identity", now, expiry.UtcDateTime, now);
            WebhookDeliveryPlanSnapshot Plan(WebhookMessage source) => WebhookDeliveryPlanSnapshot.Create(source.TenantId, source.Id, consumer.Id,
                WebhookProviderMode.Local, "1", "1", "prepared", "1", expiry, expiry.AddDays(1),
                expiry.AddDays(2), expiry.AddDays(1), expiry.AddDays(1), now);
            var plan = Plan(message);
            var other = Plan(otherMessage);
            var target = WebhookLocalTargetSnapshot.Create(plan, endpoint, 1, now, null, now);
            var attempt = new WebhookDeliveryAttempt
            {
                Id = Guid.CreateVersion7(),
                TenantId = message.TenantId,
                MessageId = message.Id,
                EndpointId = endpoint.Id,
                AttemptNumber = 1,
                Outcome = WebhookDeliveryAttemptOutcome.Failed,
                ScheduledAt = now,
                CreatedAt = now
            };
            seed.AddRange(message, otherMessage, plan, other, target, attempt);
            await seed.SaveChangesAsync();
            await seed.WebhookLocalTargetSnapshots.Where(row => row.Id == target.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.DeliveryStatusId, (int)WebhookLocalDeliveryStatus.DeadLettered));
            attemptId = attempt.Id;
            targetId = target.Id;
            replacementPlan = other.Id;
        }
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context);
        var preparation = CreatePreparation(request.Services, fixture.Clock, provider, transport);
        var execution = preparation.ExecuteAsync(new RetryWebhookDeliveryAttemptCommand { AttemptId = attemptId }, async token =>
        {
            var target = await context.WebhookLocalTargetSnapshots.SingleAsync(row => row.Id == targetId, token);
            target.ScheduleManualRetry(fixture.Clock.GetUtcNow());
            await context.SaveChangesAsync(token);
            if (replacePlan)
                await context.WebhookLocalTargetSnapshots.Where(row => row.Id == targetId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.DeliveryPlanSnapshotId, replacementPlan), token);
            request.Services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
            {
                await context.WebhookLocalTargetSnapshots.AsNoTracking().SingleAsync(row => row.Id == targetId, finalToken);
                if (expirePayload) fixture.Clock.Advance(expiry - fixture.Clock.GetUtcNow());
            }, _ => { });
            return true;
        });
        if (replacePlan || expirePayload)
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        else await Assert.That(await execution).IsTrue();
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That((await observer.WebhookLocalTargetSnapshots.SingleAsync(row => row.Id == targetId)).DeliveryStatus)
            .IsEqualTo(replacePlan || expirePayload ? WebhookLocalDeliveryStatus.DeadLettered : WebhookLocalDeliveryStatus.RetryDue);
        await Assert.That(transport.Requests.Count).IsEqualTo(provider == "cerbos" ? 1 : 0);
    }

    [Test]
    public async Task AllSixteenOriginalCommandsKeepExactProviderResourceAndAction()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var (consumer, endpoint) = await SeedAsync(fixture, tenantAdmin: true);
        Guid tenant = PlatformDefaults.DefaultTenantId, attemptId, publicationId, operationId;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            var now = fixture.Clock.GetUtcNow().UtcDateTime;
            var message = WebhookMessage.Create(tenant, "test.catalog", Guid.CreateVersion7().ToString("D"),
                "event", Guid.CreateVersion7(), consumer.Id, "{}"u8, "application/json", "identity", now, now.AddDays(1), now);
            var plan = WebhookDeliveryPlanSnapshot.Create(tenant, message.Id, consumer.Id,
                WebhookProviderMode.Composite, "1", "1", "prepared", "1", now.AddDays(1), now.AddDays(2),
                now.AddDays(3), now.AddDays(2), now.AddDays(2), now);
            var target = WebhookLocalTargetSnapshot.Create(plan, endpoint, 1, now, null, now);
            var attempt = new WebhookDeliveryAttempt
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant,
                MessageId = message.Id,
                EndpointId = endpoint.Id,
                AttemptNumber = 1,
                Outcome = WebhookDeliveryAttemptOutcome.Failed,
                ScheduledAt = now,
                CreatedAt = now
            };
            var profile = WebhookProviderCapabilityProfile.Create(WebhookProviderKind.Svix, "1",
                WebhookProviderCapability.None, "1", now);
            var binding = WebhookConsumerProviderBinding.CreateLegacyUnverified(
                tenant, consumer.Id, Guid.CreateVersion7(), "fixture", "application", profile);
            var publication = WebhookProviderPublication.CreateLegacy(Guid.CreateVersion7(), message, plan, binding,
                "application", null, WebhookProviderPublicationStatus.ManualReconciliation, 0, "fixture", null, now);
            var operation = WebhookBulkReplayOperation.Create(tenant, Guid.CreateVersion7(),
                "sha256:" + new string('c', 64), now.AddHours(-1), now, consumer.Id, endpoint.Id, null, 1, "test",
                new WebhookBulkReplayPreviewSnapshot(1, 0, 0, 0, 0, 0, 0, 0, 0), now);
            seed.AddRange(message, plan, target, attempt, binding, publication, operation);
            await seed.SaveChangesAsync();
            attemptId = attempt.Id;
            publicationId = publication.Id;
            operationId = operation.Id;
        }
        (ISecureRequest Command, Guid Id, string Action)[] cases =
        [
            (new CreateWebhookConsumerCommand { ConsumerKindId = (int)WebhookConsumerKind.User, Name = "new" },
                fixture.Binding.LocalSubjectId, AuthorizationActions.Webhooks.Create),
            (new CreateWebhookEndpointCommand { ConsumerId = consumer.Id, Url = "https://example.invalid/new", SecretRef = "fixture-reference" },
                consumer.Id, AuthorizationActions.Webhooks.Create),
            (new UpdateWebhookConsumerProviderModeCommand { ConsumerId = consumer.Id, PendingWorkReason = "test" },
                consumer.Id, AuthorizationActions.Webhooks.Update),
            (new UpdateWebhookEndpointCommand { EndpointId = endpoint.Id, Governance = new UpdateWebhookEndpointGovernanceDto { PendingWorkReason = "test" } },
                endpoint.Id, AuthorizationActions.Webhooks.Update),
            (new ArchiveWebhookEndpointCommand { EndpointId = endpoint.Id }, endpoint.Id, AuthorizationActions.Webhooks.Delete),
            (new PauseWebhookEndpointCommand { EndpointId = endpoint.Id }, endpoint.Id, AuthorizationActions.Webhooks.Pause),
            (new ResumeWebhookEndpointCommand { EndpointId = endpoint.Id }, endpoint.Id, AuthorizationActions.Webhooks.Resume),
            (new RotateWebhookEndpointSecretCommand { EndpointId = endpoint.Id, NewSecretRef = "fixture-reference-next", PendingWorkReason = "test" },
                endpoint.Id, AuthorizationActions.Webhooks.RotateSecret),
            (new TestWebhookEndpointCommand { EndpointId = endpoint.Id, SourceTenantId = tenant }, endpoint.Id, AuthorizationActions.Webhooks.Test),
            (new RetryWebhookDeliveryAttemptCommand { AttemptId = attemptId }, attemptId, AuthorizationActions.Webhooks.Retry),
            (new ScheduleWebhookBulkReplayCommand { TenantId = tenant, OperationKey = Guid.CreateVersion7(), ReasonCode = "test" },
                tenant, AuthorizationActions.Webhooks.BulkReplay),
            (new CancelWebhookBulkReplayCommand { TenantId = tenant, OperationId = operationId, ReasonCode = "test" },
                operationId, AuthorizationActions.Webhooks.BulkReplay),
            (new RepairWebhookProviderBindingCommand { ConsumerId = consumer.Id, ExternalApplicationId = "application", ReasonCode = "test" },
                consumer.Id, AuthorizationActions.Webhooks.ManageProvider),
            (new ReconcileWebhookProviderPublicationCommand { TenantId = tenant, PublicationId = publicationId },
                publicationId, AuthorizationActions.Webhooks.ReconcilePublication),
            (new AbandonWebhookProviderPublicationCommand { TenantId = tenant, PublicationId = publicationId },
                publicationId, AuthorizationActions.Webhooks.AbandonPublication),
            (new OpenSvixAppPortalCommand { ConsumerId = consumer.Id, SessionId = "original" },
                consumer.Id, AuthorizationActions.Webhooks.OpenProviderPortal)
        ];
        var transport = new Transport(request.Services.GetRequiredService<ExploreDbContext>());
        var preparation = CreatePreparation(request.Services, fixture.Clock, "cerbos", transport);
        foreach (var item in cases)
        {
            if (item.Command is OpenSvixAppPortalCommand portal)
                await (await preparation.PreparePortalAsync(portal)).ValidateAsync(portal);
            else
                await preparation.ExecuteAsync(item.Command, _ => Task.FromResult(true));
            var sent = transport.Requests[^1].Resources.Single();
            await Assert.That(sent.Actions.Single()).IsEqualTo(item.Action);
            await Assert.That(sent.Resource.Kind).IsEqualTo(ResourceKinds.Webhook);
            await Assert.That(sent.Resource.Id).IsEqualTo(item.Id.ToString("D"));
        }
        await Assert.That(transport.Requests.Count).IsEqualTo(16);
    }

    private static HumanWebhookMutationPreparation CreatePreparation(IServiceProvider services, TimeProvider clock,
        string provider, Transport transport, IdentityDatabaseTopology topology = IdentityDatabaseTopology.Colocated)
    {
        var options = Options.Create(new CerbosSettings { GrpcEndpoint = "http://cerbos.webhook.test:3593" });
        var cerbos = ActivatorUtilities.CreateInstance<CerbosAuthorizationService>(
            services, transport.Client, transport.Factory, options);
        // Invoke the production opt-in registration against the actual request scope.
        // This keeps source resolution lazy without replacing any source, lease or owner.
        var registration = new ServiceCollection();
        registration.AddHumanWebhookResourceSources(topology);
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
            () => sources,
            () => services.GetRequiredService<IPolicySourceSetFence>(),
            () => services.GetRequiredService<IHumanActionCommitFinalizer>(),
            () => services.GetRequiredService<ISerializableAttemptOwner>());
    }

    private static async Task<(WebhookConsumer Consumer, WebhookEndpoint Endpoint)> SeedAsync(
        LocalIdentityLifecycleHttpFixture fixture, bool tenantAdmin = false)
    {
        await using var seed = fixture.Native.CreateDatabase();
        var consumer = WebhookConsumer.Create(WebhookOwnershipScope.Create(WebhookConsumerKind.User,
            PlatformDefaults.DefaultTenantId, null, null, null, fixture.Binding.LocalSubjectId),
            "prepared-webhook", WebhookProviderMode.Local, fixture.Clock.GetUtcNow().UtcDateTime);
        var endpoint = new WebhookEndpoint
        {
            Id = Guid.CreateVersion7(),
            TenantId = consumer.TenantId,
            ConsumerId = consumer.Id,
            Url = "https://example.invalid/prepared-webhook",
            SecretRef = "fixture-reference",
            SecretVersion = 1,
            SecretActivatedAt = fixture.Clock.GetUtcNow().UtcDateTime,
            ConfigurationVersion = 1,
            Status = WebhookEndpointStatus.Active,
            MaxAttempts = 3,
            TimeoutSeconds = 10,
            CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime
        };
        seed.AddRange(consumer, endpoint);
        if (tenantAdmin)
        {
            var member = await seed.TenantUsers.SingleOrDefaultAsync(row =>
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
                    JoinedAt = fixture.Clock.GetUtcNow().UtcDateTime
                };
                seed.TenantUsers.Add(member);
            }
            seed.TenantUserRoleGrants.Add(new TenantUserRoleGrant
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
        await seed.SaveChangesAsync();
        return (consumer, endpoint);
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
            Context.Request.Path = "/human-webhook-preparation";
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
        internal Effect Decision { get; init; } = Effect.Allow;
        internal bool Unavailable { get; set; }

        internal Transport(ExploreDbContext owner)
        {
            Factory.GetOrCreate(Arg.Any<string>()).Returns(Client);
            Client.CheckResourcesAsync(Arg.Any<Cerbos.Sdk.Builder.CheckResourcesRequest>(), Arg.Any<Metadata>())
                .Returns(call =>
                {
                    if (owner.Database.CurrentTransaction is not null)
                        throw new InvalidOperationException("PDP evaluation under source locks is forbidden.");
                    if (Unavailable)
                        throw new RpcException(new Status(StatusCode.Unavailable, "Unavailable"));
                    var sent = call.ArgAt<Cerbos.Sdk.Builder.CheckResourcesRequest>(0).ToCheckResourcesRequest().Clone();
                    Requests.Add(sent);
                    var response = new Cerbos.Api.V1.Response.CheckResourcesResponse { RequestId = sent.RequestId };
                    var result = new Cerbos.Api.V1.Response.CheckResourcesResponse.Types.ResultEntry
                    {
                        Resource = new() { Kind = sent.Resources.Single().Resource.Kind, Id = sent.Resources.Single().Resource.Id }
                    };
                    result.Actions.Add(sent.Resources.Single().Actions.Single(), Decision);
                    response.Results.Add(result);
                    return new Cerbos.Sdk.Response.CheckResourcesResponse(response);
                });
        }
    }
}
