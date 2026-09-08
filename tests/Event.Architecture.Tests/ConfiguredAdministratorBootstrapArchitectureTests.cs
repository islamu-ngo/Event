namespace Event.Architecture.Tests;

using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Explore.API.Hosting;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Services;
using Explore.Domain;
using Explore.Infrastructure;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Secrets.Database;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ApplicationOnboardingStatus = Explore.Application.DTOs.Onboarding.InstanceOnboardingStatusDto;
using GeneratedOnboardingStatus = Explore.Blazor.Client.Clients.InstanceOnboardingStatusDto;

public sealed class ConfiguredAdministratorBootstrapArchitectureTests
{
    private static readonly Dictionary<short, OpCode> OpCodesByValue =
        typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(opCode => opCode.Value);

    private static readonly string[] OutwardAssemblyPrefixes =
    [
        "Explore.API",
        "Explore.Blazor",
        "Explore.Infrastructure",
        "Explore.Persistence",
        "Event.Setup"
    ];

    private static readonly string[] SensitiveStatusPropertyNames =
    [
        "Email",
        "Subject",
        "Issuer",
        "Did",
        "Identity",
        "Selector",
        "ConfigurationFingerprint",
        "SelectorFingerprint",
        "CompletedIdentityFingerprint",
        "FirstName",
        "LastName",
        "ProviderKey",
        "ProviderSubject"
    ];

    [Test]
    public async Task DomainRemainsPureAndBootstrapLifecycleStateRemainsPrivate()
    {
        Assembly domain = typeof(InstanceBootstrapState).Assembly;
        string[] forbiddenReferences = domain.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => OutwardAssemblyPrefixes.Any(prefix =>
                name.StartsWith(prefix, StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal)
            .ToArray();
        PropertyInfo[] lifecycleProperties = typeof(InstanceBootstrapState)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance);

        await Assert.That(forbiddenReferences).IsEmpty();
        await Assert.That(typeof(InstanceBootstrapState).GetConstructors(
            BindingFlags.Public | BindingFlags.Instance)).IsEmpty();
        await Assert.That(lifecycleProperties).IsNotEmpty();
        await Assert.That(lifecycleProperties).All(property =>
            property.GetSetMethod(nonPublic: true)?.IsPrivate == true);
    }

    [Test]
    public async Task ApplicationHasNoOutwardOrSetupDependency()
    {
        Assembly application = typeof(ProviderAccountKey).Assembly;
        string[] forbiddenReferences = application.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => OutwardAssemblyPrefixes.Any(prefix =>
                name.StartsWith(prefix, StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(forbiddenReferences).IsEmpty();
    }

    [Test]
    public async Task SetupCoreAndAssistantRemainOfflineAndDisconnectedFromRuntimeLayers()
    {
        string coreProject = ContextSystemHelpers.RepoPath(
            "src", "Event.Setup.Core", "Event.Setup.Core.csproj");
        string assistantProject = ContextSystemHelpers.RepoPath(
            "src", "Event.SetupAssistant", "Event.SetupAssistant.csproj");

        await Assert.That(ProjectReferences(coreProject))
            .IsEquivalentTo(["Event.Wire.Contracts"]);
        await Assert.That(ProjectReferences(assistantProject))
            .IsEquivalentTo(["Event.Setup.Core"]);

        string[] forbidden = ProjectClosure(coreProject, assistantProject)
            .Where(name => name is
                "Explore.API" or
                "Explore.Application" or
                "Explore.Domain" or
                "Explore.Infrastructure" or
                "Explore.Persistence" or
                "Explore.Blazor" or
                "Explore.Blazor.Client")
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(forbidden).IsEmpty();
    }

    [Test]
    public async Task ConfigurationManifestMachineContractExcludesBootstrapIdentityAuthority()
    {
        string[] forbiddenContractProperties = ReachableContractTypes(
                typeof(ConfigurationManifestV1Alpha2))
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Select(property => property.Name)
            .Where(name => new[]
            {
                "Administrator", "ProviderAccount", "ProviderSubject", "Issuer",
                "Subject", "Selector", "Fingerprint", "Bootstrap"
            }.Any(term => name.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .Order(StringComparer.Ordinal)
            .ToArray();
        ConfigurationPortabilitySectionDescriptor providerBindings =
            ConfigurationPortabilityRegistry.Sections["excluded.provider_bindings"];
        ConfigurationPortabilitySectionDescriptor pii =
            ConfigurationPortabilityRegistry.Sections["excluded.pii"];

        await Assert.That(forbiddenContractProperties).IsEmpty();
        await Assert.That(providerBindings.Scope).IsEqualTo(ConfigurationPortabilityScope.Excluded);
        await Assert.That(providerBindings.Authority).IsEqualTo(ConfigurationPortabilityAuthority.None);
        await Assert.That(providerBindings.ArtifactKinds).IsEmpty();
        await Assert.That(providerBindings.SupportsApply).IsFalse();
        await Assert.That(pii.Scope).IsEqualTo(ConfigurationPortabilityScope.Excluded);
        await Assert.That(pii.ArtifactKinds).IsEmpty();

        byte[] canonical = ConfigurationPortabilityJsonCodec.SerializeConfigurationManifest(
            CreateEmptyManifest());
        JsonObject root = JsonNode.Parse(canonical)!.AsObject();
        root["configuredAdministrator"] = new JsonObject
        {
            ["provider"] = "keycloak",
            ["subject"] = "machine-contract-canary"
        };
        byte[] smuggled = JsonSerializer.SerializeToUtf8Bytes(root);

        await Assert.That(() =>
                ConfigurationPortabilityJsonCodec.ParseConfigurationManifest(smuggled))
            .Throws<ConfigurationPortabilityContractException>();
    }

    [Test]
    public async Task ProviderAccountKeyIsTheSingleAuthorityAndRepositoriesExposeNoRawKeyOverload()
    {
        Assembly[] owningAssemblies =
        [
            typeof(InstanceBootstrapState).Assembly,
            typeof(ProviderAccountKey).Assembly,
            typeof(UserExternalLoginRepository).Assembly,
            typeof(ConfiguredAdministratorBootstrapProvider).Assembly
        ];
        Type[] authorities = owningAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.Name == nameof(ProviderAccountKey))
            .Distinct()
            .ToArray();
        MethodInfo[] repositoryLookups = new[]
            {
                typeof(IUserExternalLoginRepository),
                typeof(UserExternalLoginRepository)
            }
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Where(method => method.Name.StartsWith("GetByProvider", StringComparison.Ordinal))
            .ToArray();

        await Assert.That(authorities).IsEquivalentTo([typeof(ProviderAccountKey)]);
        await Assert.That(typeof(ProviderAccountKey).IsSealed).IsTrue();
        await Assert.That(repositoryLookups).Count().IsEqualTo(2);
        await Assert.That(repositoryLookups).All(method =>
            method.GetParameters().Count(parameter =>
                parameter.ParameterType == typeof(ProviderAccountKey)) == 1);
        await Assert.That(repositoryLookups).All(method =>
            !method.GetParameters().Any(parameter =>
                parameter.ParameterType == typeof(string)));
    }

    [Test]
    public async Task BffAndClientCannotReferenceDomainAndStartupRoutingDoesNotUseRolesOrLifecycleMutation()
    {
        string bffProject = ContextSystemHelpers.RepoPath(
            "src", "Explore.Blazor", "Explore.Blazor.csproj");
        string clientProject = ContextSystemHelpers.RepoPath(
            "src", "Explore.Blazor.Client", "Explore.Blazor.Client.csproj");
        string[] forbiddenProjects = ProjectClosure(bffProject, clientProject)
            .Where(name => name is
                "Explore.Domain" or
                "Explore.Application" or
                "Explore.Infrastructure" or
                "Explore.Persistence" or
                "Explore.API")
            .Order(StringComparer.Ordinal)
            .ToArray();
        MethodBase[] startupCalls = CallsFromTypeAndStateMachines(typeof(StartupRoutingService));
        string[] forbiddenCalls = startupCalls
            .Where(method => method.Name is
                "IsInRole" or
                "CompleteInteractive" or
                "CompleteConfiguredAdministrator" or
                "Supersede")
            .Select(Describe)
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(forbiddenProjects).IsEmpty();
        await Assert.That(forbiddenCalls).IsEmpty();
        await Assert.That(typeof(StartupRoutingService).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name))
            .DoesNotContain("Explore.Domain");
    }

    [Test]
    public async Task GeneratedClientOwnsBrowserStatusAndGenerationPipeline()
    {
        await Assert.That(typeof(GeneratedOnboardingStatus).Assembly)
            .IsEqualTo(typeof(IInstanceOnboardingClient).Assembly);
        await Assert.That(typeof(GeneratedOnboardingStatus).Assembly)
            .IsEqualTo(typeof(StartupRoutingService).Assembly);
        await Assert.That(typeof(GeneratedOnboardingStatus).Assembly)
            .IsNotEqualTo(typeof(ApplicationOnboardingStatus).Assembly);

        string[] requiredProperties =
        [
            "IsCompleted", "State", "Mode", "Provider", "Generation",
            "IsAuthenticated", "IsCurrentUserInstanceAdmin", "SelectedDeploymentMode"
        ];
        string[] generatedProperties = typeof(GeneratedOnboardingStatus)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToArray();
        await Assert.That(requiredProperties)
            .All(required => generatedProperties.Contains(required, StringComparer.Ordinal));

        XDocument project = XDocument.Load(ContextSystemHelpers.RepoPath(
            "src", "Explore.Blazor.Client", "Explore.Blazor.Client.csproj"));
        XElement generationTarget = ProjectTarget(project, "GenerateApiClient");
        XElement generatedCompile = project.Descendants()
            .Single(element => element.Name.LocalName == "Compile"
                && string.Equals(
                    element.Attribute("Update")?.Value,
                    "Clients\\EventApiTagClients.g.cs",
                    StringComparison.Ordinal));

        await Assert.That(generationTarget.Attribute("BeforeTargets")?.Value)
            .IsEqualTo("CoreCompile");
        await Assert.That(generationTarget.Attribute("DependsOnTargets")?.Value)
            .IsEqualTo("NormalizeGeneratedApiClient");
        await Assert.That(generatedCompile.Elements()
            .Single(element => element.Name.LocalName == "AutoGen").Value)
            .IsEqualTo("true");
    }

    [Test]
    public async Task EnvironmentProviderAndStartupRunnerAreRegisteredAndComposed()
    {
        var services = new ServiceCollection();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection()
            .Build();
        services.ConfigureInfrastructureServices(configuration);

        ServiceDescriptor[] providerRegistrations = services
            .Where(descriptor => descriptor.ServiceType ==
                typeof(IConfiguredAdministratorBootstrapProvider))
            .ToArray();
        ServiceDescriptor[] concreteProviders = services
            .Where(descriptor => descriptor.ServiceType ==
                typeof(ConfiguredAdministratorBootstrapProvider))
            .ToArray();
        ServiceDescriptor[] disabledProviders = services
            .Where(descriptor => descriptor.ImplementationType ==
                typeof(DisabledConfiguredAdministratorBootstrapProvider))
            .ToArray();
        ServiceDescriptor[] runners = services
            .Where(descriptor => descriptor.ServiceType ==
                typeof(ConfiguredAdministratorBootstrapStartupRunner))
            .ToArray();

        await Assert.That(providerRegistrations).Count().IsEqualTo(1);
        await Assert.That(providerRegistrations.Single().Lifetime)
            .IsEqualTo(ServiceLifetime.Scoped);
        await Assert.That(concreteProviders).Count().IsEqualTo(1);
        await Assert.That(disabledProviders).IsEmpty();
        await Assert.That(runners).Count().IsEqualTo(1);
        await Assert.That(runners.Single().Lifetime).IsEqualTo(ServiceLifetime.Scoped);

        MethodBase[] startupCalls = CallsFromTypeAndStateMachines(typeof(ApiHostStartupExtensions));
        await Assert.That(startupCalls).Contains(method =>
            method.DeclaringType == typeof(ConfiguredAdministratorBootstrapStartupRunner)
            && method.Name == nameof(ConfiguredAdministratorBootstrapStartupRunner.PrepareAsync));

        Type[] runnerDependencies = typeof(ConfiguredAdministratorBootstrapStartupRunner)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();
        await Assert.That(runnerDependencies).IsEquivalentTo(
        [
            typeof(ConfiguredAdministratorBootstrapProvider),
            typeof(IInstanceBootstrapStateRepository),
            typeof(IUnitOfWork),
            typeof(TimeProvider)
        ]);
    }

    [Test]
    public async Task OperatorAndBrowserStatusShapesRemainValueFree()
    {
        Type[] statusTypes =
        [
            typeof(ApplicationOnboardingStatus),
            typeof(GeneratedOnboardingStatus),
            typeof(InstanceOnboardingStartupStatus)
        ];
        string[] sensitiveProperties = statusTypes
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => SensitiveStatusPropertyNames.Contains(
                    property.Name,
                    StringComparer.OrdinalIgnoreCase))
                .Select(property => $"{type.FullName}.{property.Name}"))
            .Order(StringComparer.Ordinal)
            .ToArray();
        PropertyInfo[] accountKeyProperties = statusTypes
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Where(property => property.PropertyType == typeof(ProviderAccountKey))
            .ToArray();

        await Assert.That(sensitiveProperties).IsEmpty();
        await Assert.That(accountKeyProperties).IsEmpty();
    }

    [Test]
    [Arguments(PrimaryDatabaseProvider.PostgreSql, "ConfigurableNpgsqlMigrationsSqlGenerator")]
    [Arguments(PrimaryDatabaseProvider.Sqlite, "ConfigurableSqliteMigrationsSqlGenerator")]
    [Arguments(PrimaryDatabaseProvider.SqlServer, "ConfigurableSqlServerMigrationsSqlGenerator")]
    [Arguments(PrimaryDatabaseProvider.MariaDb, "ConfigurableMySqlMigrationsSqlGenerator")]
    [Arguments(PrimaryDatabaseProvider.MySql, "ConfigurableMySqlMigrationsSqlGenerator")]
    public async Task EveryGeneratedMigrationProviderRegistersBootstrapBackfillGenerator(
        PrimaryDatabaseProvider provider,
        string expectedGeneratorName)
    {
        var builder = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(
            builder,
            CreateDatabaseOptions(provider));
        await using var context = new ExploreDbContext(builder.Options);
        IMigrationsSqlGenerator generator = context.GetService<IMigrationsSqlGenerator>();
        Type generatorType = generator.GetType();
        MethodInfo generate = generatorType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(method => method.Name == nameof(IMigrationsSqlGenerator.Generate)
                && method.DeclaringType == generatorType);
        MethodBase[] calls = ReadCalledMethods(generate).ToArray();

        await Assert.That(generatorType.Name).IsEqualTo(expectedGeneratorName);
        await Assert.That(calls).Contains(method =>
            method.DeclaringType?.Name == "ConfigurableSchemaMigrationOperations"
            && method.Name == "PrepareInstanceBootstrapLifecycleBackfill");
    }

    [Test]
    [Arguments(PrimaryDatabaseProvider.PostgreSql)]
    [Arguments(PrimaryDatabaseProvider.Sqlite)]
    [Arguments(PrimaryDatabaseProvider.SqlServer)]
    [Arguments(PrimaryDatabaseProvider.MariaDb)]
    [Arguments(PrimaryDatabaseProvider.MySql)]
    public async Task ConfiguredAdministratorBootstrapDowngradePreservesLegacyValuesBeforeDroppingTypedColumns(
        PrimaryDatabaseProvider provider)
    {
        var builder = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(
            builder,
            CreateDatabaseOptions(provider));
        await using var context = new ExploreDbContext(builder.Options);
        string[] migrations = context.Database.GetMigrations().ToArray();
        string? migration = migrations.SingleOrDefault(candidate =>
            candidate.EndsWith("_AddConfiguredAdministratorBootstrapState", StringComparison.Ordinal));
        if (migration is null)
        {
            return;
        }
        int migrationIndex = Array.IndexOf(migrations, migration);
        string previousMigration = migrations[migrationIndex - 1];
        string script = context.GetService<IMigrator>().GenerateScript(migration, previousMigration);
        string forwardScript = context.GetService<IMigrator>().GenerateScript(previousMigration, migration);
        string quote = provider switch
        {
            PrimaryDatabaseProvider.SqlServer => "[]",
            PrimaryDatabaseProvider.MariaDb or PrimaryDatabaseProvider.MySql => "`",
            _ => "\""
        };
        string isCompleted = QuotedIdentifier("is_completed", quote);
        string selectedDeploymentMode = QuotedIdentifier("selected_deployment_mode", quote);
        string status = QuotedIdentifier("status", quote);
        string deploymentMode = QuotedIdentifier("deployment_mode", quote);
        string completedValue = provider == PrimaryDatabaseProvider.PostgreSql ? "TRUE" : "1";
        string incompleteValue = provider == PrimaryDatabaseProvider.PostgreSql ? "FALSE" : "0";
        int addLegacyPosition = SqlStatementPosition(script, "ADD", "is_completed", 0);
        int addSelectedDeploymentModePosition = SqlStatementPosition(script, "ADD", "selected_deployment_mode", 0);
        int backfillPosition = script.IndexOf("headless-instance-bootstrap-downgrade-backfill", StringComparison.Ordinal);
        int dropTypedPosition = provider == PrimaryDatabaseProvider.Sqlite
            ? script.IndexOf("CREATE TABLE \"ef_temp_ie_instance_bootstrap_states\"", backfillPosition, StringComparison.Ordinal)
            : SqlStatementPosition(script, "DROP", "status", backfillPosition);
        int dropTypedDeploymentModePosition = provider == PrimaryDatabaseProvider.Sqlite
            ? dropTypedPosition
            : SqlStatementPosition(script, "DROP", "deployment_mode", backfillPosition);
        int addTypedPosition = SqlStatementPosition(forwardScript, "ADD", "status", 0);
        int addTypedDeploymentModePosition = SqlStatementPosition(forwardScript, "ADD", "deployment_mode", 0);
        int forwardBackfillPosition = forwardScript.IndexOf("headless-instance-bootstrap-backfill", StringComparison.Ordinal);
        int dropLegacyPosition = provider == PrimaryDatabaseProvider.Sqlite
            ? forwardScript.IndexOf("CREATE TABLE \"ef_temp_ie_instance_bootstrap_states\"", forwardBackfillPosition, StringComparison.Ordinal)
            : SqlStatementPosition(forwardScript, "DROP", "is_completed", forwardBackfillPosition);
        int dropSelectedDeploymentModePosition = provider == PrimaryDatabaseProvider.Sqlite
            ? dropLegacyPosition
            : SqlStatementPosition(forwardScript, "DROP", "selected_deployment_mode", forwardBackfillPosition);

        await Assert.That(addLegacyPosition).IsGreaterThanOrEqualTo(0);
        await Assert.That(addSelectedDeploymentModePosition).IsGreaterThanOrEqualTo(0);
        await Assert.That(backfillPosition).IsGreaterThan(addLegacyPosition);
        await Assert.That(backfillPosition).IsGreaterThan(addSelectedDeploymentModePosition);
        await Assert.That(dropTypedPosition).IsGreaterThan(backfillPosition);
        await Assert.That(dropTypedDeploymentModePosition).IsGreaterThan(backfillPosition);
        await Assert.That(script).Contains(
            $"{isCompleted} = CASE WHEN {status} = 3 THEN {completedValue} ELSE {incompleteValue} END");
        await Assert.That(script).Contains(
            $"{selectedDeploymentMode} = CASE WHEN {deploymentMode} = 2 THEN 'MultiTenant' ELSE 'SingleTenant' END");
        await Assert.That(addTypedPosition).IsGreaterThanOrEqualTo(0);
        await Assert.That(addTypedDeploymentModePosition).IsGreaterThanOrEqualTo(0);
        await Assert.That(forwardBackfillPosition).IsGreaterThan(addTypedPosition);
        await Assert.That(forwardBackfillPosition).IsGreaterThan(addTypedDeploymentModePosition);
        await Assert.That(dropLegacyPosition).IsGreaterThan(forwardBackfillPosition);
        await Assert.That(dropSelectedDeploymentModePosition).IsGreaterThan(forwardBackfillPosition);
        if (provider == PrimaryDatabaseProvider.Sqlite)
        {
            string rebuiltTableDefinition = script[dropTypedPosition..script.IndexOf(");", dropTypedPosition, StringComparison.Ordinal)];
            string forwardRebuiltTableDefinition = forwardScript[dropLegacyPosition..forwardScript.IndexOf(");", dropLegacyPosition, StringComparison.Ordinal)];
            await Assert.That(rebuiltTableDefinition).DoesNotContain("\"status\"");
            await Assert.That(rebuiltTableDefinition).DoesNotContain("\"deployment_mode\"");
            await Assert.That(forwardRebuiltTableDefinition).DoesNotContain("\"is_completed\"");
            await Assert.That(forwardRebuiltTableDefinition).DoesNotContain("\"selected_deployment_mode\"");
        }
    }

    private static ConfigurationManifestV1Alpha2 CreateEmptyManifest() => new()
    {
        Schema = ConfigurationManifestContractMetadata.SchemaId,
        ApiVersion = ConfigurationManifestContractMetadata.ApiVersion,
        Kind = ConfigurationManifestContractMetadata.Kind,
        Metadata = new ConfigurationManifestMetadataV1Alpha2
        {
            Name = "architecture-contract"
        },
        Spec = new ConfigurationManifestSpecV1Alpha2
        {
            Instance = new ConfigurationManifestInstanceV1Alpha2
            {
                Settings = new Dictionary<string, JsonElement>(),
                Documents = new Dictionary<string, ConfigurationManifestDocumentV1Alpha2>(),
                LegalDocuments = new Dictionary<string, ConfigurationManifestLegalDocumentV1Alpha2>()
            },
            Tenants = Array.Empty<ConfigurationManifestTenantV1Alpha2>()
        }
    };

    private static IEnumerable<Type> ReachableContractTypes(Type root)
    {
        var visited = new HashSet<Type>();
        var pending = new Stack<Type>();
        pending.Push(root);
        while (pending.TryPop(out Type? current))
        {
            current = UnwrapContractType(current);
            if (!visited.Add(current)
                || current.Assembly != typeof(ConfigurationManifestV1Alpha2).Assembly)
            {
                continue;
            }

            yield return current;
            foreach (PropertyInfo property in current.GetProperties(
                BindingFlags.Public | BindingFlags.Instance))
            {
                pending.Push(property.PropertyType);
            }
        }
    }

    private static Type UnwrapContractType(Type type)
    {
        Type? nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null)
        {
            return nullable;
        }

        if (type.IsArray)
        {
            return type.GetElementType()!;
        }

        if (type.IsGenericType)
        {
            Type[] arguments = type.GetGenericArguments();
            return arguments[^1];
        }

        return type;
    }

    private static string[] ProjectReferences(string projectPath) =>
        XDocument.Load(projectPath).Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
            .Select(path => Path.GetFileNameWithoutExtension(path.Replace('\\', '/')))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static IEnumerable<string> ProjectClosure(params string[] projectPaths)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>(projectPaths.Select(Path.GetFullPath));
        while (pending.TryPop(out string? projectPath))
        {
            if (!visited.Add(projectPath))
            {
                continue;
            }

            yield return Path.GetFileNameWithoutExtension(projectPath);
            XDocument project = XDocument.Load(projectPath);
            foreach (string include in project.Descendants()
                .Where(element => element.Name.LocalName == "ProjectReference")
                .Select(element => element.Attribute("Include")?.Value)
                .OfType<string>()
                .Where(include => !include.Contains("$(", StringComparison.Ordinal)))
            {
                string referenced = Path.GetFullPath(Path.Combine(
                    Path.GetDirectoryName(projectPath)!,
                    include));
                if (File.Exists(referenced))
                {
                    pending.Push(referenced);
                }
            }
        }
    }

    private static XElement ProjectTarget(XDocument project, string name) =>
        project.Descendants().Single(element =>
            element.Name.LocalName == "Target"
            && string.Equals(element.Attribute("Name")?.Value, name, StringComparison.Ordinal));

    private static MethodBase[] CallsFromTypeAndStateMachines(Type owner) =>
        owner.Assembly.GetTypes()
            .Where(type => type == owner || IsNestedBelow(type, owner))
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .SelectMany(ReadCalledMethods)
            .Distinct()
            .ToArray();

    private static bool IsNestedBelow(Type candidate, Type owner)
    {
        for (Type? declaring = candidate.DeclaringType;
             declaring is not null;
             declaring = declaring.DeclaringType)
        {
            if (declaring == owner)
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<MethodBase> ReadCalledMethods(MethodBase caller)
    {
        MethodBody? body = caller.GetMethodBody();
        byte[] il = body?.GetILAsByteArray() ?? [];
        int offset = 0;
        while (offset < il.Length)
        {
            OpCode opCode = ReadOpCode(il, ref offset);
            if (opCode.OperandType == OperandType.InlineMethod)
            {
                int token = BitConverter.ToInt32(il, offset);
                MethodBase? called = caller.Module.ResolveMethod(
                    token,
                    caller.DeclaringType?.GetGenericArguments(),
                    caller.IsGenericMethod ? caller.GetGenericArguments() : null);
                if (called is not null)
                {
                    yield return called;
                }
            }

            offset += OperandSize(opCode.OperandType, il, offset);
        }
    }

    private static OpCode ReadOpCode(byte[] il, ref int offset)
    {
        byte first = il[offset++];
        short value = first == 0xFE
            ? unchecked((short)(0xFE00 | il[offset++]))
            : first;
        return OpCodesByValue[value];
    }

    private static int OperandSize(OperandType operandType, byte[] il, int offset) =>
        operandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or
                OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineBrTarget or OperandType.InlineField or
                OperandType.InlineI or OperandType.InlineMethod or
                OperandType.InlineSig or OperandType.InlineString or
                OperandType.InlineTok or OperandType.InlineType or
                OperandType.ShortInlineR => 4,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch => 4 + BitConverter.ToInt32(il, offset) * 4,
            _ => throw new InvalidOperationException(
                $"Unsupported IL operand type {operandType}.")
        };

    private static string Describe(MethodBase method) =>
        $"{method.DeclaringType?.FullName}.{method.Name}";

    private static PrimaryDatabaseConnectionOptions CreateDatabaseOptions(
        PrimaryDatabaseProvider provider)
    {
        if (provider == PrimaryDatabaseProvider.Sqlite)
        {
            return new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Migrator,
                Provider = provider,
                Database = "configured-bootstrap-architecture.db"
            };
        }

        PrimaryDatabaseServerFlavor? flavor =
            Enum.TryParse(provider.ToString(), out PrimaryDatabaseServerFlavor parsed)
                ? parsed
                : null;
        return new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Migrator,
            Provider = provider,
            Host = "database.example.test",
            Database = "event_db",
            Username = "migration_user",
            Password = "test-only-password",
            TlsMode = PrimaryDatabaseTlsMode.Required,
            ServerFlavor = flavor,
            ServerVersion = flavor is null ? null : new Version(11, 4)
        };
    }

    private static string QuotedIdentifier(string identifier, string quote) => quote == "[]"
        ? $"[{identifier}]"
        : $"{quote}{identifier}{quote}";

    private static int SqlStatementPosition(
        string script,
        string operation,
        string identifier,
        int startIndex)
    {
        int offset = startIndex;
        foreach (string statement in script[startIndex..].Split(';'))
        {
            if (statement.Contains(operation, StringComparison.OrdinalIgnoreCase)
                && statement.Contains(identifier, StringComparison.Ordinal))
            {
                return offset;
            }

            offset += statement.Length + 1;
        }

        return -1;
    }
}
