using System.Text.RegularExpressions;

namespace Event.Architecture.Tests;

public sealed class SecretAuthorityLeastPrivilegeArchitectureTests
{
    private static readonly string[] AllowedBlazorInfisicalPaths =
    [
        "/keycloak",
        "/blazor",
        "/atproto"
    ];

    private static readonly string[] ForbiddenBlazorInfisicalPaths =
    [
        "/api",
        "/database",
        "/database/erasure",
        "/database/identity",
        "/cerbos",
        "/storage",
        "/smtp",
        "/stripe",
        "/mcp",
        "/ai",
        "/integrations/listmonk",
        "/setup",
        "/webhook",
        "/promotions",
        "/admissions",
        "/ticketing/recovery"
    ];

    private static readonly string[] ForbiddenApiInfisicalPaths =
    [
        "/blazor"
    ];

    private static readonly string[] AllowedMigrationServiceInfisicalPaths =
    [
        "/database",
        "/database/erasure",
        "/database/identity"
    ];

    private static readonly string[] BackendOnlySecretKeys =
    [
        "AUTHENTICATION_LOCAL_JWT_KEY",
        "SETUP_SECRET",
        "INSTANCE_BOOTSTRAP_LOCAL_PASSWORD",
        "CONTROL_PLANE_REGISTRATION_CREDENTIALS",
        "VAPID_PRIVATE_KEY",
        "LUCKYPENNY_LICENSE_KEY",
        "DATABASE_RUNTIME_PASSWORD",
        "DATABASE_MIGRATOR_PASSWORD",
        "ERASURE_DATABASE_RUNTIME_PASSWORD",
        "IDENTITY_DATABASE_RUNTIME_PASSWORD",
        "STRIPE_PLATFORM_SECRET_KEY",
        "STRIPE_WEBHOOK_SECRET",
        "MAIL_SMTP_PASSWORD"
    ];

    private static readonly string[] FrontendOnlySecretKeys =
    [
        "GOOGLE_CLIENT_SECRET",
        "BFF_ADMIN_HOSTS"
    ];

    [Test]
    public async Task BlazorBff_InfisicalSubscription_MustStrictlyFollowLeastPrivilege()
    {
        var repositoryRoot = ResolveRepositoryRoot();
        var blazorConfigExtensionFile = Path.Combine(
            repositoryRoot,
            "src",
            "Explore.Blazor",
            "Extensions",
            "ConfigurationExtension.cs");

        var source = await File.ReadAllTextAsync(blazorConfigExtensionFile);

        var match = Regex.Match(
            source,
            @"source\.Paths\.AddRange\(\s*\[(?<paths>[^\]]+)\]\s*\);",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);

        await Assert.That(match.Success)
            .IsTrue()
            .Because("Explore.Blazor must declare explicit Infisical source.Paths via AddRange([...]).");

        var declaredPaths = Regex.Matches(match.Groups["paths"].Value, "\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value.Trim())
            .ToList();

        var unauthorizedPaths = declaredPaths
            .Where(path => !AllowedBlazorInfisicalPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
            .ToList();

        await Assert.That(unauthorizedPaths)
            .IsEmpty()
            .Because($"Explore.Blazor (BFF) must only subscribe to frontend secret paths (Allowed: {string.Join(", ", AllowedBlazorInfisicalPaths)}). Unauthorized: {string.Join(", ", unauthorizedPaths)}");

        var forbiddenSubscribed = declaredPaths
            .Intersect(ForbiddenBlazorInfisicalPaths, StringComparer.OrdinalIgnoreCase)
            .ToList();

        await Assert.That(forbiddenSubscribed)
            .IsEmpty()
            .Because($"Explore.Blazor (BFF) must NEVER subscribe to backend secret paths. Found violations: {string.Join(", ", forbiddenSubscribed)}");
    }

    [Test]
    public async Task BlazorBffSource_MustNotDirectlyReferenceBackendSecrets()
    {
        var repositoryRoot = ResolveRepositoryRoot();
        var blazorProjectDir = Path.Combine(repositoryRoot, "src", "Explore.Blazor");

        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(blazorProjectDir, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                              && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            var content = await File.ReadAllTextAsync(file);

            foreach (var backendSecret in BackendOnlySecretKeys)
            {
                if (content.Contains($"\"{backendSecret}\"", StringComparison.Ordinal))
                {
                    violations.Add($"{Path.GetRelativePath(repositoryRoot, file)} references backend secret key '{backendSecret}'");
                }
            }
        }

        await Assert.That(violations)
            .IsEmpty()
            .Because($"Explore.Blazor source code must never directly read or map backend-only credentials. Violations: {string.Join("; ", violations)}");
    }

    [Test]
    public async Task ApiHost_InfisicalSubscription_MustStrictlyFollowLeastPrivilege()
    {
        var repositoryRoot = ResolveRepositoryRoot();
        var apiConfigFile = Path.Combine(
            repositoryRoot,
            "src",
            "Explore.API",
            "Extensions",
            "ConfigurationExtensions.cs");

        var source = await File.ReadAllTextAsync(apiConfigFile);

        var match = Regex.Match(
            source,
            @"SecretAuthorityConfiguration\.Build\(\s*bootstrapConfig,\s*environmentName,\s*(?<paths>[^;]+)\);",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);

        await Assert.That(match.Success)
            .IsTrue()
            .Because("Explore.API must declare explicit Infisical paths via SecretAuthorityConfiguration.Build.");

        var declaredPaths = Regex.Matches(match.Groups["paths"].Value, "\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value.Trim())
            .ToList();

        var forbiddenFound = declaredPaths
            .Intersect(ForbiddenApiInfisicalPaths, StringComparer.OrdinalIgnoreCase)
            .ToList();

        await Assert.That(forbiddenFound)
            .IsEmpty()
            .Because($"Explore.API must NEVER subscribe to frontend-only secret paths ({string.Join(", ", ForbiddenApiInfisicalPaths)}). Found violations: {string.Join(", ", forbiddenFound)}");
    }

    [Test]
    public async Task MigrationService_InfisicalSubscription_MustStrictlyFollowLeastPrivilege()
    {
        var repositoryRoot = ResolveRepositoryRoot();
        var migratorConfigFile = Path.Combine(
            repositoryRoot,
            "src",
            "Event.MigrationService",
            "Extensions",
            "ConfigurationExtensions.cs");

        var source = await File.ReadAllTextAsync(migratorConfigFile);

        var match = Regex.Match(
            source,
            @"SecretAuthorityConfiguration\.Build\(\s*bootstrapConfiguration,\s*environmentName,\s*(?<paths>[^;]+)\);",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);

        await Assert.That(match.Success)
            .IsTrue()
            .Because("Event.MigrationService must declare explicit Infisical paths via SecretAuthorityConfiguration.Build.");

        var declaredPaths = Regex.Matches(match.Groups["paths"].Value, "\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value.Trim())
            .ToList();

        var unauthorizedPaths = declaredPaths
            .Where(path => !AllowedMigrationServiceInfisicalPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
            .ToList();

        await Assert.That(unauthorizedPaths)
            .IsEmpty()
            .Because($"Event.MigrationService must only subscribe to database migration paths ({string.Join(", ", AllowedMigrationServiceInfisicalPaths)}). Unauthorized: {string.Join(", ", unauthorizedPaths)}");
    }

    [Test]
    public async Task ApiSource_MustNotDirectlyReferenceFrontendOnlySecrets()
    {
        var repositoryRoot = ResolveRepositoryRoot();
        var apiProjectDir = Path.Combine(repositoryRoot, "src", "Explore.API");

        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(apiProjectDir, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                              && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            var content = await File.ReadAllTextAsync(file);

            foreach (var frontendSecret in FrontendOnlySecretKeys)
            {
                if (content.Contains($"\"{frontendSecret}\"", StringComparison.Ordinal))
                {
                    violations.Add($"{Path.GetRelativePath(repositoryRoot, file)} references frontend secret key '{frontendSecret}'");
                }
            }
        }

        await Assert.That(violations)
            .IsEmpty()
            .Because($"Explore.API source code must never directly read or map frontend-only credentials. Violations: {string.Join("; ", violations)}");
    }

    [Test]
    public async Task DockerCompose_FrontendServices_MustNotBeInjectedWithDatabaseSecrets()
    {
        var repositoryRoot = ResolveRepositoryRoot();
        var dockerComposeFile = Path.Combine(repositoryRoot, "docker-compose.yml");

        var content = await File.ReadAllTextAsync(dockerComposeFile);

        var match = Regex.Match(
            content,
            @"islamu-event-ui:.*?(?<env>environment:\s*<<:\s*\[[^\]]+\])",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);

        await Assert.That(match.Success)
            .IsTrue()
            .Because("docker-compose.yml must contain the islamu-event-ui service environment definition.");

        var envBlock = match.Groups["env"].Value;

        await Assert.That(envBlock)
            .DoesNotContain("*database-env")
            .Because("islamu-event-ui (BFF) does not connect to the database and must not be injected with *database-env.");

        await Assert.That(envBlock)
            .DoesNotContain("*database-runtime-env")
            .Because("islamu-event-ui (BFF) must not be injected with *database-runtime-env.");

        await Assert.That(envBlock)
            .DoesNotContain("*database-migrator-env")
            .Because("islamu-event-ui (BFF) must not be injected with *database-migrator-env.");
    }

    [Test]
    public async Task AppHost_BlazorResource_MustNotBeConfiguredWithDatabaseCredentials()
    {
        var repositoryRoot = ResolveRepositoryRoot();
        var appHostFile = Path.Combine(repositoryRoot, "src", "Explore.AppHost", "AppHost.cs");

        var content = await File.ReadAllTextAsync(appHostFile);

        var blazorSectionMatch = Regex.Match(
            content,
            @"builder\.AddProject<Projects\.Explore_Blazor>\(.*?\);(?<block>.*?)(?=else|\z)",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);

        await Assert.That(blazorSectionMatch.Success)
            .IsTrue()
            .Because("AppHost.cs must contain the Explore_Blazor resource registration block.");

        var blazorBlock = blazorSectionMatch.Groups["block"].Value;

        await Assert.That(blazorBlock)
            .DoesNotContain("WithLocalPrimaryDatabase(exploreBlazor")
            .Because("AppHost.cs must not configure exploreBlazor with WithLocalPrimaryDatabase; Blazor BFF does not connect to the primary database.");

        await Assert.That(blazorBlock)
            .DoesNotContain("WithExternalPrimaryDatabase(builder, exploreBlazor")
            .Because("AppHost.cs must not configure exploreBlazor with WithExternalPrimaryDatabase; Blazor BFF does not connect to the primary database.");
    }

    [Test]
    public async Task InfisicalDocumentation_MustEnforceHostLeastPrivilegeMatrix()
    {
        var repositoryRoot = ResolveRepositoryRoot();
        var docFile = Path.Combine(
            repositoryRoot,
            "docs",
            "public",
            "documentation",
            "readme",
            "configuration-and-operations",
            "infisical.md");

        var content = await File.ReadAllTextAsync(docFile);

        // Check Explore.Blazor
        var blazorMatch = Regex.Match(
            content,
            @"\|\s*`Explore\.Blazor`\s*\(BFF\)\s*\|\s*(?<folders>[^|]+)\s*\|",
            RegexOptions.CultureInvariant);

        await Assert.That(blazorMatch.Success)
            .IsTrue()
            .Because("infisical.md must contain the Section 5 mapping table row for `Explore.Blazor` (BFF).");

        var blazorFolders = Regex.Matches(blazorMatch.Groups["folders"].Value, @"`([^`]+)`")
            .Select(m => m.Groups[1].Value.Trim())
            .ToList();

        var unauthorizedDocFolders = blazorFolders
            .Where(f => !AllowedBlazorInfisicalPaths.Contains(f, StringComparer.OrdinalIgnoreCase))
            .ToList();

        await Assert.That(unauthorizedDocFolders)
            .IsEmpty()
            .Because($"infisical.md Section 5 documentation must restrict Explore.Blazor to {string.Join(", ", AllowedBlazorInfisicalPaths)}. Found: {string.Join(", ", unauthorizedDocFolders)}");

        // Check Explore.API
        var apiMatch = Regex.Match(
            content,
            @"\|\s*`Explore\.API`\s*\|\s*(?<folders>[^|]+)\s*\|",
            RegexOptions.CultureInvariant);

        await Assert.That(apiMatch.Success)
            .IsTrue()
            .Because("infisical.md must contain the Section 5 mapping table row for `Explore.API`.");

        var apiFolders = Regex.Matches(apiMatch.Groups["folders"].Value, @"`([^`]+)`")
            .Select(m => m.Groups[1].Value.Trim())
            .ToList();

        var forbiddenInApiDoc = apiFolders
            .Intersect(ForbiddenApiInfisicalPaths, StringComparer.OrdinalIgnoreCase)
            .ToList();

        await Assert.That(forbiddenInApiDoc)
            .IsEmpty()
            .Because($"infisical.md Section 5 documentation must not list frontend paths for Explore.API. Found: {string.Join(", ", forbiddenInApiDoc)}");
    }

    private static string ResolveRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Explore.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root from the architecture test output directory.");
    }
}
