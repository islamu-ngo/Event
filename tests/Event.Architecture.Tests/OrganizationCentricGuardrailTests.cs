// ABOUTME: Architecture guardrails for organization-centric public UX boundaries.
// ABOUTME: Prevents scope-model drift, resolver coupling, and Domain business-default leakage.

namespace Event.Architecture.Tests;

using System.Reflection;
using Explore.Domain.Interfaces;

public class OrganizationCentricGuardrailTests
{
    private static readonly Assembly DomainAssembly = typeof(Explore.Domain.Event).Assembly;

    private static readonly string[] ForbiddenScopeConceptNames =
    [
        "OrganizerScope",
        "BusinessScope",
        "Workspace",
        "TenantWorkspace",
        "SubTenant",
        "OrganizationScope"
    ];

    [Test]
    [DisplayName("Domain must not introduce organization-centric scope entity types")]
    public async Task DomainMustNotIntroduce_OrganizationCentricScopeTypes()
    {
        var forbiddenTypes = DomainAssembly.GetTypes()
            .Where(type => ForbiddenScopeConceptNames.Contains(type.Name, StringComparer.Ordinal))
            .Select(type => type.FullName ?? type.Name)
            .ToList();

        await Assert.That(forbiddenTypes).IsEmpty()
            .Because("organization-centric UX must not add Domain entity types for workspace, sub-tenant, or organization-scope concepts");
    }

    [Test]
    [DisplayName("Migrations must not introduce organization-centric scope tables")]
    public async Task MigrationsMustNotIntroduce_OrganizationCentricScopeTables()
    {
        var migrationsDirectory = Path.Combine(RepositoryRoot.FullName, "Explore.Persistence", "Migrations");
        var forbiddenTableNames = ForbiddenScopeConceptNames
            .SelectMany(name => new[] { name, $"{name}s" })
            .ToArray();

        var violations = Directory.Exists(migrationsDirectory)
            ? Directory.GetFiles(migrationsDirectory, "*.cs", SearchOption.TopDirectoryOnly)
                .SelectMany(file => FindForbiddenText(file, forbiddenTableNames))
                .ToList()
            : [];

        await Assert.That(violations).IsEmpty()
            .Because("organization-centric UX must use existing Tenant, Organization, Group, and Actor tables instead of adding workspace/scope tables");
    }

    [Test]
    [DisplayName("Event ownership must remain actor-backed without workspace or scope IDs")]
    public async Task EventOwnershipMustRemain_ActorBackedWithoutScopeIds()
    {
        var forbiddenEventPropertyNames = new[]
        {
            "WorkspaceId",
            "OrganizerScopeId",
            "BusinessScopeId",
            "OrganizationScopeId",
            "TenantWorkspaceId",
            "SubTenantId",
            "ScopeId"
        };

        var eventPropertyNames = typeof(Explore.Domain.Event)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        var violations = forbiddenEventPropertyNames
            .Where(eventPropertyNames.Contains)
            .ToList();

        await Assert.That(violations).IsEmpty()
            .Because("public event ownership filters must remain ActorId/OrganizationId/GroupId projections over existing actor-backed data");
    }

    [Test]
    [DisplayName("Tenant-scoped entities must not gain SubTenantId")]
    public async Task TenantScopedEntitiesMustNotGain_SubTenantId()
    {
        var violations = DomainAssembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(type => typeof(ITenantEntity).IsAssignableFrom(type))
            .Where(type => type.GetProperty("SubTenantId", BindingFlags.Public | BindingFlags.Instance) is not null)
            .Select(type => type.FullName ?? type.Name)
            .ToList();

        await Assert.That(violations).IsEmpty()
            .Because("Tenant remains the isolation boundary; tenant-scoped entities must not introduce a sub-tenant layer");
    }

    [Test]
    [DisplayName("Tenant resolvers must not accept organization identifiers as resolver input")]
    public async Task TenantResolversMustNotUse_OrganizationIdentifiersAsInputs()
    {
        var resolverFiles = new[]
        {
            "Explore.Application/Contracts/Services/ITenantResolver.cs",
            "Explore.Application/Contracts/Services/ITenantResolverService.cs",
            "Explore.Infrastructure/Services/TenantResolverService.cs",
            "Explore.Blazor/Services/BlazorHeaderTenantResolver.cs",
            "Explore.Blazor/Services/Resolvers/SubdomainTenantResolver.cs",
            "Explore.Blazor/Services/Resolvers/CustomDomainTenantResolver.cs",
            "Explore.Blazor/Middleware/PathTenantResolverMiddleware.cs"
        };

        var forbiddenTokens = new[] { "OrganizationId", "PrimaryOrganizationId" };
        var violations = resolverFiles
            .Select(path => new FileInfo(Path.Combine(RepositoryRoot.FullName, path)))
            .Where(file => file.Exists)
            .SelectMany(file => FindForbiddenText(file.FullName, forbiddenTokens))
            .ToList();

        await Assert.That(violations).IsEmpty()
            .Because("primary organization is tenant-local referenced content, not tenant resolver input");
    }

    [Test]
    [DisplayName("Public-experience posture vocabulary must stay out of Domain entities")]
    public async Task PublicExperiencePostureVocabularyMustStayOutOf_DomainEntities()
    {
        var allowedNamespaces = new[]
        {
            "Explore.Domain.Constants",
            "Explore.Domain.Settings"
        };
        var forbiddenNameFragments = new[]
        {
            "PublicExperience",
            "DiscoveryCentric",
            "OrganizationCentric",
            "PublicEventSection",
            "EventCatalogLabel",
            "PrimaryOrganization"
        };

        var violations = DomainAssembly.GetTypes()
            .Where(type => type.Namespace is not null && !allowedNamespaces.Any(allowed => type.Namespace.StartsWith(allowed, StringComparison.Ordinal)))
            .SelectMany(type => FindForbiddenMemberNames(type, forbiddenNameFragments))
            .ToList();

        await Assert.That(violations).IsEmpty()
            .Because("public posture, presets, event visibility posture, and import-convenience defaults belong in Application/read models or settings metadata, not Domain entities");
    }

    private static DirectoryInfo RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Explore.slnx")))
            {
                directory = directory.Parent;
            }

            return directory ?? throw new DirectoryNotFoundException("Could not find repository root containing Explore.slnx.");
        }
    }

    private static IEnumerable<string> FindForbiddenText(string filePath, IReadOnlyCollection<string> forbiddenTokens)
    {
        var text = File.ReadAllText(filePath);
        return forbiddenTokens
            .Where(token => text.Contains(token, StringComparison.Ordinal))
            .Select(token => $"{RelativePath(filePath)} contains {token}");
    }

    private static IEnumerable<string> FindForbiddenMemberNames(Type type, IReadOnlyCollection<string> forbiddenNameFragments)
    {
        var names = new List<string> { type.Name };
        names.AddRange(type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Select(field => field.Name));
        names.AddRange(type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Select(property => property.Name));

        foreach (var name in names)
        {
            foreach (var fragment in forbiddenNameFragments)
            {
                if (name.Contains(fragment, StringComparison.Ordinal))
                {
                    yield return $"{type.FullName}.{name} contains {fragment}";
                }
            }
        }
    }

    private static string RelativePath(string path) => Path.GetRelativePath(RepositoryRoot.FullName, path);
}
