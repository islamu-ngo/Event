using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Webhooks.Handlers.Commands;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanWebhookPreparationTests
{
    [Test]
    [Arguments("local", false)]
    [Arguments("cerbos", false)]
    [Arguments("local", true)]
    [Arguments("cerbos", true)]
    public async Task AcquisitionCannotBorrowAnExtendedSupportExpiry(string provider, bool expireOriginal)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var (_, endpoint) = await SeedAsync(fixture);
        var (supportId, expiresAt) = await StartHorizonSupportAsync(fixture, request);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context);
        var command = new ArchiveWebhookEndpointCommand { EndpointId = endpoint.Id };
        var prepared = await CreatePreparation(request.Services, fixture.Clock, provider, transport).PrepareAsync(command);
        await using (var writer = fixture.Native.CreateDatabase())
            await writer.SupportAccessSessions.Where(row => row.Id == supportId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAtUtc, expiresAt.AddMinutes(10)));
        if (expireOriginal) fixture.Clock.Advance(expiresAt - fixture.Clock.GetUtcNow());

        bool acquired = false;
        var execution = request.Services.GetRequiredService<ISerializableAttemptOwner>().ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(command, token);
            acquired = true;
            await request.Services.GetRequiredService<IWebhookEndpointRepository>()
                .ArchiveAsync(endpoint.TenantId, endpoint.Id, fixture.Clock.GetUtcNow().UtcDateTime, token);
            return true;
        });
        if (expireOriginal) await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        else await Assert.That(await execution).IsTrue();
        await Assert.That(acquired).IsEqualTo(!expireOriginal);
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That((await observer.WebhookEndpoints.SingleAsync(row => row.Id == endpoint.Id)).Status)
            .IsEqualTo(expireOriginal ? WebhookEndpointStatus.Active : WebhookEndpointStatus.Archived);
        await Assert.That((await observer.SupportAccessSessions.SingleAsync(row => row.Id == supportId)).ExpiresAtUtc)
            .IsEqualTo(expiresAt.AddMinutes(10));
        await Assert.That(transport.Requests.Count).IsEqualTo(provider == "cerbos" ? 1 : 0);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("portal", "active")]
    [Arguments("repair", "active")]
    [Arguments("portal", "expired_original")]
    [Arguments("repair", "expired_original")]
    [Arguments("portal", "fresh_read_only")]
    [Arguments("repair", "fresh_read_only")]
    public async Task OrdinaryRemoteCheckpointsIntersectOriginalAndFreshSupport(string operation, string change)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var (consumer, _) = await SeedAsync(fixture, tenantAdmin: true);
        var (supportId, expiresAt) = await StartHorizonSupportAsync(fixture, request);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context);
        var preparation = CreatePreparation(request.Services, fixture.Clock, "cerbos", transport);
        Func<Task> checkpoint;
        if (operation == "portal")
        {
            var command = new OpenSvixAppPortalCommand { ConsumerId = consumer.Id, SessionId = "original" };
            var prepared = await preparation.PreparePortalAsync(command);
            checkpoint = () => prepared.ValidateAsync(command);
        }
        else
        {
            var command = new RepairWebhookProviderBindingCommand
            {
                ConsumerId = consumer.Id,
                ExternalApplicationId = "application",
                ReasonCode = "maintenance"
            };
            var prepared = await preparation.PrepareRepairAsync(command);
            checkpoint = () => prepared.ValidateAsync(command);
        }
        await using (var writer = fixture.Native.CreateDatabase())
        {
            await writer.SupportAccessSessions.Where(row => row.Id == supportId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAtUtc, expiresAt.AddMinutes(10)));
            if (change == "fresh_read_only")
                await writer.SupportAccessSessions.Where(row => row.Id == supportId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ModeId, (int)SupportAccessModeEnum.ReadOnly));
        }
        if (change == "expired_original") fixture.Clock.Advance(expiresAt - fixture.Clock.GetUtcNow());
        if (change == "active") await checkpoint();
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(checkpoint);
        await Assert.That(transport.Requests.Count).IsEqualTo(1);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("local")]
    [Arguments("cerbos")]
    public async Task OriginalReadOnlySupportCannotBeUpgradedByTheMutation(string provider)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var (_, endpoint) = await SeedAsync(fixture);
        var (supportId, _) = await StartHorizonSupportAsync(fixture, request, SupportAccessModeEnum.ReadOnly);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context);
        bool mutationEntered = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            CreatePreparation(request.Services, fixture.Clock, provider, transport).ExecuteAsync(
                new ArchiveWebhookEndpointCommand { EndpointId = endpoint.Id }, async token =>
                {
                    mutationEntered = true;
                    await context.SupportAccessSessions.Where(row => row.Id == supportId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.ModeId,
                            (int)SupportAccessModeEnum.Write), token);
                    return true;
                }));
        await Assert.That(mutationEntered).IsFalse();
        await Assert.That(transport.Requests.Count).IsEqualTo(0);
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That((await observer.SupportAccessSessions.SingleAsync(row => row.Id == supportId)).ModeId)
            .IsEqualTo((int)SupportAccessModeEnum.ReadOnly);
        await Assert.That((await observer.WebhookEndpoints.SingleAsync(row => row.Id == endpoint.Id)).Status)
            .IsEqualTo(WebhookEndpointStatus.Active);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("active")]
    [Arguments("rollback_gap")]
    [Arguments("evidence_final")]
    public async Task CancellationEvidenceKeepsTheFirstSupportHorizonAcrossAcknowledgedRollback(string boundary)
    {
        var interceptor = new CancellationAuditInterceptor();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: interceptor);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var command = await SeedCancellationAsync(fixture, "version");
        var (supportId, expiresAt) = await StartHorizonSupportAsync(fixture, request);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var owner = new CancellationBoundary((IOriginalOwnerSettlementBoundary)
            request.Services.GetRequiredService<IUnitOfWork>())
        {
            AfterRollback = async () =>
            {
                await Assert.That(context.Database.CurrentTransaction).IsNull();
                await using var writer = fixture.Native.CreateDatabase();
                await writer.SupportAccessSessions.Where(row => row.Id == supportId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAtUtc, expiresAt.AddMinutes(10)));
                if (boundary == "rollback_gap") fixture.Clock.Advance(expiresAt - fixture.Clock.GetUtcNow());
            }
        };
        if (boundary == "evidence_final")
            interceptor.AfterAudit = database =>
            {
                request.Services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async token =>
                {
                    await database.WebhookAuditEvents.AsNoTracking()
                        .SingleAsync(row => row.TargetId == command.OperationId, token);
                    fixture.Clock.Advance(expiresAt - fixture.Clock.GetUtcNow());
                }, _ => { });
                return Task.CompletedTask;
            };
        var transport = new Transport(context);
        var handler = ActivatorUtilities.CreateInstance<CancelWebhookBulkReplayCommandHandler>(request.Services,
            CreatePreparation(request.Services, fixture.Clock, "cerbos", transport), owner);
        if (boundary == "active")
            await Assert.That((await handler.ExecuteAsync(command, CancellationToken.None)).FailureCode)
                .IsEqualTo("webhook_bulk_replay_concurrency_conflict");
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.ExecuteAsync(command, CancellationToken.None));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.WebhookAuditEvents.CountAsync(row => row.TargetId == command.OperationId))
            .IsEqualTo(boundary == "active" ? 1 : 0);
        await Assert.That((await observer.WebhookBulkReplayOperations.SingleAsync(row => row.Id == command.OperationId)).Status)
            .IsEqualTo(WebhookBulkReplayStatus.Queued);
        await Assert.That(owner.Dispositions[0]).IsEqualTo(OriginalOwnerDisposition.WholeTransactionRolledBack);
        await Assert.That(owner.Dispositions.Count).IsEqualTo(boundary == "active" ? 2 : 1);
        await Assert.That(owner.Consumptions).IsEqualTo(boundary == "active" ? 2 : 1);
        await Assert.That(interceptor.SavedAudits).IsEqualTo(boundary == "rollback_gap" ? 0 : 1);
        await Assert.That(transport.Requests.Count).IsEqualTo(boundary == "rollback_gap" ? 1 : 2);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    private static async Task<(Guid Id, DateTimeOffset ExpiresAt)> StartHorizonSupportAsync(
        LocalIdentityLifecycleHttpFixture fixture, Request request, SupportAccessModeEnum mode = SupportAccessModeEnum.Write)
    {
        await using var database = fixture.Native.CreateDatabase();
        foreach (var key in new[] { GovernanceSettingKeys.SupportAccess.Enabled, GovernanceSettingKeys.SupportAccess.AllowWriteMode })
        {
            var setting = await database.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
            if (setting is null) database.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "true" });
            else setting.Value = "true";
        }
        var expiresAt = fixture.Clock.GetUtcNow().AddMinutes(5);
        var session = SupportAccessSession.Start(fixture.Binding.LocalSubjectId, PlatformDefaults.DefaultTenantId,
            mode, "maintenance", "Webhook support horizon", "fixture-ticket",
            fixture.Clock.GetUtcNow().AddMinutes(-1), expiresAt);
        database.SupportAccessSessions.Add(session);
        await database.SaveChangesAsync();
        request.Context.Request.Headers[SupportAccessHeaderNames.SessionId] = session.Id.ToString("D");
        var support = await request.Services.GetRequiredService<ISupportAccessSessionService>().GetCurrentAsync();
        await Assert.That(support.IsActive).IsTrue();
        await Assert.That(support.SessionId).IsEqualTo(session.Id);
        var lifetime = (await request.Services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
            .ObserveAsync(request.Context.User, CancellationToken.None))!;
        await Assert.That(lifetime.IsValidAt(expiresAt)).IsTrue();
        return (session.Id, expiresAt);
    }
}
