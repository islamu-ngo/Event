using System.Reflection;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application;
using Explore.Application.Features.ControlPlane.Handlers.Commands;
using Explore.Application.Features.Settings.Handlers.Commands;
using Explore.Application.Notifications;
using Explore.Application.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Repositories;
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

    private static readonly IReadOnlyDictionary<Type, string[]> GuardedKeyRejectionEntryPoints =
        new Dictionary<Type, string[]>
        {
            [typeof(TenantSettingRepository)] =
            [
                "SetValueAsync",
                "RemoveOverrideAsync",
                "LockAsync",
                "UnlockAsync",
                "UpsertManyForTenantAsync"
            ],
            [typeof(SystemSettingRepository)] = ["UpsertAsync", "UpsertLockAsync"],
            [typeof(HierarchicalSettingsResolver)] =
            ["SetValueAsync", "RemoveOverrideAsync", "LockAsync", "UnlockAsync"]
        };

    private static readonly Type[] GuardedTenantLockRejectionOwners =
    [
        typeof(LockControlPlaneTenantSettingCommandHandler),
        typeof(UnlockControlPlaneTenantSettingCommandHandler)
    ];

    private static readonly Type[] GuardedSettingRowWriteAllowlist =
    [
        typeof(CoordinatedSettingMutationRepository)
    ];

    private static readonly string[] CanonicalPublicationPolicyKeys =
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
            .Because("tenant policy batches must acquire all canonical setting locks in deterministic order");
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
                .Any(parameter => parameter.ParameterType == typeof(MediatR.IMediator)))
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
    public async Task GuardedMutationOwnersMustDispatchGuardedKeysBeforeUsingGenericMutationApis()
    {
        Type[] genericMutationOwners =
        [
            typeof(SetControlPlaneTenantSettingCommandHandler),
            typeof(UpdateSettingCommandHandler),
            typeof(UpdateSettingBatchCommandHandler)
        ];
        string[] violations = genericMutationOwners
            .Where(type =>
            {
                string source = ReadSource(type);
                return !source.Contains("PublicationPolicySettingKeys", StringComparison.Ordinal)
                    || !source.Contains("IPublicationPolicyMutationBoundary", StringComparison.Ordinal);
            })
            .Select(type => type.FullName!)
            .ToArray();

        await Assert.That(violations).IsEmpty()
            .Because("generic setting APIs may still resolve or persist unguarded keys, but must dispatch guarded keys to the boundary first.");
    }

    [Test]
    public async Task GuardedTenantLockHandlersMustRejectGuardedKeysBeforeLockAcquisition()
    {
        string[] violations = GuardedTenantLockRejectionOwners
            .Where(type =>
            {
                string source = ReadSource(type);
                int guardedKeyPreflight = source.IndexOf(
                    "PublicationPolicySettingKeys.All.Contains",
                    StringComparison.Ordinal);
                int lockAcquisition = source.IndexOf("mutationLock.ExecuteAsync", StringComparison.Ordinal);
                if (guardedKeyPreflight < 0 || lockAcquisition < 0 || guardedKeyPreflight > lockAcquisition)
                    return true;

                string preflight = source[guardedKeyPreflight..lockAcquisition];
                return !preflight.Contains("return ControlPlaneTenantSettingSecurity.Failure", StringComparison.Ordinal)
                    || !preflight.Contains("\"setting_not_lockable\"", StringComparison.Ordinal);
            })
            .Select(type => type.FullName!)
            .ToArray();

        await Assert.That(violations).IsEmpty()
            .Because("tenant lock handlers must reject coordinated publication-policy keys before generic lock acquisition.");
    }

    [Test]
    public async Task OnlyCoordinatedRepositoryMayWriteGuardedSettingRows()
    {
        await Assert.That(GuardedSettingRowWriteAllowlist.Length).IsEqualTo(1);
        await Assert.That(GuardedSettingRowWriteAllowlist.Single())
            .IsEqualTo(typeof(CoordinatedSettingMutationRepository));

        string[] violations = GuardedKeyRejectionEntryPoints
            .SelectMany(entry => entry.Value
                .Where(methodName =>
                {
                    string methodBody = ReadMethodBody(ReadSource(entry.Key), methodName);
                    return !methodBody.Contains("PublicationPolicySettingKeys", StringComparison.Ordinal)
                        || !methodBody.Contains("throw", StringComparison.Ordinal);
                })
                .Select(methodName => $"{entry.Key.FullName}.{methodName}"))
            .ToArray();

        await Assert.That(violations).IsEmpty()
            .Because("regular setting repositories and the hierarchical resolver must reject guarded keys instead of writing their rows directly.");
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
    public async Task PublicationPolicyKeyRegistryMustContainExactlyTheFiveCoordinatedKeys()
    {
        string[] guardedKeys = PublicationPolicySettingKeys.All.ToArray();
        string[] registryKeys = SettingRegistry.All
            .Where(definition => definition.RequiresCoordinatedMutation)
            .Select(definition => definition.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(guardedKeys.Length).IsEqualTo(CanonicalPublicationPolicyKeys.Length);
        await Assert.That(guardedKeys.Order(StringComparer.Ordinal).SequenceEqual(
            CanonicalPublicationPolicyKeys.Order(StringComparer.Ordinal))).IsTrue();
        await Assert.That(registryKeys.SequenceEqual(CanonicalPublicationPolicyKeys.Order(StringComparer.Ordinal))).IsTrue();
        await Assert.That(guardedKeys.All(key => SettingRegistry.Get(key)?.RequiresCoordinatedMutation == true))
            .IsTrue();
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

    private static string ReadSource(Type type) => type switch
    {
        _ when type == typeof(TenantSettingRepository) => File.ReadAllText(ContextSystemHelpers.RepoPath(
            "Explore.Persistence", "Repositories", "TenantSettingRepository.cs")),
        _ when type == typeof(SystemSettingRepository) => File.ReadAllText(ContextSystemHelpers.RepoPath(
            "Explore.Persistence", "Repositories", "SystemSettingRepository.cs")),
        _ when type == typeof(HierarchicalSettingsResolver) => File.ReadAllText(ContextSystemHelpers.RepoPath(
            "Explore.Infrastructure", "Services", "HierarchicalSettingsResolver.cs")),
        _ when type == typeof(SetControlPlaneTenantSettingCommandHandler) => File.ReadAllText(ContextSystemHelpers.RepoPath(
            "Explore.Application", "Features", "ControlPlane", "Handlers", "Commands", "SetControlPlaneTenantSettingCommandHandler.cs")),
        _ when type == typeof(LockControlPlaneTenantSettingCommandHandler) => File.ReadAllText(ContextSystemHelpers.RepoPath(
            "Explore.Application", "Features", "ControlPlane", "Handlers", "Commands", "LockControlPlaneTenantSettingCommandHandler.cs")),
        _ when type == typeof(UnlockControlPlaneTenantSettingCommandHandler) => File.ReadAllText(ContextSystemHelpers.RepoPath(
            "Explore.Application", "Features", "ControlPlane", "Handlers", "Commands", "UnlockControlPlaneTenantSettingCommandHandler.cs")),
        _ when type == typeof(UpdateSettingCommandHandler) => File.ReadAllText(ContextSystemHelpers.RepoPath(
            "Explore.Application", "Features", "Settings", "Handlers", "Commands", "UpdateSettingCommandHandler.cs")),
        _ when type == typeof(UpdateSettingBatchCommandHandler) => File.ReadAllText(ContextSystemHelpers.RepoPath(
            "Explore.Application", "Features", "Settings", "Handlers", "Commands", "UpdateSettingBatchCommandHandler.cs")),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "No focused source path is registered.")
    };

    private static string ReadMethodBody(string source, string methodName)
    {
        int methodStart = source.IndexOf($" {methodName}(", StringComparison.Ordinal);
        if (methodStart < 0)
            throw new InvalidOperationException($"Method '{methodName}' was not found.");

        int bodyStart = source.IndexOf('{', methodStart);
        if (bodyStart < 0)
            throw new InvalidOperationException($"Method '{methodName}' has no body.");

        int depth = 0;
        for (int index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
                depth++;
            else if (source[index] == '}' && --depth == 0)
                return source[bodyStart..(index + 1)];
        }

        throw new InvalidOperationException($"Method '{methodName}' body is not balanced.");
    }
}
