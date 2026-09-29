using System.Reflection;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application;
using Explore.Application.Features.ControlPlane.Handlers.Commands;
using Explore.Application.Features.ControlPlane.Requests.Commands;
using Explore.Application.Features.Settings.Handlers.Commands;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Explore.Secrets.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Architecture.Tests;

public sealed class SettingMutationBoundaryArchitectureTests
{
    private static readonly IReadOnlyDictionary<Type, IReadOnlyCollection<Type>>
        GuardedMutationOwnerDependencies = new Dictionary<Type, IReadOnlyCollection<Type>>
        {
            [typeof(TenantPolicySettingService)] = [typeof(IPublicationPolicyMutationBoundary)],
            [typeof(SetControlPlaneTenantSettingCommandHandler)] = [typeof(IPublicationPolicyMutationBoundary)],
            [typeof(ApplyControlPlaneTenantPlanAssignmentCommandHandler)] = [typeof(IPublicationPolicyMutationBoundary)],
            [typeof(UpdateSettingCommandHandler)] = [typeof(IPublicationPolicyMutationBoundary)],
            [typeof(UpdateSettingBatchCommandHandler)] = [typeof(IPublicationPolicyMutationBoundary)],
            [typeof(ResetSettingCommandHandler)] = [typeof(IPublicationPolicyMutationBoundary)],
            [typeof(LockSettingCommandHandler)] = [typeof(IPublicationPolicyMutationBoundary)],
            [typeof(UnlockSettingCommandHandler)] = [typeof(IPublicationPolicyMutationBoundary)],
            [typeof(InstanceGovernanceSettingService)] = [typeof(SettingUpsertService)],
            [typeof(SettingUpsertService)] = [typeof(IPublicationPolicyMutationBoundary)]
        };

    private static readonly Type[] GuardedSettingRowWriteAllowlist =
    [
        typeof(CoordinatedSettingMutationRepository)
    ];

    private static readonly string[] ExpectedPublicationPolicyKeys =
    [
        "event_reporting.intake_enabled",
        "events.require_approval",
        "events.user_submission_enabled",
        "events.organization_submission_enabled",
        "events.group_submission_enabled"
    ];

    [Test]
    public async Task SettingMutationLock_ShouldExposeMultiKeyExecution()
    {
        MethodInfo? method = typeof(ISettingMutationLock).GetMethod("ExecuteManyAsync");

        await Assert.That(method).IsNotNull()
            .Because("tenant policy batches must acquire all publication-policy setting locks in deterministic order");
    }

    [Test]
    public async Task PersistenceLockImplementations_ShouldRemainProviderNeutral()
    {
        Type[] lockContracts = [typeof(ISettingMutationLock), typeof(IAtprotoSessionRefreshLock)];
        string[] violations = typeof(RelationalSettingMutationLock).Assembly.GetTypes()
            .Where(type => lockContracts.Any(contract => contract.IsAssignableFrom(type))
                && !type.IsInterface
                && type.Name.Contains("Postgres", StringComparison.OrdinalIgnoreCase))
            .Select(type => type.FullName!)
            .ToArray();

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task SettingRepositoryContracts_ShouldNotInheritGenericCrud()
    {
        Type genericRepository = typeof(IGenericRepository<,>);
        Type[] violations =
        [
            .. typeof(ITenantSettingRepository).GetInterfaces()
                .Where(candidate => candidate.IsGenericType
                    && candidate.GetGenericTypeDefinition() == genericRepository),
            .. typeof(ISystemSettingRepository).GetInterfaces()
                .Where(candidate => candidate.IsGenericType
                    && candidate.GetGenericTypeDefinition() == genericRepository)
        ];

        await Assert.That(violations).IsEmpty()
            .Because("setting mutations must use explicit coordinated repository operations");
    }

    [Test]
    public async Task SettingRepositoryImplementations_ShouldNotExposeGenericMutationMethods()
    {
        string[] forbiddenNames = ["Create", "Update", "Delete"];
        string[] violations = new[] { typeof(TenantSettingRepository), typeof(SystemSettingRepository) }
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(method => forbiddenNames.Contains(method.Name, StringComparer.Ordinal))
                .Select(method => $"{type.FullName}.{method.Name}"))
            .ToArray();

        await Assert.That(violations).IsEmpty()
            .Because("concrete setting repositories must not retain inherited mutation escape hatches");
    }

    [Test]
    public async Task TenantPolicyContract_ShouldReturnNotificationsForPostCommitPublication()
    {
        MethodInfo method = typeof(ITenantPolicySettingService)
            .GetMethod(nameof(ITenantPolicySettingService.ApplyTenantSettingsAsync))!;

        await Assert.That(method.ReturnType).IsEqualTo(typeof(Task<IReadOnlyList<SettingChangedNotification>>));
        await Assert.That(method.GetParameters().Last().ParameterType).IsEqualTo(typeof(CancellationToken));
    }

    [Test]
    public async Task DirectControlPlaneSettingHandlers_ShouldRequireTrustedCurrentUserContext()
    {
        Type[] handlerTypes =
        [
            typeof(SetControlPlaneTenantSettingCommandHandler),
            typeof(LockControlPlaneTenantSettingCommandHandler),
            typeof(UnlockControlPlaneTenantSettingCommandHandler)
        ];

        string[] violations = handlerTypes
            .Where(type => !type.GetConstructors().Single().GetParameters()
                .Any(parameter => parameter.ParameterType == typeof(ICurrentUserService)))
            .Select(type => type.Name)
            .ToArray();

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task ControlPlaneMutationHandlers_ShouldDeclarePostCommitSideEffectDependencies()
    {
        Type[] handlerTypes =
        [
            typeof(SetControlPlaneTenantSettingCommandHandler),
            typeof(LockControlPlaneTenantSettingCommandHandler),
            typeof(UnlockControlPlaneTenantSettingCommandHandler),
            typeof(ApplyControlPlaneTenantPlanAssignmentCommandHandler)
        ];

        string[] violations = handlerTypes
            .Where(type => !type.GetConstructors().Single().GetParameters()
                .Any(parameter => parameter.ParameterType ==
                    typeof(IEnumerable<Explore.Application.Contracts.Operations.INotificationHandler<SettingChangedNotification>>)))
            .Select(type => type.Name)
            .ToArray();

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task GuardedMutationOwnersMustDependOnTheBoundaryOrApprovedCoordinator()
    {
        string[] violations = GuardedMutationOwnerDependencies
            .Where(entry => !entry.Key.GetConstructors().Single().GetParameters()
                .Any(parameter => entry.Value.Contains(parameter.ParameterType)))
            .Select(entry => $"{entry.Key.FullName} must depend on one of: {string.Join(", ", entry.Value.Select(type => type.Name))}")
            .ToArray();

        await Assert.That(violations).IsEmpty()
            .Because("guarded publication-policy mutations must enter through the coordinated boundary.");
    }

    [Test]
    public async Task GuardedMutationOwnersMustDeclareTypedBoundaryContracts()
    {
        Type[] mutationOwners =
        [
            typeof(SetControlPlaneTenantSettingCommandHandler),
            typeof(UpdateSettingCommandHandler),
            typeof(UpdateSettingBatchCommandHandler)
        ];
        string[] violations = mutationOwners
            .Where(type => !type.GetConstructors().Single().GetParameters()
                .Any(parameter => parameter.ParameterType == typeof(IPublicationPolicyMutationBoundary)))
            .Select(type => type.FullName!)
            .ToArray();
        MethodInfo[] boundaryMethods = typeof(IPublicationPolicyMutationBoundary).GetMethods();

        await Assert.That(violations).IsEmpty()
            .Because("generic setting APIs must receive the typed publication-policy boundary.");
        await Assert.That(boundaryMethods.Select(method => method.Name))
            .IsEquivalentTo(
            [
                nameof(IPublicationPolicyMutationBoundary.ApplyTenantAsync),
                nameof(IPublicationPolicyMutationBoundary.ApplyTenantInCurrentTransactionAsync),
                nameof(IPublicationPolicyMutationBoundary.ApplyInstanceAsync),
                nameof(IPublicationPolicyMutationBoundary.ApplyInstanceInCurrentTransactionAsync)
            ]);
        await Assert.That(boundaryMethods.All(method =>
                method.ReturnType == typeof(Task<PublicationPolicyMutationResult>)))
            .IsTrue();
    }

    [Test]
    public async Task GuardedTenantLockHandlersMustRejectPublicationPolicyKeys()
    {
        Guid tenantId = Guid.CreateVersion7();
        var currentUser = new FixedCurrentUserService(Guid.CreateVersion7());
        var lockHandler = new LockControlPlaneTenantSettingCommandHandler(
            null!, null!, null!, currentUser, null!, [], null!, null!, null!);
        var unlockHandler = new UnlockControlPlaneTenantSettingCommandHandler(
            null!, null!, null!, currentUser, null!, [], null!, null!, null!);

        foreach (string key in PublicationPolicySettingKeys.All)
        {
            BaseCommandResponse<Guid> lockResponse = await lockHandler.ExecuteAsync(
                new LockControlPlaneTenantSettingCommand(tenantId, key),
                CancellationToken.None);
            BaseCommandResponse<Guid> unlockResponse = await unlockHandler.ExecuteAsync(
                new UnlockControlPlaneTenantSettingCommand(tenantId, key),
                CancellationToken.None);

            await Assert.That(lockResponse.IsSuccess).IsFalse();
            await Assert.That(lockResponse.FailureCode).IsEqualTo("setting_not_lockable");
            await Assert.That(unlockResponse.IsSuccess).IsFalse();
            await Assert.That(unlockResponse.FailureCode).IsEqualTo("setting_not_lockable");
        }
    }

    [Test]
    public async Task GenericMutationEntryPointsMustRejectPublicationPolicyKeys()
    {
        await Assert.That(GuardedSettingRowWriteAllowlist.Length).IsEqualTo(1);
        await Assert.That(GuardedSettingRowWriteAllowlist.Single())
            .IsEqualTo(typeof(CoordinatedSettingMutationRepository));

        var tenantRepository = new TenantSettingRepository(null!, null!);
        var systemRepository = new SystemSettingRepository(null!, null!);
        var resolver = new HierarchicalSettingsResolver(
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);
        Guid tenantId = Guid.CreateVersion7();
        Guid actorId = Guid.CreateVersion7();

        foreach (string key in PublicationPolicySettingKeys.All)
        {
            Func<Task>[] attempts =
            [
                () => tenantRepository.SetValueAsync(tenantId, key, "true"),
                async () => await tenantRepository.RemoveOverrideAsync(tenantId, key),
                async () => await tenantRepository.LockAsync(tenantId, key, actorId),
                async () => await tenantRepository.UnlockAsync(tenantId, key, actorId),
                () => tenantRepository.UpsertManyForTenantAsync(
                    tenantId,
                    [new TenantSettingOverrideUpsert(key, "true", IsLocked: false)],
                    actorId),
                async () => await systemRepository.UpsertAsync(CreateSystemSetting(key)),
                async () => await systemRepository.UpsertLockAsync(CreateSystemSetting(key)),
                () => resolver.SetValueAsync(key, "true", SettingScope.Tenant, tenantId, actorId),
                () => resolver.RemoveOverrideAsync(key, SettingScope.Tenant, tenantId, actorId),
                () => resolver.LockAsync(key, SettingScope.Tenant, tenantId, actorId),
                () => resolver.UnlockAsync(key, SettingScope.Tenant, tenantId, actorId)
            ];

            foreach (Func<Task> attempt in attempts)
                await Assert.That(async () => await attempt()).Throws<InvalidOperationException>();
        }
    }

    [Test]
    [Arguments(PrimaryDatabaseProvider.PostgreSql)]
    [Arguments(PrimaryDatabaseProvider.Sqlite)]
    [Arguments(PrimaryDatabaseProvider.SqlServer)]
    [Arguments(PrimaryDatabaseProvider.MariaDb)]
    [Arguments(PrimaryDatabaseProvider.MySql)]
    public async Task PrimaryProviderCompositionsMustRegisterOneScopedBoundaryAndCoordinatedStore(
        PrimaryDatabaseProvider provider)
    {
        var services = new ServiceCollection();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = provider.ToString()
            })
            .Build();
        services.ConfigureApplicationServices(configuration);
        services.ConfigurePersistenceServices(
            configuration,
            skipDbContextRegistration: true,
            skipLookupCacheInitializer: true);

        ServiceDescriptor[] boundaryDescriptors = services
            .Where(descriptor => descriptor.ServiceType == typeof(IPublicationPolicyMutationBoundary))
            .ToArray();
        ServiceDescriptor[] storeDescriptors = services
            .Where(descriptor => descriptor.ServiceType == typeof(ICoordinatedSettingMutationStore))
            .ToArray();

        await Assert.That(boundaryDescriptors.Length).IsEqualTo(1);
        await Assert.That(boundaryDescriptors.Single().ImplementationType)
            .IsEqualTo(typeof(PublicationPolicyMutationBoundary));
        await Assert.That(boundaryDescriptors.Single().Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        await Assert.That(storeDescriptors.Length).IsEqualTo(1);
        await Assert.That(storeDescriptors.Single().ImplementationType)
            .IsEqualTo(typeof(CoordinatedSettingMutationRepository));
        await Assert.That(storeDescriptors.Single().Lifetime).IsEqualTo(ServiceLifetime.Scoped);
    }

    [Test]
    public async Task PublicationPolicyKeyRegistryMustContainExactlyTheFivePublicationKeys()
    {
        string[] guardedKeys = PublicationPolicySettingKeys.All.ToArray();

        await Assert.That(guardedKeys.Length).IsEqualTo(ExpectedPublicationPolicyKeys.Length);
        await Assert.That(guardedKeys.Order(StringComparer.Ordinal).SequenceEqual(
            ExpectedPublicationPolicyKeys.Order(StringComparer.Ordinal))).IsTrue();
        await Assert.That(guardedKeys.All(key => SettingRegistry.Get(key)?.RequiresCoordinatedMutation == true))
            .IsTrue();
    }

    [Test]
    public async Task EventResourceSettingsServicesAreRequiredScopedRegistrations()
    {
        var services = new ServiceCollection();
        services.ConfigureApplicationServices(new ConfigurationBuilder().Build());
        services.ConfigurePersistenceServices(
            new ConfigurationBuilder().Build(),
            skipDbContextRegistration: true,
            skipLookupCacheInitializer: true);

        ServiceDescriptor writer = services.Single(descriptor =>
            descriptor.ServiceType == typeof(IEventResourceSettingsWriter));
        ServiceDescriptor reader = services.Single(descriptor =>
            descriptor.ServiceType == typeof(IEventResourceGovernancePolicyReader));

        await Assert.That(writer.ImplementationType).IsEqualTo(typeof(EventResourceSettingsWriter));
        await Assert.That(writer.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        await Assert.That(reader.ImplementationType).IsEqualTo(typeof(EventResourceGovernancePolicyReader));
        await Assert.That(reader.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
    }

    [Test]
    public async Task PersistenceRegistration_ShouldNotExposeGenericSettingRepositories()
    {
        var services = new ServiceCollection();
        services.ConfigurePersistenceServices(
            new ConfigurationBuilder().Build(),
            skipDbContextRegistration: true,
            skipLookupCacheInitializer: true);

        bool hasOpenGeneric = services.Any(descriptor =>
            descriptor.ServiceType == typeof(IGenericRepository<,>));
        bool hasSystemSetting = services.Any(descriptor =>
            descriptor.ServiceType == typeof(IGenericRepository<SystemSetting, Guid>));
        bool hasTenantSetting = services.Any(descriptor =>
            descriptor.ServiceType == typeof(IGenericRepository<TenantSetting, Guid>));
        bool hasLegitimateClosedRepository = services.Any(descriptor =>
            descriptor.ServiceType == typeof(IGenericRepository<EventReportDecision, Guid>));

        using ServiceProvider provider = services.BuildServiceProvider();
        object? resolvedSystemSettingRepository = provider.GetService<IGenericRepository<SystemSetting, Guid>>();
        object? resolvedTenantSettingRepository = provider.GetService<IGenericRepository<TenantSetting, Guid>>();

        await Assert.That(hasOpenGeneric).IsFalse();
        await Assert.That(hasSystemSetting).IsFalse();
        await Assert.That(hasTenantSetting).IsFalse();
        await Assert.That(hasLegitimateClosedRepository).IsTrue();
        await Assert.That(resolvedSystemSettingRepository).IsNull();
        await Assert.That(resolvedTenantSettingRepository).IsNull();
    }

    private static SystemSetting CreateSystemSetting(string key) => new()
    {
        Id = Guid.CreateVersion7(),
        SettingKey = key,
        Value = "true"
    };

    private sealed class FixedCurrentUserService(Guid userId) : ICurrentUserService
    {
        public Guid? UserId => userId;
        public bool IsAuthenticated => true;
    }
}
