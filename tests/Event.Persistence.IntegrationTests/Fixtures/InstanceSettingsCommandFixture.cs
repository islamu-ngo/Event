
using System.Security.Claims;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Notifications;
using Explore.Application.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Infrastructure.Identity;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Event.Persistence.IntegrationTests.Fixtures;

internal sealed class InstanceSettingsCommandFixture : IDisposable, ITenantContext
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly ServiceProvider _provider;

    internal InstanceSettingsCommandFixture(ExploreDbContext context, Guid userId,
        RelationalSettingMutationLock? mutationLock = null)
    {
        Context = context;
        UserId = userId;
        UnitOfWork = new EfCoreUnitOfWork(context);
        MutationLock = mutationLock ?? new RelationalSettingMutationLock(context, UnitOfWork);
        SystemSettings = new SystemSettingRepository(context, MutationLock);
        EmailDeliverySettingsWriter = EmailDispatchSqliteFixture.CreateEmailSettingsWriter(context, MutationLock);
        VisitorSettings = new VisitorAccessSettingsWriter(context, MutationLock, UnitOfWork,
            new EventParticipationConfigurationRepository(context), new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        Settings = new HierarchicalSettingsResolver(SystemSettings, new TenantSettingRepository(context, MutationLock),
            new OrganizationSettingRepository(context), new GroupSettingRepository(context),
            new GroupTenantRepository(context), new UserPreferenceRepository(context), this,
            MutationLock, _cache, NullLogger<HierarchicalSettingsResolver>.Instance, EmailDeliverySettingsWriter);
        var httpContext = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "test-principal"))
            }
        };
        CurrentUserService = new CurrentUserService(httpContext);
        AdminContext = new AdminContext(httpContext, new PlatformUserRoleRepository(context), new TenantUserRoleGrantRepository(context),
            new OrganizationMemberRepository(context), new GroupMemberRepository(context),
            new UserExternalLoginRepository(context), _cache, NullLogger<AdminContext>.Instance);
        Notifications = new CommittedNotificationObserver(context);
        _provider = new ServiceCollection()
            .AddSingleton<INotificationHandler<SettingChangedNotification>>(Notifications)
            .BuildServiceProvider();
        Mediator = new Mediator(_provider);
        PublicationPolicyBoundary = new PublicationPolicyMutationBoundary(MutationLock,
            new CoordinatedSettingMutationRepository(context));
        UpsertService = new SettingUpsertService(SystemSettings, Mediator, PublicationPolicyBoundary, EmailDeliverySettingsWriter);
        Governance = new InstanceGovernanceSettingService(Settings, UpsertService,
            new ModuleCapabilityService(new TenantCapabilityRepository(context), new ModuleDefinitionRepository(context)),
            NullLogger<InstanceGovernanceSettingService>.Instance, EmailDeliverySettingsWriter);
    }

    public Guid TenantId => Guid.Empty;
    internal Guid UserId { get; }
    internal ExploreDbContext Context { get; }
    internal EfCoreUnitOfWork UnitOfWork { get; }
    internal RelationalSettingMutationLock MutationLock { get; }
    internal SystemSettingRepository SystemSettings { get; }
    internal EmailDeliverySettingsWriter EmailDeliverySettingsWriter { get; }
    internal VisitorAccessSettingsWriter VisitorSettings { get; }
    internal HierarchicalSettingsResolver Settings { get; }
    internal AdminContext AdminContext { get; }
    internal CurrentUserService CurrentUserService { get; }
    internal IMediator Mediator { get; }
    internal PublicationPolicyMutationBoundary PublicationPolicyBoundary { get; }
    internal SettingUpsertService UpsertService { get; }
    internal InstanceGovernanceSettingService Governance { get; }
    internal CommittedNotificationObserver Notifications { get; }

    internal static async Task<Guid> SeedAdministratorAsync(ExploreDbContext context)
    {
        var user = new User
        {
            Id = Guid.CreateVersion7(), Pii = new UserPii
            {
                Email = $"settings-admin-{Guid.CreateVersion7():N}@example.test",
                FirstName = "Settings", LastName = "Administrator"
            },
            CreatedAt = DateTime.UtcNow
        };
        var role = await context.Roles.SingleAsync(role => role.MasterCode == "platform.admin");
        await new PlatformUserRoleRepository(context).Create(new PlatformUserRole
        {
            Id = Guid.CreateVersion7(), UserId = user.Id, User = user, RoleId = role.Id, Role = role,
            GrantedAt = DateTime.UtcNow
        });
        return user.Id;
    }

    public void Dispose()
    {
        _provider.Dispose();
        _cache.Dispose();
    }

    internal sealed class CommittedNotificationObserver(ExploreDbContext context)
        : INotificationHandler<SettingChangedNotification>
    {
        internal List<SettingChangedNotification> Published { get; } = [];

        internal Func<SettingChangedNotification, CancellationToken, Task>? OnPublishing { get; set; }

        public async Task Handle(SettingChangedNotification notification, CancellationToken cancellationToken)
        {
            if (context.Database.CurrentTransaction is not null)
                throw new InvalidOperationException("Settings notifications must be published after commit.");
            if (OnPublishing is not null)
                await OnPublishing(notification, cancellationToken);
            Published.Add(notification);
        }
    }
}
