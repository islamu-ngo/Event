using System.Data;
using System.Data.Common;
using System.Security.Claims;
using Cerbos.Api.V1.Effect;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.Webhooks.Handlers.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.Extensions;

namespace Event.Api.IntegrationTests.Authentication;

/// <summary>
/// Consumer handlers entered with genuine Local sign-in credentials and the original physical
/// owner. Shares only the existing authenticated request and real preparation construction;
/// no source, lease, successful authority or rollback receipt is substituted.
/// </summary>
public sealed partial class HumanWebhookPreparationTests
{
    [Test]
    [Arguments("create", false)]
    [Arguments("update", false)]
    [Arguments("create", true)]
    [Arguments("update", true)]
    public async Task CanonicalConsumerAuditCannotBeReplacedByOrdinaryClaimAttribution(
        string operation, bool missingAttribution)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var consumer = await SeedConsumerMutationAsync(fixture);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var otherActor = Guid.CreateVersion7();
        // Only the ordinary audit adapter sees conflicting claims. The original signed
        // requester, canonical resolution and P/route/H/W preparation are unchanged.
        var attribution = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, otherActor.ToString("D"))], "audit-attribution"))
        };
        context.CurrentUserService = missingAttribution ? null
            : new CurrentUserService(new ConsumerAuditContextAccessor { HttpContext = attribution });
        if (!missingAttribution)
            await Assert.That(context.CurrentUserService!.UserId).IsEqualTo(otherActor);
        var preparation = CreatePreparation(request.Services, fixture.Clock, "local", new Transport(context));
        await Assert.That((await ExecuteConsumerMutationAsync(
            request.Services, preparation, operation, consumer)).IsSuccess).IsTrue();
        await AssertConsumerMutationAsync(fixture, consumer, operation, committed: true);
        await Assert.That(context.HumanCommitAuditSubject!.PrincipalId).IsEqualTo(fixture.Binding.LocalSubjectId);
        await context.DisposeAsync();
        await Assert.That(context.HumanCommitAuditSubject).IsNull();
        await Assert.That(context.HasHumanCommitContextLease).IsFalse();
    }

    private sealed class ConsumerAuditContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    [Test]
    public async Task CreatedConsumerOwnLineageDriftAfterAuditCannotCommitOutsideCreationSelector()
    {
        var interceptor = new ConsumerMutationSaveBoundary();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: interceptor);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var consumer = await SeedConsumerMutationAsync(fixture);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        interceptor.AfterAudit = async (database, token) =>
        {
            var product = database.ChangeTracker.Entries<WebhookConsumer>().Single().Entity;
            await database.WebhookConsumers.Where(row => row.Id == product.Id)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(row => row.ConsumerKindId, (int)WebhookConsumerKind.Tenant)
                    .SetProperty(row => row.OwnerUserId, (Guid?)null), token);
        };
        var preparation = CreatePreparation(request.Services, fixture.Clock, "local", new Transport(context));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            ExecuteConsumerMutationAsync(request.Services, preparation, "create", consumer));
        await Assert.That(interceptor.SavedAudits).IsEqualTo(1);
        await AssertConsumerMutationAsync(fixture, consumer, "create", committed: false);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("create", "local", false)]
    [Arguments("update", "local", false)]
    [Arguments("create", "local", true)]
    [Arguments("update", "local", true)]
    [Arguments("create", "cerbos", false)]
    [Arguments("update", "cerbos", false)]
    [Arguments("create", "cerbos", true)]
    [Arguments("update", "cerbos", true)]
    public async Task ConsumerMutationRequiresOriginalSelectedDecision(string operation, string provider, bool deny)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var consumer = await SeedConsumerMutationAsync(fixture, tenantOwned: provider == "local" && deny);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context) { Decision = deny ? Effect.Deny : Effect.Allow };
        var preparation = CreatePreparation(request.Services, fixture.Clock, provider, transport);
        var execution = ExecuteConsumerMutationAsync(request.Services, preparation, operation, consumer);
        if (deny)
            await Assert.ThrowsAsync<AuthorizationException>(() => execution);
        else
            await Assert.That((await execution).IsSuccess).IsTrue();
        await AssertConsumerMutationAsync(fixture, consumer, operation, committed: !deny);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(transport.Requests.Count).IsEqualTo(provider == "cerbos" ? 1 : 0);
        if (provider == "cerbos")
        {
            var sent = transport.Requests.Single();
            await Assert.That(sent.Principal.Id).IsEqualTo(fixture.Binding.LocalSubjectId.ToString("D"));
            var resource = sent.Resources.Single();
            await Assert.That(resource.Resource.Id)
                .IsEqualTo((operation == "create" ? consumer.OwnerId : consumer.Id).ToString("D"));
            await Assert.That(resource.Resource.Attr["userId"].StringValue)
                .IsEqualTo(fixture.Binding.LocalSubjectId.ToString("D"));
            await Assert.That(resource.Actions.Single()).IsEqualTo(operation == "create"
                ? AuthorizationActions.Webhooks.Create : AuthorizationActions.Webhooks.Update);
        }
    }

    [Test]
    [Arguments("create")]
    [Arguments("update")]
    public async Task ConsumerSelectedProviderOutageCannotFallThroughToOrdinaryOwner(string operation)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var consumer = await SeedConsumerMutationAsync(fixture);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var preparation = CreatePreparation(request.Services, fixture.Clock, "cerbos",
            new Transport(context) { Unavailable = true });
        await Assert.ThrowsAsync<AuthorizationProviderUnavailableException>(() =>
            ExecuteConsumerMutationAsync(request.Services, preparation, operation, consumer));
        await AssertConsumerMutationAsync(fixture, consumer, operation, committed: false);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("create")]
    [Arguments("update")]
    public async Task ConsumerAcquisitionRejectsDriftAfterOriginalProviderDecision(string operation)
    {
        var interceptor = new ConsumerMutationSaveBoundary();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: interceptor);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var consumer = await SeedConsumerMutationAsync(fixture);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context);
        transport.Client.Configure().CheckResourcesAsync(Arg.Any<Cerbos.Sdk.Builder.CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns(async call =>
            {
                await Assert.That(context.Database.CurrentTransaction).IsNull();
                var sent = call.ArgAt<Cerbos.Sdk.Builder.CheckResourcesRequest>(0).ToCheckResourcesRequest().Clone();
                transport.Requests.Add(sent);
                // Commit real source drift while the remote decision is outstanding, before
                // acquisition. No timing race or fabricated lease stands in for the source.
                await using var writer = fixture.Native.CreateDatabase();
                if (operation == "create")
                    await writer.TenantUsers.Where(row => row.TenantId == consumer.TenantId
                            && row.UserId == fixture.Binding.LocalSubjectId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
                else
                    await writer.WebhookConsumers.Where(row => row.Id == consumer.Id)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.ConfigurationVersion, 2));
                var response = new Cerbos.Api.V1.Response.CheckResourcesResponse { RequestId = sent.RequestId };
                var result = new Cerbos.Api.V1.Response.CheckResourcesResponse.Types.ResultEntry
                {
                    Resource = new() { Kind = sent.Resources.Single().Resource.Kind, Id = sent.Resources.Single().Resource.Id }
                };
                result.Actions.Add(sent.Resources.Single().Actions.Single(), Effect.Allow);
                response.Results.Add(result);
                return new Cerbos.Sdk.Response.CheckResourcesResponse(response);
            });
        var preparation = CreatePreparation(request.Services, fixture.Clock, "cerbos", transport);
        interceptor.AfterAudit = (_, _) => throw new InvalidOperationException("Business audit must not be reached.");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            ExecuteConsumerMutationAsync(request.Services, preparation, operation, consumer));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.WebhookConsumers.AsNoTracking()
            .CountAsync(row => row.Name == consumer.Name + "-created")).IsEqualTo(0);
        await Assert.That(await observer.Set<WebhookAuditEvent>().AsNoTracking()
            .CountAsync(row => row.EffectiveScopeId == consumer.OwnerId)).IsEqualTo(0);
        var original = await observer.WebhookConsumers.AsNoTracking().SingleAsync(row => row.Id == consumer.Id);
        await Assert.That(original.ProviderMode).IsEqualTo(WebhookProviderMode.DryRun);
        await Assert.That(original.ConfigurationVersion).IsEqualTo(operation == "update" ? 2 : 1);
        await Assert.That(interceptor.SavedAudits).IsEqualTo(0);
        await Assert.That(transport.Requests.Count).IsEqualTo(1);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("create", "grant")]
    [Arguments("update", "grant")]
    [Arguments("create", "credential")]
    [Arguments("update", "credential")]
    [Arguments("create", "expiry_after_reads")]
    [Arguments("update", "expiry_after_reads")]
    [Arguments("create", "route")]
    [Arguments("update", "route")]
    [Arguments("create", "owner_membership")]
    [Arguments("update", "wrong_transition")]
    public async Task ConsumerFinalAdmissionRollsBackPersistedBusinessAndAudit(string operation, string drift)
    {
        var interceptor = new ConsumerMutationSaveBoundary();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: interceptor);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var consumer = await SeedConsumerMutationAsync(fixture, tenantOwned: drift == "grant",
            administrator: drift == "grant");
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context);
        var preparation = CreatePreparation(request.Services, fixture.Clock, drift == "grant" ? "local" : "cerbos", transport);
        var lifetime = (await request.Services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
            .ObserveAsync(request.Context.User, CancellationToken.None))!;
        interceptor.AfterAudit = async (database, token) =>
        {
            await Assert.That(database).IsSameReferenceAs(context);
            await Assert.That(database.Database.CurrentTransaction!.GetDbTransaction().IsolationLevel)
                .IsEqualTo(IsolationLevel.Serializable);
            if (drift == "grant")
                await database.TenantUserRoleGrants.Where(row => row.TenantId == consumer.TenantId
                        && row.TenantUser.UserId == fixture.Binding.LocalSubjectId
                        && row.RoleId == (int)RoleEnum.TenantAdmin)
                    .ExecuteDeleteAsync(token);
            if (drift == "credential")
                await database.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ProviderKey, Guid.NewGuid().ToString()), token);
            if (drift == "owner_membership")
                await database.TenantUsers.Where(row => row.TenantId == consumer.TenantId
                        && row.UserId == fixture.Binding.LocalSubjectId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true), token);
            if (drift == "wrong_transition")
                await database.WebhookConsumers.Where(row => row.Id == consumer.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.StatusId,
                        (int)WebhookConsumerStatus.Archived), token);
            if (drift == "route")
            {
                foreach (var item in new[]
                {
                    (GovernanceSettingKeys.Cerbos.TenantCustomizationEnabled, "true"),
                    (GovernanceSettingKeys.Cerbos.Mode, "\"custom_endpoint\""),
                    (GovernanceSettingKeys.Cerbos.CustomEndpoint, "\"http://changed.consumer.test:3593\"")
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
            if (drift == "expiry_after_reads")
                request.Services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
                {
                    await database.WebhookConsumers.AsNoTracking().SingleAsync(row => row.Id == consumer.Id, finalToken);
                    fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1)
                        - fixture.Clock.GetUtcNow());
                }, _ => { });
        };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            ExecuteConsumerMutationAsync(request.Services, preparation, operation, consumer));
        await Assert.That(interceptor.SavedAudits).IsEqualTo(1);
        await AssertConsumerMutationAsync(fixture, consumer, operation, committed: false);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(transport.Requests.Count).IsEqualTo(drift == "grant" ? 0 : 1);
    }

    [Test]
    [Arguments("create")]
    [Arguments("update")]
    public async Task ConsumerWholeOwnerRetryRetainsIdentityAndOriginalBusinessTime(string operation)
    {
        var interceptor = new ConsumerMutationSaveBoundary();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: interceptor);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var consumer = await SeedConsumerMutationAsync(fixture);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context);
        var preparation = CreatePreparation(request.Services, fixture.Clock, "cerbos", transport);
        var originalTime = fixture.Clock.GetUtcNow().UtcDateTime;
        var products = new List<(Guid Id, DateTime Time, Guid? Actor)>();
        var transactions = new List<DbTransaction>();
        interceptor.AfterAudit = (database, _) =>
        {
            var product = database.ChangeTracker.Entries<WebhookConsumer>().Single().Entity;
            products.Add((product.Id, operation == "create" ? product.CreatedAt : product.UpdatedAt!.Value,
                operation == "create" ? product.CreatedBy : product.UpdatedBy));
            transactions.Add(database.Database.CurrentTransaction!.GetDbTransaction());
            if (products.Count == 1)
            {
                fixture.Clock.Advance(TimeSpan.FromMinutes(1));
                // The real owner must roll back real rows and issue its own acknowledgement.
                throw new SqliteException("Injected conflict after consumer and audit persistence.", 5);
            }
            return Task.CompletedTask;
        };
        var result = await ExecuteConsumerMutationAsync(request.Services, preparation, operation, consumer);
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(products.Count).IsEqualTo(2);
        await Assert.That(products[1]).IsEqualTo(products[0]);
        await Assert.That(products[1].Id).IsEqualTo(result.Id);
        await Assert.That(products[1].Time).IsEqualTo(originalTime);
        await Assert.That(products[1].Actor).IsEqualTo(fixture.Binding.LocalSubjectId);
        await Assert.That(ReferenceEquals(transactions[0], transactions[1])).IsFalse();
        await Assert.That(transport.Requests.Count).IsEqualTo(2);
        await AssertConsumerMutationAsync(fixture, consumer, operation, committed: true);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("create", "active")]
    [Arguments("update", "active")]
    [Arguments("create", "revoke")]
    [Arguments("update", "revoke")]
    [Arguments("create", "expiry_after_reads")]
    [Arguments("update", "expiry_after_reads")]
    [Arguments("create", "extended_active")]
    [Arguments("update", "extended_active")]
    [Arguments("create", "extended_expiry_after_reads")]
    [Arguments("update", "extended_expiry_after_reads")]
    public async Task ConsumerMutationRetainsForwardedSupportThroughFinalAdmission(string operation, string change)
    {
        var interceptor = new ConsumerMutationSaveBoundary();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: interceptor);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var consumer = await SeedConsumerMutationAsync(fixture);
        var expiresAt = fixture.Clock.GetUtcNow().AddMinutes(5);
        Guid supportId;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            foreach (var key in new[] { GovernanceSettingKeys.SupportAccess.Enabled, GovernanceSettingKeys.SupportAccess.AllowWriteMode })
            {
                var setting = await seed.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
                if (setting is null)
                    seed.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "true" });
                else
                    setting.Value = "true";
            }
            var session = SupportAccessSession.Start(fixture.Binding.LocalSubjectId,
                PlatformDefaults.DefaultTenantId, SupportAccessModeEnum.Write, "maintenance",
                "Consumer administration", "fixture-ticket", fixture.Clock.GetUtcNow().AddMinutes(-1), expiresAt);
            seed.SupportAccessSessions.Add(session);
            await seed.SaveChangesAsync();
            supportId = session.Id;
        }
        request.Context.Request.Headers[SupportAccessHeaderNames.SessionId] = supportId.ToString("D");
        var support = await request.Services.GetRequiredService<ISupportAccessSessionService>().GetCurrentAsync();
        await Assert.That(support.IsActive).IsTrue();
        await Assert.That(support.SessionId).IsEqualTo(supportId);
        var lifetime = (await request.Services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
            .ObserveAsync(request.Context.User, CancellationToken.None))!;
        await Assert.That(lifetime.IsValidAt(expiresAt)).IsTrue();
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context);
        var preparation = CreatePreparation(request.Services, fixture.Clock, "cerbos", transport);
        interceptor.AfterAudit = async (database, token) =>
        {
            if (change == "revoke")
                await database.SupportAccessSessions.Where(row => row.Id == supportId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.StatusId,
                        (int)SupportAccessSessionStatusEnum.Revoked), token);
            if (change is "extended_active" or "extended_expiry_after_reads")
                await database.SupportAccessSessions.Where(row => row.Id == supportId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAtUtc, expiresAt.AddMinutes(10)), token);
            if (change is "expiry_after_reads" or "extended_expiry_after_reads")
                request.Services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
                {
                    await database.WebhookConsumers.AsNoTracking().SingleAsync(row => row.Id == consumer.Id, finalToken);
                    fixture.Clock.Advance(expiresAt - fixture.Clock.GetUtcNow());
                }, _ => { });
        };
        var execution = ExecuteConsumerMutationAsync(request.Services, preparation, operation, consumer);
        bool committed = change is "active" or "extended_active";
        if (committed)
            await Assert.That((await execution).IsSuccess).IsTrue();
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        await Assert.That(interceptor.SavedAudits).IsEqualTo(1);
        await AssertConsumerMutationAsync(fixture, consumer, operation, committed);
        await using var supportObserver = fixture.Native.CreateDatabase();
        await Assert.That((await supportObserver.SupportAccessSessions.SingleAsync(row => row.Id == supportId)).ExpiresAtUtc)
            .IsEqualTo(change == "extended_active" ? expiresAt.AddMinutes(10) : expiresAt);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(transport.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task CreatedConsumerCannotChangeTypedOwnershipOutsideCreationSelector()
    {
        var interceptor = new ConsumerMutationSaveBoundary();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: interceptor);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var consumer = await SeedConsumerMutationAsync(fixture);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var preparation = CreatePreparation(request.Services, fixture.Clock, "local", new Transport(context));
        interceptor.BeforeConsumer = product =>
        {
            product.ConsumerKind = WebhookConsumerKind.Tenant;
            product.OwnerUserId = null;
        };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            ExecuteConsumerMutationAsync(request.Services, preparation, "create", consumer));
        await AssertConsumerMutationAsync(fixture, consumer, "create", committed: false);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("create")]
    [Arguments("update")]
    public async Task AnonymousConsumerMutationCannotUseOrdinaryOwner(string operation)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        var consumer = await SeedConsumerMutationAsync(fixture);
        await using var request = new Request(fixture.Host.Services);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context);
        var preparation = CreatePreparation(request.Services, fixture.Clock, "cerbos", transport);
        if (operation == "create")
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                ExecuteConsumerMutationAsync(request.Services, preparation, operation, consumer));
        else
        {
            var result = await ExecuteConsumerMutationAsync(request.Services, preparation, operation, consumer);
            await Assert.That(result.FailureCode).IsEqualTo("webhook_consumer_provider_mode_actor_required");
        }
        await AssertConsumerMutationAsync(fixture, consumer, operation, committed: false);
        await Assert.That(transport.Requests.Count).IsEqualTo(0);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("duplicate", "webhook_consumer_name_conflict", 1)]
    [Arguments("stale", "webhook_consumer_configuration_conflict", 1)]
    [Arguments("unchanged", "webhook_consumer_provider_mode_unchanged", 1)]
    [Arguments("migrate", "webhook_consumer_provider_mode_pending_migration_unsupported", 0)]
    [Arguments("target", "webhook_consumer_provider_mode_local_target_required", 1)]
    [Arguments("unavailable_create", "webhook_provider_mode_capability_unavailable", 0)]
    [Arguments("unavailable_update", "webhook_consumer_provider_mode_unavailable", 1)]
    public async Task ConsumerBusinessRejectionsKeepTheirOriginalSemantics(string rejection, string code, int decisions)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var consumer = await SeedConsumerMutationAsync(fixture);
        if (rejection == "target")
        {
            await using var seed = fixture.Native.CreateDatabase();
            await seed.WebhookEndpointSubscriptions.Where(row => row.Endpoint != null && row.Endpoint.ConsumerId == consumer.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsEnabled, false));
        }
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context);
        var preparation = CreatePreparation(request.Services, fixture.Clock, "cerbos", transport);
        var operation = rejection is "duplicate" or "unavailable_create" ? "create" : "update";
        var result = await ExecuteConsumerMutationAsync(request.Services, preparation, operation, consumer, rejection);
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(code);
        await AssertConsumerMutationAsync(fixture, consumer, operation, committed: false);
        await Assert.That(transport.Requests.Count).IsEqualTo(decisions);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ConsumerModeChangePreservesUncertainPublicationSnapshots(bool acknowledge)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var consumer = await SeedConsumerMutationAsync(fixture);
        Guid publicationId, planId;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            var now = fixture.Clock.GetUtcNow().UtcDateTime;
            var message = WebhookMessage.Create(consumer.TenantId!.Value, "test.consumer-mode",
                Guid.CreateVersion7().ToString("D"), "event", Guid.CreateVersion7(), consumer.Id,
                "{}"u8, "application/json", "identity", now, now.AddDays(1), now);
            var plan = WebhookDeliveryPlanSnapshot.Create(message.TenantId, message.Id, consumer.Id,
                WebhookProviderMode.DryRun, "1", "1", "prepared", "1", now.AddDays(1), now.AddDays(2),
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
            planId = plan.Id;
        }
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context);
        var preparation = CreatePreparation(request.Services, fixture.Clock, "local", transport);
        var result = await ActivatorUtilities.CreateInstance<UpdateWebhookConsumerProviderModeCommandHandler>(
            request.Services, preparation).ExecuteAsync(new()
            {
                ConsumerId = consumer.Id,
                ProviderModeId = (int)WebhookProviderMode.Local,
                ExpectedConfigurationVersion = 1,
                PendingWorkDecisionId = (int)WebhookPendingWorkDecision.PreserveExisting,
                PendingWorkReason = "maintenance",
                AcknowledgeUncertainProviderPublications = acknowledge
            }, CancellationToken.None);
        await Assert.That(result.IsSuccess).IsEqualTo(acknowledge);
        if (!acknowledge)
            await Assert.That(result.FailureCode).IsEqualTo("webhook_consumer_provider_mode_uncertain_publications");
        await AssertConsumerMutationAsync(fixture, consumer, "update", committed: acknowledge);
        await using var observer = fixture.Native.CreateDatabase();
        var stored = await observer.WebhookProviderPublications.AsNoTracking().SingleAsync(row => row.Id == publicationId);
        await Assert.That(stored.Status).IsEqualTo(WebhookProviderPublicationStatus.ManualReconciliation);
        await Assert.That(stored.ConcurrencyVersion).IsEqualTo(1L);
        var snapshot = await observer.WebhookDeliveryPlanSnapshots.AsNoTracking().SingleAsync(row => row.Id == planId);
        await Assert.That(snapshot.ProviderMode).IsEqualTo(WebhookProviderMode.DryRun);
        await Assert.That(snapshot.ConfigurationVersion).IsEqualTo("1");
    }

    private static Task<BaseCommandResponse<Guid>> ExecuteConsumerMutationAsync(IServiceProvider services,
        IHumanWebhookMutationPreparation preparation, string operation, WebhookConsumer consumer, string? rejection = null)
    {
        if (operation == "create")
            return ActivatorUtilities.CreateInstance<CreateWebhookConsumerCommandHandler>(services, preparation)
                .ExecuteAsync(new()
                {
                    ConsumerKindId = consumer.ConsumerKindId,
                    OwnerId = consumer.OwnerId,
                    Name = rejection == "duplicate" ? consumer.Name : consumer.Name + "-created",
                    ProviderModeId = (int)(rejection == "unavailable_create" ? WebhookProviderMode.Svix : WebhookProviderMode.Local)
                }, CancellationToken.None);
        return ActivatorUtilities.CreateInstance<UpdateWebhookConsumerProviderModeCommandHandler>(services, preparation)
            .ExecuteAsync(new()
            {
                ConsumerId = consumer.Id,
                ProviderModeId = (int)(rejection == "unchanged" ? WebhookProviderMode.DryRun
                    : rejection == "unavailable_update" ? WebhookProviderMode.Svix : WebhookProviderMode.Local),
                ExpectedConfigurationVersion = rejection == "stale" ? 2 : 1,
                PendingWorkDecisionId = (int)(rejection == "migrate" ? WebhookPendingWorkDecision.MigrateEligible
                    : WebhookPendingWorkDecision.PreserveExisting),
                PendingWorkReason = "maintenance"
            }, CancellationToken.None);
    }

    private static async Task<WebhookConsumer> SeedConsumerMutationAsync(LocalIdentityLifecycleHttpFixture fixture,
        bool tenantOwned = false, bool administrator = false)
    {
        await using var database = fixture.Native.CreateDatabase();
        var now = fixture.Clock.GetUtcNow().UtcDateTime;
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
                JoinedAt = now
            };
            database.TenantUsers.Add(member);
        }
        if (administrator)
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
        var consumer = WebhookConsumer.Create(WebhookOwnershipScope.Create(
            tenantOwned ? WebhookConsumerKind.Tenant : WebhookConsumerKind.User,
            PlatformDefaults.DefaultTenantId, null, null, null, tenantOwned ? null : fixture.Binding.LocalSubjectId),
            $"consumer-{Guid.CreateVersion7():N}", WebhookProviderMode.DryRun, now);
        var endpoint = new WebhookEndpoint
        {
            Id = Guid.CreateVersion7(),
            TenantId = consumer.TenantId,
            ConsumerId = consumer.Id,
            Url = "https://example.invalid/consumer",
            SecretRef = "fixture-reference",
            SecretVersion = 1,
            SecretActivatedAt = now,
            ConfigurationVersion = 1,
            Status = WebhookEndpointStatus.Active,
            MaxAttempts = 3,
            TimeoutSeconds = 10,
            CreatedAt = now
        };
        var eventType = new WebhookEventType
        {
            Id = Guid.CreateVersion7(),
            Name = $"test.consumer.{Guid.CreateVersion7():N}",
            GroupName = "test",
            Description = "Consumer mode change",
            SchemaJson = "{}",
            SchemaVersion = 1,
            IsEnabled = true,
            IsPublic = true,
            PayloadRetentionDays = 1,
            CreatedAt = now
        };
        database.AddRange(consumer, endpoint, eventType, new WebhookEndpointSubscription
        {
            Id = Guid.CreateVersion7(),
            TenantId = consumer.TenantId,
            EndpointId = endpoint.Id,
            EventTypeId = eventType.Id,
            IsEnabled = true,
            CreatedAt = now
        });
        await database.SaveChangesAsync();
        return consumer;
    }

    private static async Task AssertConsumerMutationAsync(LocalIdentityLifecycleHttpFixture fixture,
        WebhookConsumer original, string operation, bool committed)
    {
        await using var observer = fixture.Native.CreateDatabase();
        // A separate physical context observes commit/rollback, never the owner's tracker.
        var consumers = await observer.WebhookConsumers.AsNoTracking()
            .Where(row => row.Name == original.Name || row.Name == original.Name + "-created").ToListAsync();
        await Assert.That(consumers.Count).IsEqualTo(committed && operation == "create" ? 2 : 1);
        var existing = consumers.Single(row => row.Id == original.Id);
        await Assert.That(existing.ConfigurationVersion).IsEqualTo(committed && operation == "update" ? 2 : 1);
        await Assert.That(existing.ProviderMode).IsEqualTo(committed && operation == "update"
            ? WebhookProviderMode.Local : WebhookProviderMode.DryRun);
        await Assert.That(existing.Status).IsEqualTo(WebhookConsumerStatus.Active);
        foreach (var item in consumers)
            await Assert.That(item.Ownership).IsEqualTo(original.Ownership);
        if (committed)
        {
            var product = operation == "create" ? consumers.Single(row => row.Id != original.Id) : existing;
            await Assert.That(operation == "create" ? product.CreatedBy : product.UpdatedBy)
                .IsEqualTo(fixture.Binding.LocalSubjectId);
            await Assert.That(product.ProviderMode).IsEqualTo(WebhookProviderMode.Local);
            if (operation == "create")
                await Assert.That(product.ConfigurationVersion).IsEqualTo(1);
        }
        var audits = await observer.Set<WebhookAuditEvent>().AsNoTracking()
            .Where(row => row.EffectiveScopeId == original.OwnerId).ToListAsync();
        await Assert.That(audits.Count).IsEqualTo(committed ? 1 : 0);
        if (committed)
        {
            var audit = audits.Single();
            await Assert.That(audit.PrincipalKind).IsEqualTo(WebhookAuditPrincipalKind.User);
            await Assert.That(audit.PrincipalReference).IsEqualTo($"user:{fixture.Binding.LocalSubjectId:D}");
            await Assert.That(audit.Action).IsEqualTo(operation == "create"
                ? WebhookAuditAction.ConsumerCreated : WebhookAuditAction.ConsumerProviderModeChanged);
            await Assert.That(audit.TargetId).IsEqualTo(operation == "create"
                ? consumers.Single(row => row.Id != original.Id).Id : original.Id);
        }
    }

    private sealed class ConsumerMutationSaveBoundary : SaveChangesInterceptor
    {
        private bool _savingAudit;
        internal Action<WebhookConsumer>? BeforeConsumer { get; set; }
        internal Func<ExploreDbContext, CancellationToken, Task>? AfterAudit { get; set; }
        internal int SavedAudits { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            foreach (var entry in eventData.Context!.ChangeTracker.Entries<WebhookConsumer>()
                .Where(entry => entry.State == EntityState.Added))
                BeforeConsumer?.Invoke(entry.Entity);
            _savingAudit = AfterAudit is not null && eventData.Context.ChangeTracker.Entries<WebhookAuditEvent>()
                .Any(entry => entry.State == EntityState.Added);
            return ValueTask.FromResult(result);
        }

        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (_savingAudit)
            {
                _savingAudit = false;
                SavedAudits++;
                await AfterAudit!((ExploreDbContext)eventData.Context!, cancellationToken);
            }
            return result;
        }
    }
}
