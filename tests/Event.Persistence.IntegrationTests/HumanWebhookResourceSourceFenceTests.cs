using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Domain;
using Explore.Infrastructure.Services;
using Explore.Persistence.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests;

public sealed class HumanWebhookResourceSourceFenceTests
{
    [Test]
    public async Task EndpointOwnerReplacementIsPersistedAndNeverComesFromTrackedOrAuditIdentity()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var consumer = await SeedConsumerAsync(fixture);
        var endpoint = await SeedEndpointAsync(fixture, consumer);
        var replacement = await SeedConsumerAsync(fixture, userOwned: true);
        var (subject, forwarding, _) = await HumanSupportAccessSourceFenceTests.CaptureAsync(fixture, null);
        var target = HumanWebhookResourceTarget.From(new ArchiveWebhookEndpointCommand { EndpointId = endpoint.Id });
        var source = new HumanWebhookResourceSourceFence(fixture.Context, subject, target, PolicySourceSetPurpose.RuntimeTenant);
        var sets = new PolicySourceSetFence(fixture.Context, PolicySourceSetPurpose.RuntimeTenant);
        var principal = new HumanPrincipalSourceFence(fixture.Context, subject, PolicySourceSetPurpose.RuntimeTenant);
        var support = new HumanSupportAccessSourceFence(fixture.Context, subject, forwarding, PolicySourceSetPurpose.RuntimeTenant);
        var observed = await source.ObserveAsync();
        await using var transaction = await fixture.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var s = await sets.AcquireAsync();
        var p = await principal.AcquireAsync(s);
        var held = await source.AcquireAsync(s, p, await support.AcquireAsync(s, p));
        endpoint.ConsumerId = replacement.Id;
        await Assert.That((await held.ValidateAsync()).Endpoints.Single().ConsumerId).IsEqualTo(consumer.Id);
        await fixture.Context.SaveChangesAsync();
        var final = await held.ValidateAsync();
        await Assert.That(final.Endpoints.Single().ConsumerId).IsEqualTo(replacement.Id);
        var projected = HumanWebhookResourceProjection.Resource(final)!;
        await Assert.That(((WebhookOwnershipAuthorizationFacts)projected.Facts).UserId).IsEqualTo(fixture.UserId);
        await Assert.That(observed.Consumers.Single().Id).IsEqualTo(consumer.Id);
        await Assert.That(held.Facts.Consumers.Single().Id).IsEqualTo(consumer.Id);
        await transaction.RollbackAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => held.ValidateAsync());
        await using var replacementOwner = await fixture.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.ThrowsAsync<InvalidOperationException>(() => held.ValidateAsync());
        await replacementOwner.RollbackAsync();
    }

    [Test]
    public async Task MissingEndpointRangeExcludesWritersAndSeesOwnInsertion()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var consumer = await SeedConsumerAsync(fixture);
        var endpoint = Endpoint(consumer);
        var (subject, forwarding, _) = await HumanSupportAccessSourceFenceTests.CaptureAsync(fixture, null);
        var source = new HumanWebhookResourceSourceFence(fixture.Context, subject,
            HumanWebhookResourceTarget.From(new ArchiveWebhookEndpointCommand { EndpointId = endpoint.Id }),
            PolicySourceSetPurpose.RuntimeTenant);
        var sets = new PolicySourceSetFence(fixture.Context, PolicySourceSetPurpose.RuntimeTenant);
        var principal = new HumanPrincipalSourceFence(fixture.Context, subject, PolicySourceSetPurpose.RuntimeTenant);
        var support = new HumanSupportAccessSourceFence(fixture.Context, subject, forwarding, PolicySourceSetPurpose.RuntimeTenant);
        await using var owner = await fixture.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var s = await sets.AcquireAsync();
        var p = await principal.AcquireAsync(s);
        var held = await source.AcquireAsync(s, p, await support.AcquireAsync(s, p));
        await Assert.That(HumanWebhookResourceProjection.Resource(held.Facts)).IsNull();
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ObserveAsync());
        await using var writer = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = fixture.DatabasePath,
            Pooling = false
        }.ToString());
        await writer.OpenAsync();
        await Assert.That(SQLitePCL.raw.sqlite3_exec(writer.Handle, "BEGIN IMMEDIATE")).IsEqualTo(SQLitePCL.raw.SQLITE_BUSY);
        fixture.Context.WebhookEndpoints.Add(endpoint);
        await fixture.Context.SaveChangesAsync();
        await Assert.That(HumanWebhookResourceProjection.Resource(await held.ValidateAsync())!.ResourceId)
            .IsEqualTo(endpoint.Id.ToString("D"));
        await Assert.That(held.Facts.Endpoints.Length).IsEqualTo(0);
        await owner.RollbackAsync();
    }

    [Test]
    public async Task ExactAttemptRetainsMessageEndpointAndTargetLineageAndTerminalState()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var consumer = await SeedConsumerAsync(fixture);
        var endpoint = await SeedEndpointAsync(fixture, consumer);
        var now = DateTime.UtcNow;
        var message = WebhookMessage.Create(fixture.TenantId, "test.source", Guid.CreateVersion7().ToString("D"),
            "event", Guid.CreateVersion7(), consumer.Id, "{}"u8, "application/json", "identity", now,
            now.AddDays(1), now);
        var plan = WebhookDeliveryPlanSnapshot.Create(fixture.TenantId, message.Id, consumer.Id,
            WebhookProviderMode.Local, "1", "1", "source", "1", now.AddDays(1), now.AddDays(2),
            now.AddDays(3), now.AddDays(2), now.AddDays(2), now);
        var local = WebhookLocalTargetSnapshot.Create(plan, endpoint, 1, now, null, now);
        var attempt = new WebhookDeliveryAttempt
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            MessageId = message.Id,
            EndpointId = endpoint.Id,
            AttemptNumber = 1,
            Outcome = WebhookDeliveryAttemptOutcome.Failed,
            ScheduledAt = now,
            CreatedAt = now
        };
        fixture.Context.AddRange(message, plan, local, attempt);
        await fixture.Context.SaveChangesAsync();
        var (subject, _, _) = await HumanSupportAccessSourceFenceTests.CaptureAsync(fixture, null);
        var source = new HumanWebhookResourceSourceFence(fixture.Context, subject,
            HumanWebhookResourceTarget.From(new RetryWebhookDeliveryAttemptCommand { AttemptId = attempt.Id }),
            PolicySourceSetPurpose.RuntimeTenant);
        var facts = await source.ObserveAsync();
        await Assert.That(HumanWebhookResourceProjection.Resource(facts)!.ResourceId).IsEqualTo(attempt.Id.ToString("D"));
        await Assert.That(facts.LocalTargets.Single().Id).IsEqualTo(local.Id);
        await Assert.That(facts.Messages.Single().HasPayload).IsTrue();
        await fixture.Context.WebhookDeliveryAttempts.Where(row => row.Id == attempt.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.OutcomeId, (int)WebhookDeliveryAttemptOutcome.Succeeded));
        await Assert.That((await source.ObserveAsync()).Attempts.Single().OutcomeId)
            .IsEqualTo((int)WebhookDeliveryAttemptOutcome.Succeeded);
        var other = await SeedEndpointAsync(fixture, consumer);
        await fixture.Context.WebhookDeliveryAttempts.Where(row => row.Id == attempt.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.EndpointId, other.Id));
        await Assert.That(HumanWebhookResourceProjection.Resource(await source.ObserveAsync())).IsNull();
    }

    [Test]
    public async Task BulkReplayRemainsTenantScopedWhilePortalKeepsOriginalOwnerAttributes()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var consumer = await SeedConsumerAsync(fixture, userOwned: true);
        var (subject, _, _) = await HumanSupportAccessSourceFenceTests.CaptureAsync(fixture, null);
        var bulk = new HumanWebhookResourceSourceFence(fixture.Context, subject,
            HumanWebhookResourceTarget.From(new ScheduleWebhookBulkReplayCommand
            {
                TenantId = fixture.TenantId,
                ActorUserId = Guid.CreateVersion7(),
                OperationKey = Guid.CreateVersion7(),
                WebhookConsumerId = consumer.Id,
                ReasonCode = "source"
            }), PolicySourceSetPurpose.RuntimeTenant);
        var portal = new HumanWebhookResourceSourceFence(fixture.Context, subject,
            HumanWebhookResourceTarget.From(new OpenSvixAppPortalCommand { ConsumerId = consumer.Id, SessionId = "not-authority" }),
            PolicySourceSetPurpose.RuntimeTenant);
        var bulkResource = HumanWebhookResourceProjection.Resource(await bulk.ObserveAsync())!;
        await Assert.That(bulkResource.Facts is TenantScopedAuthorizationFacts).IsTrue();
        await Assert.That(bulkResource.Action).IsEqualTo(AuthorizationActions.Webhooks.BulkReplay);
        var portalResource = HumanWebhookResourceProjection.Resource(await portal.ObserveAsync())!;
        await Assert.That(((WebhookOwnershipAuthorizationFacts)portalResource.Facts).UserId).IsEqualTo(fixture.UserId);
        await Assert.That(portalResource.Action).IsEqualTo(AuthorizationActions.Webhooks.OpenProviderPortal);
        await Assert.That(HumanWebhookResourceProjection.PortalConsumer(await portal.ObserveAsync())!.ConsumerId)
            .IsEqualTo(consumer.Id);
    }

    [Test]
    public async Task OwnPersistedBulkCancellationIsFreshWithoutChangingTheOriginalPolicyScope()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var consumer = await SeedConsumerAsync(fixture);
        var now = DateTime.UtcNow;
        var operation = WebhookBulkReplayOperation.Create(fixture.TenantId, Guid.CreateVersion7(),
            "sha256:" + new string('a', 64), now.AddHours(-1), now, consumer.Id, null, null, 1, "source",
            new WebhookBulkReplayPreviewSnapshot(1, 0, 0, 0, 0, 0, 0, 0, 0), now);
        fixture.Context.WebhookBulkReplayOperations.Add(operation);
        await fixture.Context.SaveChangesAsync();
        var (subject, forwarding, _) = await HumanSupportAccessSourceFenceTests.CaptureAsync(fixture, null);
        var source = new HumanWebhookResourceSourceFence(fixture.Context, subject,
            HumanWebhookResourceTarget.From(new CancelWebhookBulkReplayCommand
            {
                TenantId = fixture.TenantId,
                OperationId = operation.Id,
                ExpectedConcurrencyVersion = operation.ConcurrencyVersion,
                ReasonCode = "source",
                ActorUserId = Guid.CreateVersion7()
            }), PolicySourceSetPurpose.RuntimeTenant);
        var sets = new PolicySourceSetFence(fixture.Context, PolicySourceSetPurpose.RuntimeTenant);
        var principal = new HumanPrincipalSourceFence(fixture.Context, subject, PolicySourceSetPurpose.RuntimeTenant);
        var support = new HumanSupportAccessSourceFence(fixture.Context, subject, forwarding, PolicySourceSetPurpose.RuntimeTenant);
        await using var owner = await fixture.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var s = await sets.AcquireAsync();
        var p = await principal.AcquireAsync(s);
        var held = await source.AcquireAsync(s, p, await support.AcquireAsync(s, p));
        operation.Cancel("source", now);
        await fixture.Context.SaveChangesAsync();
        var final = await held.ValidateAsync();
        await Assert.That(final.BulkReplays.Single().StatusId).IsEqualTo((int)WebhookBulkReplayStatus.Cancelled);
        await Assert.That(final.BulkReplays.Single().CancelledAt).IsNotNull();
        await Assert.That(held.Facts.BulkReplays.Single().StatusId).IsEqualTo((int)WebhookBulkReplayStatus.Queued);
        await Assert.That(HumanWebhookResourceProjection.Resource(final)!.Facts is TenantScopedAuthorizationFacts).IsTrue();
        await owner.RollbackAsync();
    }

    [Test]
    public async Task CreationRequiresCurrentPersistedMembershipAndRejectsRequestedTenantSubstitution()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var (subject, _, _) = await HumanSupportAccessSourceFenceTests.CaptureAsync(fixture, null);
        var source = new HumanWebhookResourceSourceFence(fixture.Context, subject,
            HumanWebhookResourceTarget.From(new CreateWebhookConsumerCommand
            {
                ConsumerKindId = (int)WebhookConsumerKind.User,
                Name = "source",
                ProviderModeId = (int)WebhookProviderMode.Local
            }), PolicySourceSetPurpose.RuntimeTenant);
        await Assert.That(((WebhookOwnershipAuthorizationFacts)HumanWebhookResourceProjection.Resource(
            await source.ObserveAsync())!.Facts).UserId).IsEqualTo(fixture.UserId);
        await fixture.Context.TenantUsers.Where(row => row.TenantId == fixture.TenantId && row.UserId == fixture.UserId)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
        await Assert.That(HumanWebhookResourceProjection.Resource(await source.ObserveAsync())).IsNull();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Task.FromResult(new HumanWebhookResourceSourceFence(
            fixture.Context, subject, HumanWebhookResourceTarget.From(new ScheduleWebhookBulkReplayCommand
            {
                TenantId = Guid.CreateVersion7(),
                OperationKey = Guid.CreateVersion7(),
                ReasonCode = "source"
            }), PolicySourceSetPurpose.RuntimeTenant)));
    }

    internal static async Task<WebhookConsumer> SeedConsumerAsync(EventVisitorCapabilitySqliteFixture fixture, bool userOwned = false)
    {
        var ownership = WebhookOwnershipScope.Create(userOwned ? WebhookConsumerKind.User : WebhookConsumerKind.Tenant,
            fixture.TenantId, null, null, null, userOwned ? fixture.UserId : null);
        var consumer = WebhookConsumer.Create(ownership, $"source-{Guid.CreateVersion7():N}", WebhookProviderMode.Local, DateTime.UtcNow);
        fixture.Context.WebhookConsumers.Add(consumer);
        await fixture.Context.SaveChangesAsync();
        return consumer;
    }

    private static WebhookEndpoint Endpoint(WebhookConsumer consumer) => new()
    {
        Id = Guid.CreateVersion7(),
        ConsumerId = consumer.Id,
        TenantId = consumer.TenantId,
        InstanceId = consumer.InstanceId,
        Url = $"https://example.invalid/{Guid.CreateVersion7():N}",
        SecretRef = "fixture-reference",
        SecretVersion = 1,
        SecretActivatedAt = DateTime.UtcNow,
        ConfigurationVersion = 1,
        Status = WebhookEndpointStatus.Active,
        MaxAttempts = 3,
        TimeoutSeconds = 10,
        CreatedAt = DateTime.UtcNow
    };

    private static async Task<WebhookEndpoint> SeedEndpointAsync(EventVisitorCapabilitySqliteFixture fixture, WebhookConsumer consumer)
    {
        var endpoint = Endpoint(consumer);
        fixture.Context.WebhookEndpoints.Add(endpoint);
        await fixture.Context.SaveChangesAsync();
        return endpoint;
    }
}
