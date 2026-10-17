using System.Data.Common;
using System.Security.Claims;
using System.Security.Cryptography;
using Cerbos.Api.V1.Effect;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Exceptions;
using Explore.Application.Features.Webhooks.Handlers.Commands;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Configuration;
using Explore.Infrastructure.Webhooks;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanWebhookPreparationTests
{
    [Test]
    [Arguments("local")]
    [Arguments("cerbos")]
    public async Task ActualPortalRetainsOriginalHumanAndPersistsSafeAuditBeforeRelease(string provider)
    {
        await using var portal = await PortalFixture.CreateAsync(provider);
        var started = portal.Identity.Clock.GetUtcNow();
        portal.BeforeRemoteReturn = _ =>
        {
            portal.Identity.Clock.Advance(TimeSpan.FromSeconds(1));
            return Task.CompletedTask;
        };
        var result = await portal.ExecuteAsync();
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Id!.Token).IsEqualTo(portal.Token);
        await Assert.That(result.Id.Url).IsEqualTo(portal.Url);
        // GetApplication consumes one second; expiry is anchored BEFORE CreateAccess,
        // not extended by its remote response or later audit/read latency.
        await Assert.That(result.Id.ExpiresAt).IsEqualTo(started.AddSeconds(301));
        var audit = (await portal.ReadAuditsAsync()).Single();
        await Assert.That(audit.Action).IsEqualTo(WebhookAuditAction.PortalAccessIssued);
        await Assert.That(audit.PrincipalReference).IsEqualTo($"user:{portal.Identity.Binding.LocalSubjectId:D}");
        await Assert.That(audit.SafeAfterJson).DoesNotContain(portal.Token, StringComparison.Ordinal);
        await Assert.That(audit.SafeAfterJson).DoesNotContain(portal.Url, StringComparison.Ordinal);
        await Assert.That(portal.GetCalls).IsEqualTo(1);
        await Assert.That(portal.CreateCalls).IsEqualTo(1);
        await Assert.That(portal.Decisions).IsEqualTo(provider == "local" ? 0 : 1);
        await Assert.That(portal.Context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("get", "credential")]
    [Arguments("create", "credential")]
    [Arguments("get", "revoke")]
    [Arguments("create", "revoke")]
    [Arguments("get", "principal")]
    [Arguments("create", "principal")]
    [Arguments("get", "webhook")]
    [Arguments("create", "webhook")]
    [Arguments("get", "binding")]
    [Arguments("create", "binding")]
    [Arguments("get", "options")]
    [Arguments("create", "options")]
    public async Task ActualPortalRejectsOriginalCustodyDriftAcrossEachRemoteAwait(string boundary, string drift)
    {
        await using var portal = await PortalFixture.CreateAsync();
        portal.BeforeRemoteReturn = step => step == boundary ? portal.DriftAsync(drift) : Task.CompletedTask;
        await AssertPortalRejectedAsync(portal, drift);
        await Assert.That(portal.GetCalls).IsEqualTo(1);
        await Assert.That(portal.CreateCalls).IsEqualTo(boundary == "get" ? 0 : 1);
        await Assert.That(portal.Decisions).IsEqualTo(1);
        await Assert.That(portal.Context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("audit", "credential")]
    [Arguments("audit", "revoke")]
    [Arguments("audit", "principal")]
    [Arguments("audit", "webhook")]
    [Arguments("audit", "binding")]
    [Arguments("audit", "options")]
    [Arguments("provider_read", "credential")]
    [Arguments("provider_read", "revoke")]
    [Arguments("provider_read", "principal")]
    [Arguments("provider_read", "webhook")]
    [Arguments("provider_read", "binding")]
    [Arguments("provider_read", "options")]
    [Arguments("last_source_close", "credential")]
    [Arguments("last_source_close", "principal")]
    [Arguments("last_source_close", "options")]
    public async Task ActualPortalJoinsAuditAndFinalBlockingReadsBeforeRelease(string boundary, string drift)
    {
        await using var portal = await PortalFixture.CreateAsync();
        portal.AtBoundary(boundary, () => portal.DriftAsync(drift));
        await AssertPortalRejectedAsync(portal, drift);
        await Assert.That(portal.Boundaries.Fired).IsTrue();
        await Assert.That(portal.GetCalls).IsEqualTo(1);
        await Assert.That(portal.CreateCalls).IsEqualTo(1);
        var audits = await portal.ReadAuditsAsync();
        await Assert.That(audits.Any(row => row.Action == WebhookAuditAction.PortalAccessIssued)).IsTrue();
        foreach (var audit in audits)
        {
            await Assert.That(audit.SafeAfterJson).DoesNotContain(portal.Token, StringComparison.Ordinal);
            await Assert.That(audit.SafeAfterJson).DoesNotContain(portal.Url, StringComparison.Ordinal);
        }
    }

    [Test]
    [Arguments("get", "support_expiry")]
    [Arguments("create", "support_expiry")]
    [Arguments("audit", "support_expiry")]
    [Arguments("provider_read", "support_expiry")]
    [Arguments("last_source_close", "support_expiry")]
    [Arguments("get", "support_revoke")]
    [Arguments("create", "support_revoke")]
    [Arguments("audit", "support_revoke")]
    [Arguments("provider_read", "support_revoke")]
    [Arguments("valid", "none")]
    public async Task ActualPortalRetainsOriginalForwardedSupportThroughRelease(string boundary, string drift)
    {
        await using var portal = await PortalFixture.CreateAsync();
        await portal.ForwardSupportAsync();
        if (boundary is "get" or "create")
            portal.BeforeRemoteReturn = step => step == boundary ? portal.DriftAsync(drift) : Task.CompletedTask;
        else if (boundary != "valid")
            portal.AtBoundary(boundary, () => portal.DriftAsync(drift));
        if (boundary == "valid")
            await Assert.That((await portal.ExecuteAsync()).IsSuccess).IsTrue();
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => portal.ExecuteAsync());
        await Assert.That(portal.Decisions).IsEqualTo(1);
        await Assert.That(portal.Context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ActualPortalNeverFallsBackFromSelectedCerbosDenialOrUnavailability(bool unavailable)
    {
        await using var portal = await PortalFixture.CreateAsync(deny: !unavailable, unavailable: unavailable);
        if (unavailable)
            await Assert.ThrowsAsync<AuthorizationProviderUnavailableException>(() => portal.ExecuteAsync());
        else
            await Assert.ThrowsAsync<AuthorizationException>(() => portal.ExecuteAsync());
        await Assert.That(portal.GetCalls).IsEqualTo(0);
        await Assert.That(portal.CreateCalls).IsEqualTo(0);
        await Assert.That(await portal.ReadAuditsAsync()).IsEmpty();
    }

    [Test]
    public async Task ActualPortalDoesNotSanitizeSourceFailureIntoRetryableProviderFailure()
    {
        await using var portal = await PortalFixture.CreateAsync();
        portal.BeforeRemoteReturn = step =>
        {
            if (step == "get")
                portal.Boundaries.BeforeProviderRead = () => throw new InvalidOperationException("portal source unavailable");
            return Task.CompletedTask;
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => portal.ExecuteAsync());
        await Assert.That(portal.Boundaries.Fired).IsTrue();
        await Assert.That(portal.CreateCalls).IsEqualTo(0);
        await Assert.That(await portal.ReadAuditsAsync()).IsEmpty();
    }

    [Test]
    public async Task ActualPortalCannotReleaseAResultWhoseRequestedLifetimeElapsedDuringAudit()
    {
        await using var portal = await PortalFixture.CreateAsync();
        portal.AtBoundary("audit", () =>
        {
            portal.Identity.Clock.Advance(TimeSpan.FromSeconds(300));
            return Task.CompletedTask;
        });
        var result = await portal.ExecuteAsync();
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.IsRetryable).IsFalse();
        await Assert.That(result.Id).IsNull();
        await Assert.That(portal.Boundaries.Fired).IsTrue();
        await Assert.That((await portal.ReadAuditsAsync()).Select(row => row.Action))
            .IsEquivalentTo([WebhookAuditAction.PortalAccessIssued, WebhookAuditAction.PortalAccessRejected]);
    }

    [Test]
    [Arguments("command")]
    [Arguments("session")]
    [Arguments("duration")]
    public async Task ActualPortalCannotReuseOriginalRequesterForAnotherCommandOrInput(string substitution)
    {
        await using var portal = await PortalFixture.CreateAsync();
        var requester = await portal.PrepareRequesterAsync();
        var input = new WebhookProviderPortalAccessInput(portal.Consumer.Id, portal.Command.SessionId,
            TimeSpan.FromSeconds(300), requester.Consumer)
        {
            OriginalCommand = substitution == "command" ? portal.Command with { } : portal.Command,
            OriginalRequester = requester
        };
        if (substitution == "session") input = input with { SessionId = Guid.NewGuid().ToString("N") };
        if (substitution == "duration") input = input with { ExpiresIn = TimeSpan.FromMinutes(10) };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => portal.Service.CreateAccessAsync(input, CancellationToken.None));
        await Assert.That(portal.GetCalls).IsEqualTo(0);
        await Assert.That(portal.CreateCalls).IsEqualTo(0);
        await Assert.That(await portal.ReadAuditsAsync()).IsEmpty();
    }

    private static async Task AssertPortalRejectedAsync(PortalFixture portal, string drift)
    {
        if (drift == "options")
        {
            var result = await portal.ExecuteAsync();
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.IsRetryable).IsFalse();
            await Assert.That(result.Id).IsNull();
        }
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => portal.ExecuteAsync());
    }

    /// <summary>Real Local login, selected sources, repositories, audit writer, handler and Svix service.</summary>
    internal sealed class PortalFixture : IAsyncDisposable
    {
        private readonly Request _request;
        private readonly Transport _transport;
        private Guid? _supportId;
        private DateTimeOffset _supportExpiry;
        internal LocalIdentityLifecycleHttpFixture Identity { get; }
        internal PortalBoundaryInterceptor Boundaries { get; }
        internal ExploreDbContext Context => _request.Services.GetRequiredService<ExploreDbContext>();
        internal IServiceProvider Services => _request.Services;
        internal ISvixWebhookClient Svix { get; } = Substitute.For<ISvixWebhookClient>();
        internal PortalOptions Options { get; } = new();
        internal WebhookConsumer Consumer { get; }
        internal WebhookConsumerProviderBinding Binding { get; }
        internal OpenSvixAppPortalCommand Command { get; }
        internal string Token { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        internal string Url => $"https://svix.example/portal/{Token}";
        internal Func<string, Task>? BeforeRemoteReturn { get; set; }
        internal Exception? ProviderFailure { get; set; }
        internal int GetCalls { get; private set; }
        internal int CreateCalls { get; private set; }
        internal int Decisions => _transport.Requests.Count;
        internal OpenSvixAppPortalCommandHandler Handler { get; }
        internal IWebhookProviderPortalService Service { get; }
        private IHumanWebhookMutationPreparation Preparation { get; }

        private PortalFixture(LocalIdentityLifecycleHttpFixture identity, Request request,
            PortalBoundaryInterceptor boundaries, WebhookConsumer consumer,
            WebhookConsumerProviderBinding binding, string provider, bool deny, bool unavailable)
        {
            Identity = identity;
            _request = request;
            Boundaries = boundaries;
            Consumer = consumer;
            Binding = binding;
            Command = new() { ConsumerId = consumer.Id, SessionId = Guid.NewGuid().ToString("N"), ExpiresInSeconds = 300 };
            _transport = new Transport(Context) { Decision = deny ? Effect.Deny : Effect.Allow, Unavailable = unavailable };
            Preparation = CreatePreparation(Services, identity.Clock, provider, _transport);
            Service = new SvixAppPortalService(Svix, Services.GetRequiredService<IWebhookConsumerRepository>(),
                Services.GetRequiredService<IWebhookConsumerProviderBindingRepository>(), Options, identity.Clock);
            Handler = new OpenSvixAppPortalCommandHandler(
                Service,
                Services.GetRequiredService<IWebhookAuditEventWriter>(),
                Services.GetRequiredService<IWebhookConsumerRepository>(),
                Preparation,
                Services.GetRequiredService<IMachinePrincipalAccessor>());
            Svix.GetApplicationAsync(binding.ExternalApplicationId!, Arg.Any<CancellationToken>())
                .Returns(async _ =>
                {
                    RequireIdle();
                    GetCalls++;
                    if (BeforeRemoteReturn is { } callback) await callback("get");
                    return new SvixApplicationBindingResult(binding.ExternalApplicationId!, binding.ApplicationUid,
                        new Dictionary<string, string>
                        {
                            ["islamu.tenant_id"] = consumer.TenantId!.Value.ToString("D"),
                            ["islamu.consumer_id"] = consumer.Id.ToString("D"),
                            ["islamu.owner_id"] = consumer.OwnerId.ToString("D"),
                            ["islamu.owner_kind_id"] = ((int)consumer.ConsumerKind).ToString()
                        });
                });
            Svix.CreateAppPortalAccessAsync(Arg.Any<SvixAppPortalAccessRequest>(), Arg.Any<CancellationToken>())
                .Returns(async _ =>
                {
                    RequireIdle();
                    CreateCalls++;
                    if (ProviderFailure is { } failure) throw failure;
                    if (BeforeRemoteReturn is { } callback) await callback("create");
                    return new SvixAppPortalAccessResult(Url, Token);
                });
        }

        internal static async Task<PortalFixture> CreateAsync(string provider = "cerbos", bool deny = false, bool unavailable = false)
        {
            var boundaries = new PortalBoundaryInterceptor();
            var identity = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: boundaries);
            var request = new Request(identity.Host.Services);
            try
            {
                await request.AuthenticateAsync(await identity.SignInAsync());
                await using var seed = identity.Native.CreateDatabase();
                var now = identity.Clock.GetUtcNow();
                var consumer = WebhookConsumer.Create(WebhookOwnershipScope.Create(WebhookConsumerKind.User,
                    PlatformDefaults.DefaultTenantId, null, null, null, identity.Binding.LocalSubjectId),
                    "portal-original-requester", WebhookProviderMode.Svix, now.UtcDateTime);
                var profile = WebhookProviderCapabilityProfile.Create(WebhookProviderKind.Svix, "1.96.1",
                    WebhookProviderCapability.AppPortal, "svix-1.96.1-v1", now);
                var binding = WebhookConsumerProviderBinding.CreatePending(consumer.TenantId, consumer.Id,
                    Guid.CreateVersion7(), "production", profile, WebhookProviderCapability.AppPortal);
                binding.VerifyOwnership(consumer.TenantId, consumer.Id, "app_original_requester", now);
                int role = await seed.Roles.Where(row => row.MasterCode == "platform.admin"
                    && row.RoleScopeId == (int)RoleScopeEnum.Platform).Select(row => row.Id).SingleAsync();
                seed.AddRange(consumer, binding, new PlatformUserRole
                {
                    Id = Guid.CreateVersion7(),
                    UserId = identity.Binding.LocalSubjectId,
                    User = null!,
                    RoleId = role,
                    Role = null!,
                    GrantedAt = now.UtcDateTime
                });
                await seed.SaveChangesAsync();
                boundaries.ConsumerId = consumer.Id;
                return new(identity, request, boundaries, consumer, binding, provider, deny, unavailable);
            }
            catch
            {
                await request.DisposeAsync();
                await identity.DisposeAsync();
                throw;
            }
        }

        internal Task<WebhookProviderPortalAccessCommandResponse> ExecuteAsync()
        {
            // Bind in the caller's execution context, not inside the async fixture factory.
            Services.GetRequiredService<IHttpContextAccessor>().HttpContext = _request.Context;
            return Handler.ExecuteAsync(Command, LocalIdentityLifecycleHttpFixture.CancellationToken);
        }

        internal Task<IPreparedHumanWebhookPortalRequester> PrepareRequesterAsync()
        {
            Services.GetRequiredService<IHttpContextAccessor>().HttpContext = _request.Context;
            return Preparation.PreparePortalAsync(Command);
        }

        internal IHumanWebhookMutationPreparation CreateHttpPreparation(IServiceProvider services) =>
            CreatePreparation(services, Identity.Clock, "local",
                new Transport(services.GetRequiredService<ExploreDbContext>()));

        internal async Task<List<WebhookAuditEvent>> ReadAuditsAsync()
        {
            await using var database = Identity.Native.CreateDatabase();
            return await database.Set<WebhookAuditEvent>().AsNoTracking()
                .Where(row => row.TargetId == Consumer.Id).ToListAsync();
        }

        internal void AtBoundary(string boundary, Func<Task> action)
        {
            if (boundary == "audit") Boundaries.AfterAudit = action;
            else Boundaries.AfterAudit = () =>
            {
                if (boundary == "provider_read") Boundaries.BeforeProviderRead = action;
                else if (boundary == "last_source_close") Boundaries.AfterLastSourceClose = action;
                else throw new ArgumentOutOfRangeException(nameof(boundary));
                return Task.CompletedTask;
            };
        }

        internal async Task DriftAsync(string drift)
        {
            switch (drift)
            {
                case "credential":
                    var lifetime = (await Services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
                        .ObserveAsync(_request.Context.User, CancellationToken.None))!;
                    Identity.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1) - Identity.Clock.GetUtcNow());
                    break;
                case "principal":
                    _request.Context.User = new ClaimsPrincipal(_request.Context.User.Identities
                        .Select(identity => new ClaimsIdentity(identity)));
                    break;
                case "options":
                    Options.CurrentValue.Svix.BaseUrl = "https://different-svix.example";
                    break;
                case "support_expiry":
                    Identity.Clock.Advance(_supportExpiry - Identity.Clock.GetUtcNow());
                    break;
                default:
                    await using (var database = Identity.Native.CreateDatabase())
                    {
                        if (drift == "revoke")
                            await database.Set<LocalIdentityUser>().Where(row => row.Id == Identity.Binding.LocalSubjectId)
                                .ExecuteUpdateAsync(update => update.SetProperty(row => row.SecurityStamp, Guid.NewGuid().ToString("N")));
                        else if (drift == "webhook")
                            await database.WebhookConsumers.Where(row => row.Id == Consumer.Id)
                                .ExecuteUpdateAsync(update => update.SetProperty(row => row.ConfigurationVersion, row => row.ConfigurationVersion + 1));
                        else if (drift == "binding")
                            await database.WebhookConsumerProviderBindings.Where(row => row.Id == Binding.Id)
                                .ExecuteUpdateAsync(update => update.SetProperty(row => row.VerificationFence, row => row.VerificationFence + 1));
                        else if (drift == "support_revoke")
                            await database.SupportAccessSessions.Where(row => row.Id == _supportId)
                                .ExecuteUpdateAsync(update => update.SetProperty(row => row.StatusId, (int)SupportAccessSessionStatusEnum.Revoked));
                        else throw new ArgumentOutOfRangeException(nameof(drift));
                    }
                    break;
            }
        }

        internal async Task ForwardSupportAsync()
        {
            Services.GetRequiredService<IHttpContextAccessor>().HttpContext = _request.Context;
            await using var database = Identity.Native.CreateDatabase();
            foreach (var key in new[] { GovernanceSettingKeys.SupportAccess.Enabled, GovernanceSettingKeys.SupportAccess.AllowWriteMode })
            {
                var setting = await database.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
                if (setting is null) database.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "true" });
                else setting.Value = "true";
            }
            _supportExpiry = Identity.Clock.GetUtcNow().AddMinutes(2);
            var session = SupportAccessSession.Start(Identity.Binding.LocalSubjectId,
                PlatformDefaults.DefaultTenantId, SupportAccessModeEnum.Write, "maintenance",
                "Portal administration", "fixture-ticket", Identity.Clock.GetUtcNow().AddMinutes(-1), _supportExpiry);
            database.SupportAccessSessions.Add(session);
            await database.SaveChangesAsync();
            _supportId = session.Id;
            _request.Context.Request.Headers[SupportAccessHeaderNames.SessionId] = session.Id.ToString("D");
            var support = await Services.GetRequiredService<ISupportAccessSessionService>().GetCurrentAsync();
            await Assert.That(support.IsActive).IsTrue();
            await Assert.That(support.SessionId).IsEqualTo(session.Id);
        }

        private void RequireIdle()
        {
            var current = Services.GetRequiredService<IHttpContextAccessor>().HttpContext?.RequestServices
                .GetRequiredService<ExploreDbContext>() ?? Context;
            if (current.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
                throw new InvalidOperationException("Remote portal issuance must not run under source ownership.");
        }

        public async ValueTask DisposeAsync()
        {
            await _request.DisposeAsync();
            await Identity.DisposeAsync();
        }
    }

    internal sealed class PortalOptions : IOptionsMonitor<WebhookOptions>
    {
        public WebhookOptions CurrentValue { get; } = new()
        {
            Provider = WebhookOptions.ProviderSvix,
            Svix = new() { Environment = "production", ProviderVersion = "1.96.1", CapabilityPolicyVersion = "svix-1.96.1-v1" }
        };
        public WebhookOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<WebhookOptions, string?> listener) => null;
    }

    /// <summary>
    /// Real persistence events, not substituted sources. The final provider query closes once,
    /// then P/route/H/W each close their original connection. Gate the fifth close to exercise
    /// the LAST awaited cleanup before genuine lifetime/support time, with no sleeps or polling.
    /// </summary>
    internal sealed class PortalBoundaryInterceptor : DbCommandInterceptor, ISaveChangesInterceptor, IDbConnectionInterceptor
    {
        private bool _savingAudit;
        private DbContext? _finalContext;
        private int _finalCloses;
        internal Guid ConsumerId { get; set; }
        internal bool FailAudit { get; set; }
        internal bool Fired { get; private set; }
        internal Func<Task>? AfterAudit { get; set; }
        internal Func<Task>? BeforeProviderRead { get; set; }
        internal Func<Task>? AfterLastSourceClose { get; set; }

        public ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            _savingAudit = ConsumerId != Guid.Empty && eventData.Context!.ChangeTracker.Entries<WebhookAuditEvent>()
                .Any(row => row.State == EntityState.Added && row.Entity.TargetId == ConsumerId);
            if (_savingAudit && FailAudit) throw new InvalidOperationException("audit unavailable");
            return ValueTask.FromResult(result);
        }

        public async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (_savingAudit)
            {
                _savingAudit = false;
                if (AfterAudit is { } callback)
                {
                    AfterAudit = null;
                    await callback();
                    if (BeforeProviderRead is null && AfterLastSourceClose is null) Fired = true;
                }
            }
            return result;
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (!command.CommandText.Contains("webhook_consumers", StringComparison.OrdinalIgnoreCase))
                return result;
            if (BeforeProviderRead is { } callback)
            {
                BeforeProviderRead = null;
                Fired = true;
                await callback();
            }
            if (AfterLastSourceClose is not null && _finalContext is null)
                _finalContext = eventData.Context;
            return result;
        }

        public async Task ConnectionClosedAsync(DbConnection connection, ConnectionEndEventData eventData)
        {
            if (AfterLastSourceClose is { } callback && _finalContext is not null
                && ReferenceEquals(eventData.Context, _finalContext) && ++_finalCloses == 5)
            {
                AfterLastSourceClose = null;
                Fired = true;
                await callback();
            }
        }
    }
}
