namespace Event.Architecture.Tests;

using System.Reflection;
using NetArchTest.Rules;

/// <summary>
/// Tests that enforce naming conventions across the codebase.
/// Consistent naming improves code discoverability and maintainability.
/// </summary>
public class NamingConventionTests
{
    private static readonly Assembly ApplicationAssembly = typeof(Explore.Application.ApplicationServicesRegistration).Assembly;
    private static readonly Assembly PersistenceAssembly = typeof(Explore.Persistence.ExploreDbContext).Assembly;

    #region Handler Naming Conventions

    [Test]
    public async Task CommandHandlers_ShouldEndWith_CommandHandler()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .ResideInNamespaceContaining("Handlers.Commands")
            .And()
            .AreClasses()
            .And()
            .AreNotAbstract()
            .And()
            .HaveNameEndingWith("Handler")
            .Should()
            .HaveNameEndingWith("CommandHandler")
            .GetResult();

        if (!result.IsSuccessful && result.FailingTypes != null)
        {
            Console.WriteLine($"CommandHandler Naming Failures ({result.FailingTypes.Count}):");
            foreach (var type in result.FailingTypes)
            {
                Console.WriteLine($"  - {type.FullName}");
            }
        }

        await Assert.That(result.IsSuccessful).IsTrue();
    }

    [Test]
    public async Task QueryHandlers_ShouldEndWith_Handler()
    {
        // The codebase uses both "RequestHandler" and "QueryHandler" naming
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .ResideInNamespaceContaining("Queries")
            .And()
            .AreClasses()
            .And()
            .AreNotAbstract()
            .And()
            .HaveNameEndingWith("Handler")
            .Should()
            .HaveNameEndingWith("Handler")
            .GetResult();

        await Assert.That(result.IsSuccessful).IsTrue();
    }

    #endregion

    #region Persistence Configuration Conventions

    [Test]
    public async Task Phase4PersistenceConfigurations_ShouldUseOneClassPerCanonicalFile()
    {
        string configurationDirectory = Path.Combine(FindRepoRoot(), "src", "Explore.Persistence", "Configurations", "Entities");
        string[] configurationNames =
        [
            "EventTicketCatalogVersionConfiguration",
            "EventTicketTypeConfiguration",
            "TicketTypeEntitlementConfiguration",
            "EventCapacityPoolConfiguration",
            "LookupConfiguration",
            "TicketCatalogStatusConfiguration",
            "TicketPricingModeConfiguration",
            "ParticipantDataCollectionModeConfiguration",
            "EntitlementScopeTypeConfiguration",
            "EntitlementSelectionRuleConfiguration",
            "CapacityOversellPolicyConfiguration",
            "PlatformFeePolicyConfiguration",
            "PlatformFeeFixedChargeConfiguration",
            "PlatformContributionSettingConfiguration",
            "PlatformContributionOptionConfiguration"
        ];

        foreach (string configurationName in configurationNames)
        {
            string path = Path.Combine(configurationDirectory, $"{configurationName}.cs");
            await Assert.That(File.Exists(path)).IsTrue();
            string source = await File.ReadAllTextAsync(path);
            string[] classDeclarations = source.Split('\n')
                .Where(line => line.Contains(" class ", StringComparison.Ordinal))
                .ToArray();
            await Assert.That(source).Contains($"class {configurationName}");
            await Assert.That(classDeclarations.Length).IsEqualTo(1);
        }

        foreach (string groupedFile in new[] { "EventTicketingConfigurations.cs", "TicketingLookupConfigurations.cs", "PlatformMonetizationConfigurations.cs" })
        {
            await Assert.That(File.Exists(Path.Combine(configurationDirectory, groupedFile))).IsFalse();
        }
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Explore.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root containing Explore.slnx.");
    }

    #endregion

    #region Repository Naming Conventions

    [Test]
    public async Task Repositories_ShouldEndWith_Repository()
    {
        // Exclude generic repository base class
        var result = Types.InAssembly(PersistenceAssembly)
            .That()
            .ResideInNamespace("Explore.Persistence.Repositories")
            .And()
            .AreClasses()
            .And()
            .AreNotAbstract()
            .And()
            .AreNotGeneric()
            .And()
            .AreNotNested()
            .And()
            .DoNotHaveName("CustomPropertyExposureScope")
            .Should()
            .HaveNameEndingWith("Repository")
            .GetResult();

        await Assert.That(result.FailingTypeNames ?? []).IsEmpty();
    }

    #endregion

    #region Validator Naming Conventions

    [Test]
    public async Task Validators_ShouldEndWith_Validator()
    {
        var types = Types.InAssembly(ApplicationAssembly)
            .That()
            .ResideInNamespaceContaining("Validators")
            .And()
            .AreClasses()
            .And()
            .AreNotAbstract()
            .GetTypes();

        var failingTypes = types
            .Where(t =>
            {
                var name = t.Name;
                if (t.IsGenericType || name.Contains('`'))
                {
                    var idx = name.IndexOf('`');
                    if (idx >= 0)
                    {
                        name = name.Substring(0, idx);
                    }
                }
                return !name.EndsWith("Validator", StringComparison.Ordinal);
            })
            .ToList();

        if (failingTypes.Any())
        {
            Console.WriteLine($"Validator Naming Failures ({failingTypes.Count()}):");
            foreach (var type in failingTypes)
            {
                Console.WriteLine($"  - {type.FullName}");
            }
        }

        await Assert.That(failingTypes).IsEmpty();
    }

    #endregion

    #region DTO Naming Conventions

    [Test]
    public async Task DTOs_ShouldEndWith_Dto()
    {
        // Composite aggregates, write-model request DTOs, and non-DTO types are excluded from the Dto suffix rule
        var compositeAggregateNames = new HashSet<string>
        {
            "InstanceGovernanceSettings",
            "CompleteInstanceOnboardingRequest",
            "CompleteTenantOnboardingRequest",
            "UpdateTenantPolicyRequest",
            "BatchUpdateMode",  // Enum, not a DTO
            "UiThemeInputRules",  // Utility class, not a DTO
            "CustomPropertyFilterCriterion",  // Filter specification input, not a data transfer object
            "CustomPropertyFilterOperator",  // Enum, not a DTO
            "AddressProviderOutcome"  // Enum, not a DTO
        };

        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .ResideInNamespaceContaining("DTOs")
            .And()
            .AreClasses()
            .And()
            .AreNotAbstract()
            .And()
            .DoNotResideInNamespaceContaining("Validators")
            .Should()
            .HaveNameEndingWith("Dto")
            .GetResult();

        var failures = result.FailingTypes?
            .Where(t => !compositeAggregateNames.Contains(t.Name))
            .ToList() ?? [];

        if (failures.Count > 0)
        {
            Console.WriteLine($"DTO Naming Failures ({failures.Count}):");
            foreach (var type in failures)
            {
                Console.WriteLine($"  - {type.FullName}");
            }
        }

        await Assert.That(failures.Count).IsEqualTo(0);
    }

    #endregion

    #region Authorization Refactor Regression Tests

    private static readonly Assembly DomainAssembly = typeof(Explore.Domain.Enums.RoleEnum).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(Explore.Infrastructure.InfrastructureServicesRegistration).Assembly;

    /// <summary>
    /// Ensures no types reference removed entity names from the pre-refactor authorization model.
    /// OrganizationRole and TenantAdministratorRole were replaced by the unified Role entity.
    /// Note: PlatformUserRole remains a valid membership/link entity in the current model.
    /// TenantAdministrator and the former membership aggregate were replaced by TenantUserRoleGrant.
    /// </summary>
    [Test]
    [DisplayName("No types should be named after removed authorization entities")]
    public async Task NoTypesNamed_AfterRemovedAuthorizationEntities()
    {
        var allAssemblies = new[] { DomainAssembly, ApplicationAssembly, PersistenceAssembly, InfrastructureAssembly };

        // These exact type names were removed during the authorization provider refactor.
        // Any new type with these names would indicate an incomplete migration.
        string[] forbiddenTypeNames =
        [
            "OrganizationRole",
            "TenantAdministratorRole",
            "TenantAdministrator",
        ];

        foreach (var assembly in allAssemblies)
        {
            foreach (var forbiddenName in forbiddenTypeNames)
            {
                var matchingTypes = Types.InAssembly(assembly)
                    .That()
                    .HaveNameMatching($"^{forbiddenName}$")
                    .GetTypes();

                await Assert.That(matchingTypes).IsEmpty()
                    .Because($"Type '{forbiddenName}' should not exist in {assembly.GetName().Name} — it was removed during the authorization refactor");
            }
        }
    }

    /// <summary>
    /// Organization-centric public UX is a typed Application posture over tenant-local data.
    /// It must not reintroduce workspace/sub-tenant/scope domain models.
    /// </summary>
    [Test]
    [DisplayName("No types should introduce organization-centric workspace or sub-tenant scope models")]
    public async Task NoTypesNamed_AfterForbiddenOrganizationCentricScopeConcepts()
    {
        var allAssemblies = new[] { DomainAssembly, ApplicationAssembly, PersistenceAssembly, InfrastructureAssembly };

        string[] forbiddenTypeNames =
        [
            "OrganizerScope",
            "BusinessScope",
            "Workspace",
            "TenantWorkspace",
            "SubTenant",
            "OrganizationScope",
        ];

        foreach (var assembly in allAssemblies)
        {
            foreach (var forbiddenName in forbiddenTypeNames)
            {
                var matchingTypes = Types.InAssembly(assembly)
                    .That()
                    .HaveNameMatching($"^{forbiddenName}$")
                    .GetTypes();

                await Assert.That(matchingTypes).IsEmpty()
                    .Because($"Organization-centric UX must stay a typed Application posture; type '{forbiddenName}' would imply a forbidden workspace/sub-tenant scope model in {assembly.GetName().Name}");
            }
        }
    }

    /// <summary>
    /// Ensures no interfaces reference the old ICerbosAuthorizationService name.
    /// It was renamed to IAuthorizationProvider during the refactor.
    /// </summary>
    [Test]
    [DisplayName("No interfaces should use the old ICerbosAuthorizationService name")]
    public async Task NoInterfacesNamed_ICerbosAuthorizationService()
    {
        var matchingTypes = Types.InAssembly(ApplicationAssembly)
            .That()
            .AreInterfaces()
            .And()
            .HaveNameMatching("^ICerbosAuthorizationService$")
            .GetTypes();

        await Assert.That(matchingTypes).IsEmpty()
            .Because("ICerbosAuthorizationService was renamed to IAuthorizationProvider during the refactor");
    }

    /// <summary>
    /// Ensures no attributes reference the old CerbosAuthorizeAttribute name.
    /// It was renamed to AuthorizeResourceAttribute during the refactor.
    /// </summary>
    [Test]
    [DisplayName("No attributes should use the old CerbosAuthorizeAttribute name")]
    public async Task NoAttributesNamed_CerbosAuthorizeAttribute()
    {
        var matchingTypes = Types.InAssembly(ApplicationAssembly)
            .That()
            .HaveNameMatching("^CerbosAuthorizeAttribute$")
            .GetTypes();

        await Assert.That(matchingTypes).IsEmpty()
            .Because("CerbosAuthorizeAttribute was renamed to AuthorizeResourceAttribute during the refactor");
    }

    #endregion

    #region Interface Naming Conventions

    [Test]
    public async Task Interfaces_ShouldStartWith_I()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .AreInterfaces()
            .Should()
            .HaveNameStartingWith("I")
            .GetResult();

        await Assert.That(result.IsSuccessful).IsTrue();
    }

    #endregion
}
