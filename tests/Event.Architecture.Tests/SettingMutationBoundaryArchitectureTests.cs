using System.Reflection;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application;
using Explore.Application.Features.InstanceAdmin.Handlers.Commands;
using Explore.Application.Features.Settings;
using Explore.Application.Features.Settings.Handlers.Commands;
using Explore.Application.Notifications;
using Explore.Application.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Settings;
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
            [typeof(SetInstanceTenantSettingCommandHandler)] = [typeof(IPublicationPolicyMutationBoundary)],
            [typeof(ApplyInstanceTenantPlanAssignmentCommandHandler)] = [typeof(ITypedSettingsOriginalStore)],
            [typeof(UpdateSettingCommandHandler)] = [typeof(IPublicationPolicyMutationBoundary)],
            [typeof(UpdateSettingBatchCommandHandler)] = [typeof(IPublicationPolicyMutationBoundary)],
            [typeof(ResetSettingCommandHandler)] = [typeof(IPublicationPolicyMutationBoundary)],
            [typeof(LockSettingCommandHandler)] = [typeof(IPublicationPolicyMutationBoundary)],
            [typeof(UnlockSettingCommandHandler)] = [typeof(IPublicationPolicyMutationBoundary)],
            [typeof(InstanceGovernanceSettingService)] = [typeof(SettingUpsertService)],
            [typeof(SettingUpsertService)] = [typeof(IPublicationPolicyMutationBoundary)]
        };

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
        MethodInfo[] methods = typeof(ITenantPolicySettingService).GetMethods()
            .Where(method => method.Name == nameof(ITenantPolicySettingService.ApplyTenantSettingsAsync)).ToArray();
        await Assert.That(methods.Length).IsEqualTo(2);
        foreach (var method in methods)
        {
            await Assert.That(method.ReturnType).IsEqualTo(typeof(Task<IReadOnlyList<SettingChangedNotification>>));
            await Assert.That(method.GetParameters().Last().ParameterType).IsEqualTo(typeof(CancellationToken));
        }
    }

    [Test]
    public async Task DirectInstanceSettingHandlers_ShouldRequireTrustedCurrentUserContext()
    {
        Type[] handlerTypes =
        [
            typeof(SetInstanceTenantSettingCommandHandler),
            typeof(LockInstanceTenantSettingCommandHandler),
            typeof(UnlockInstanceTenantSettingCommandHandler)
        ];

        string[] violations = handlerTypes
            .Where(type => !type.GetConstructors().Single().GetParameters()
                .Any(parameter => parameter.ParameterType == typeof(ICurrentUserService)))
            .Select(type => type.Name)
            .ToArray();

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task InstanceMutationHandlers_ShouldDeclarePostCommitSideEffectDependencies()
    {
        Type[] handlerTypes =
        [
            typeof(SetInstanceTenantSettingCommandHandler),
            typeof(LockInstanceTenantSettingCommandHandler),
            typeof(UnlockInstanceTenantSettingCommandHandler),
            typeof(ApplyInstanceTenantPlanAssignmentCommandHandler)
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
            typeof(SetInstanceTenantSettingCommandHandler),
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
                nameof(IPublicationPolicyMutationBoundary.ApplyInstanceInCurrentTransactionAsync),
                nameof(IPublicationPolicyMutationBoundary.ApplyTypedInCurrentTransactionAsync),
                nameof(IPublicationPolicyMutationBoundary.ApplyImportInCurrentTransactionAsync)
            ]);
        await Assert.That(boundaryMethods.All(method =>
                method.ReturnType == typeof(Task<PublicationPolicyMutationResult>)))
            .IsTrue();
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

        await Assert.That(writer.ImplementationType?.FullName)
            .IsEqualTo("Explore.Persistence.Services.SelectedEventResourceSettingsWriter");
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

}
