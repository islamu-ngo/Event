using System.Collections.Immutable;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EmailDispatch;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Features.EmailDispatch.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class EmailDisableHandlerAuthenticationTests
{
    private static DisableEmailDeliveryCommand Command(EmailDeliveryDisablePreviewDto preview) =>
        new(preview.TenantId, preview.ExpectedRevision, DisableEmailDeliveryCommand.RequiredAcknowledgement, preview.ConfirmationToken);

    private static async Task<Guid> AddTenantAsync(ExploreDbContext context)
    {
        Guid id = Guid.CreateVersion7();
        context.Tenants.Add(new Tenant
        {
            Id = id,
            FullName = "Native disable tenant",
            Slug = $"native-disable-{id:N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        return id;
    }

    private static async Task<(string Value, Guid? Actor, DateTime? UpdatedAt)> ReadSettingAsync(ExploreDbContext context, Guid? target)
    {
        if (target is Guid tenantId)
        {
            var row = await context.TenantSettingOverrides
                .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation).AsNoTracking()
                .SingleAsync(row => row.TenantId == tenantId && row.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled);
            return (row.Value, row.UpdatedBy, row.UpdatedAt);
        }
        var system = await context.SystemSettings.AsNoTracking()
            .SingleAsync(row => row.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled);
        return (system.Value, system.UpdatedBy, system.UpdatedAt);
    }

    // These helpers are exclusively for detached nonhuman starting state and external changes.
    // Every operation under test goes through the authenticated session's production handlers.
    private static InternalEmailDeliverySettingsWriter InternalWriter(ExploreDbContext context)
    {
        var unitOfWork = new EfCoreUnitOfWork(context);
        return new(context, new RelationalSettingMutationLock(context, unitOfWork), unitOfWork);
    }

    private static async Task ApplyEmailSettingsAsync(ExploreDbContext context,
        ImmutableArray<EmailDeliverySettingMutation> mutations)
    {
        var result = await InternalWriter(context).ApplyInternalAsync(mutations, actorUserId: null);
        if (result.Status is not (EmailDeliverySettingsWriteStatus.Applied or EmailDeliverySettingsWriteStatus.NoChange))
            throw new InvalidOperationException($"Email fixture mutation rejected: {result.Status}.");
    }

    private static Task SetEmailSettingAsync(ExploreDbContext context, string key, string value, Guid? tenantId = null) =>
        ApplyEmailSettingsAsync(context, [new(tenantId, key, EmailDeliverySettingMutationKind.SetValue, value)]);

    private static async Task ConfirmEmailDisableAsync(ExploreDbContext context, Guid? tenantId, Guid? actorUserId)
    {
        var result = await InternalWriter(context).DisableInternalAsync(tenantId, actorUserId);
        if (result.Status is not (EmailDeliverySettingsWriteStatus.Applied or EmailDeliverySettingsWriteStatus.NoChange))
            throw new InvalidOperationException($"Email fixture disable rejected: {result.Status}.");
    }

    /// <summary>
    /// One API host and database, three independently provisioned Local accounts, and real HTTP
    /// login credentials. Account switching selects a different original authenticated request;
    /// it never changes claims on an existing principal.
    /// </summary>
    private sealed class Scenario : IAsyncDisposable
    {
        private readonly LocalIdentityLifecycleHttpFixture local;
        private readonly Dictionary<Guid, string> accessTokens = [];
        private readonly AsyncServiceScope tokenScope;
        private PlatformUserRole platformGrant = null!;
        internal WebApplicationFactory<Program> Host { get; }
        internal Guid PlatformActorId { get; private set; }
        internal Guid OtherPlatformActorId { get; private set; }
        internal Guid TenantActorId { get; private set; }
        internal Guid TenantId => PlatformDefaults.DefaultTenantId;
        internal Guid InheritedTenantId { get; private set; }
        internal TimeProvider Clock => local.Clock;
        internal IEmailDeliveryDisableTokenService Tokens =>
            tokenScope.ServiceProvider.GetRequiredService<IEmailDeliveryDisableTokenService>();

        private Scenario(LocalIdentityLifecycleHttpFixture local)
        {
            this.local = local;
            Host = local.Host.WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(Session.ConfigureServices));
            tokenScope = Host.Services.CreateAsyncScope();
        }

        internal ExploreDbContext CreateIndependentContext() => local.Native.CreateDatabase();

        internal static async Task<Scenario> CreateAsync()
        {
            var local = await LocalIdentityLifecycleHttpFixture.CreateAsync(verified: true, emailEnabled: true);
            Scenario? scenario = null;
            try
            {
                scenario = new(local);
                scenario.PlatformActorId = local.Binding.LocalSubjectId;
                scenario.accessTokens.Add(scenario.PlatformActorId, await local.SignInAsync());
                scenario.OtherPlatformActorId = await scenario.AddAuthenticatedAccountAsync();
                scenario.TenantActorId = await scenario.AddAuthenticatedAccountAsync();

                await using var context = scenario.CreateIndependentContext();
                foreach (Guid actor in new[] { scenario.PlatformActorId, scenario.OtherPlatformActorId })
                {
                    var grant = new PlatformUserRole
                    {
                        Id = Guid.CreateVersion7(),
                        UserId = actor,
                        User = null!,
                        RoleId = (int)RoleEnum.Admin,
                        Role = null!,
                        GrantedAt = DateTime.UtcNow
                    };
                    context.PlatformUserRoles.Add(grant);
                    if (actor == scenario.PlatformActorId) scenario.platformGrant = grant;
                }
                var membership = new TenantUser
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = scenario.TenantId,
                    Tenant = null!,
                    UserId = scenario.TenantActorId,
                    User = null!,
                    StatusId = (int)TenantUserStatusEnum.Active,
                    JoinedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                };
                context.TenantUsers.Add(membership);
                context.TenantUserRoleGrants.Add(new TenantUserRoleGrant
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = scenario.TenantId,
                    Tenant = null!,
                    TenantUserId = membership.Id,
                    TenantUser = membership,
                    RoleId = (int)RoleEnum.TenantAdmin,
                    Role = null!,
                    RoleScopeId = (int)RoleScopeEnum.Tenant,
                    GrantedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                });
                // The bootstrap's default tenant is the independently configured tenant. The
                // only added tenant inherits instance SMTP, preserving the original two scopes.
                scenario.InheritedTenantId = await AddTenantAsync(context);
                foreach (var (key, value) in new Dictionary<string, string>
                {
                    [GovernanceSettingKeys.Deployment.Mode] = "\"MultiTenant\"",
                    [GovernanceSettingKeys.EventReporting.IntakeEnabled] = "true"
                })
                {
                    var setting = await context.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
                    if (setting is null)
                    {
                        var definition = SettingRegistry.Get(key)!;
                        setting = new SystemSetting
                        {
                            Id = Guid.CreateVersion7(),
                            SettingKey = key,
                            Value = value,
                            ValueType = definition.ValueType,
                            CreatedAt = DateTime.UtcNow
                        };
                        context.SystemSettings.Add(setting);
                    }
                    setting.Value = value;
                }
                await context.SaveChangesAsync();
                await ApplyEmailSettingsAsync(context,
                    [new(null, GovernanceSettingKeys.Email.DeliveryEnabled, EmailDeliverySettingMutationKind.SetValue, "true"),
                     new(null, GovernanceSettingKeys.Email.SmtpHost, EmailDeliverySettingMutationKind.SetValue, "\"smtp.fixture.test\""),
                     new(null, GovernanceSettingKeys.Email.FromAddress, EmailDeliverySettingMutationKind.SetValue, "\"events@fixture.test\""),
                     new(null, GovernanceSettingKeys.Email.SmtpSecurity, EmailDeliverySettingMutationKind.SetValue, "\"StartTls\""),
                     new(null, GovernanceSettingKeys.TenantDelegation.LockSmtp, EmailDeliverySettingMutationKind.SetValue, "false")]);
                await ApplyEmailSettingsAsync(context,
                    [new(scenario.TenantId, GovernanceSettingKeys.Email.DeliveryEnabled, EmailDeliverySettingMutationKind.SetValue, "true", true),
                     new(scenario.TenantId, GovernanceSettingKeys.Email.SmtpHost, EmailDeliverySettingMutationKind.SetValue, "\"smtp.native-tenant.test\""),
                     new(scenario.TenantId, GovernanceSettingKeys.Email.FromAddress, EmailDeliverySettingMutationKind.SetValue, "\"events@native-tenant.test\"")]);
                await SetEmailSettingAsync(context, GovernanceSettingKeys.Email.FromName, "\"Native handler sender\"");
                return scenario;
            }
            catch
            {
                if (scenario is not null) await scenario.DisposeAsync();
                else await local.DisposeAsync();
                throw;
            }
        }

        private async Task<Guid> AddAuthenticatedAccountAsync()
        {
            LocalAuthRequestDto login = await local.Native.SeedLocalUserAsync(emailConfirmed: true);
            string token = await local.SignInAsync(login);
            await using var context = CreateIndependentContext();
            Guid actor = await context.Users.Where(user => user.Pii.Email == login.Identifier)
                .Select(user => user.Id).SingleAsync();
            accessTokens.Add(actor, token);
            return actor;
        }

        internal Task<Session> OpenAsync(bool tenantActor = false, Guid? actor = null,
            Guid? currentTenant = null, Guid? ownerTenant = null)
        {
            Guid selectedActor = actor ?? (tenantActor ? TenantActorId : PlatformActorId);
            return Session.CreateAsync(this, selectedActor,
                selectedActor == Guid.Empty ? null : accessTokens[selectedActor],
                currentTenant ?? TenantId, ownerTenant ?? (tenantActor ? currentTenant ?? TenantId : null));
        }

        internal async Task RevokeAsync(bool tenantActor)
        {
            await using var context = CreateIndependentContext();
            if (tenantActor)
                await context.TenantUserRoleGrants.IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation)
                    .Where(row => row.TenantId == TenantId).ExecuteUpdateAsync(update => update.SetProperty(row => row.RevokedAt, DateTime.UtcNow));
            else
                await context.PlatformUserRoles.Where(row => row.UserId == PlatformActorId).ExecuteDeleteAsync();
        }

        internal async Task RestoreAsync(bool tenantActor)
        {
            await using var context = CreateIndependentContext();
            if (tenantActor)
                await context.TenantUserRoleGrants.IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation)
                    .Where(row => row.TenantId == TenantId).ExecuteUpdateAsync(update => update.SetProperty(row => row.RevokedAt, (DateTime?)null));
            else
            {
                context.PlatformUserRoles.Add(new PlatformUserRole
                {
                    Id = platformGrant.Id,
                    UserId = PlatformActorId,
                    User = null!,
                    RoleId = platformGrant.RoleId,
                    Role = null!,
                    GrantedAt = platformGrant.GrantedAt
                });
                await context.SaveChangesAsync();
            }
        }

        internal async Task<string> StateAsync()
        {
            await using var context = CreateIndependentContext();
            // Detached persisted scalars include policy values, revisions, suppression, locks, and actor/timestamp metadata.
            var system = await context.SystemSettings.AsNoTracking().OrderBy(row => row.SettingKey)
                .Select(row => new { row.Id, row.SettingKey, row.Value, row.IsLocked, row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy }).ToArrayAsync();
            var tenants = await context.TenantSettingOverrides.IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation)
                .AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new { row.Id, row.TenantId, row.SettingKey, row.Value, row.IsLocked, row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy }).ToArrayAsync();
            var processor = await context.EmailDispatchProcessorStates.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new
                {
                    row.Id,
                    row.DeliveryPolicyRevision,
                    row.OptionalSuppressedThroughRevision,
                    row.OptionalSuppressedThroughUtc,
                    row.IsPaused,
                    row.PauseReason,
                    row.PausedAt,
                    row.PausedBy,
                    row.GlobalSmtpRateLimitPerMinuteOverride,
                    row.OptionalRemindersDeferred,
                    row.SmtpAvailableTokens,
                    row.SmtpRefillAt
                }).ToArrayAsync();
            var controls = await context.EmailDispatchTenantControls.IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation)
                .AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new
                {
                    row.Id,
                    row.TenantId,
                    row.DeliveryPolicyRevision,
                    row.OptionalSuppressedThroughRevision,
                    row.OptionalSuppressedThroughUtc,
                    row.IsPaused,
                    row.PauseReason,
                    row.PausedAt,
                    row.PausedBy,
                    row.SmtpAvailableTokens,
                    row.SmtpRefillAt
                }).ToArrayAsync();
            var dispatches = await context.EmailDispatchOutbox.IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation)
                .AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new
                {
                    row.Id,
                    row.TenantId,
                    row.Status,
                    row.AttemptCount,
                    row.ProcessingStartedAt,
                    row.ProcessingLeaseToken,
                    row.PublishEventId,
                    row.CreatedAt,
                    row.UpdatedAt
                }).ToArrayAsync();
            var deliveries = await context.NotificationDeliveries.IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation)
                .AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new
                {
                    row.Id,
                    row.TenantId,
                    row.StatusId,
                    row.ProviderStatus,
                    row.QueuedAt,
                    row.EmailDispatchOutboxId,
                    row.PolicyVersion
                }).ToArrayAsync();
            return JsonSerializer.Serialize(new { system, tenants, processor, controls, dispatches, deliveries });
        }

        public async ValueTask DisposeAsync()
        {
            await tokenScope.DisposeAsync();
            await Host.DisposeAsync();
            await local.DisposeAsync();
        }
    }
}
