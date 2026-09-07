namespace Event.Architecture.Tests;

using System.Text.RegularExpressions;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

public class PersistenceTenantFilterArchitectureTests
{
    private static readonly Regex RawBypassReasonRegex = new(
        @"\.(IgnoreTenantFilter|IgnoreAllFilters|EnableTenantFilterBypass)\s*\(\s*(?:reason\s*:\s*)?[@$]*""{1,3}",
        RegexOptions.Compiled);

    [Test]
    [DisplayName("Tenant query filters must fail closed when tenant context is missing")]
    public async Task TenantQueryFilters_ShouldFailClosed_WhenTenantContextIsMissing()
    {
        var queryFiltersPath = ContextSystemHelpers.RepoPath(
            "Explore.Persistence",
            "ExploreDbContext.QueryFilters.cs");
        var source = await File.ReadAllTextAsync(queryFiltersPath);

        await Assert.That(source.Contains("TenantContext == null ||", StringComparison.Ordinal)).IsFalse()
            .Because("missing TenantContext must not broaden tenant-scoped queries; use an explicit bypass reason for system/admin flows.");
    }

    [Test]
    [DisplayName("Runtime persistence code must not disable all query filters directly")]
    public async Task RuntimePersistenceCode_ShouldNotDisableAllQueryFiltersDirectly()
    {
        var persistenceRoot = ContextSystemHelpers.RepoPath("Explore.Persistence");
        var allowedRelativePaths = new HashSet<string>(StringComparer.Ordinal)
        {
            Path.Combine("src", "Explore.Persistence", "QueryFilters", "QueryFilterExtensions.cs"),
        };

        var violations = new List<string>();
        foreach (var sourceFile in Directory.GetFiles(persistenceRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(ContextSystemHelpers.RepoRoot, sourceFile);
            if (IsGeneratedOrNonRuntime(relativePath) || allowedRelativePaths.Contains(relativePath))
            {
                continue;
            }

            var source = await File.ReadAllTextAsync(sourceFile);
            if (source.Contains(".IgnoreQueryFilters()", StringComparison.Ordinal))
            {
                violations.Add(relativePath);
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because("runtime code must use named filters or QueryFilterExtensions with an explicit bypass reason; full EF filter bypasses are reserved for seeding/tests.");
    }

    [Test]
    [DisplayName("Runtime tenant-filter bypasses must use approved reason constants")]
    public async Task RuntimeTenantFilterBypasses_ShouldUse_ApprovedReasonConstants()
    {
        var runtimeRoots = new[]
        {
            ContextSystemHelpers.RepoPath("Explore.API"),
            ContextSystemHelpers.RepoPath("Explore.Persistence"),
        };

        var violations = new List<string>();
        foreach (var runtimeRoot in runtimeRoots)
        {
            foreach (var sourceFile in Directory.GetFiles(runtimeRoot, "*.cs", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(ContextSystemHelpers.RepoRoot, sourceFile);
                if (IsGeneratedOrNonRuntime(relativePath) || IsTenantBypassInfrastructure(relativePath))
                {
                    continue;
                }

                var source = await File.ReadAllTextAsync(sourceFile);
                if (RawBypassReasonRegex.IsMatch(source))
                {
                    violations.Add(relativePath);
                }
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because("runtime tenant-filter bypasses must reference TenantFilterBypassReasons constants so cross-tenant access remains named, reviewable, and auditable.");
    }

    [Test]
    [DisplayName("API controllers must not call tenant-filter bypass helpers directly")]
    public async Task ApiControllers_ShouldNotCall_TenantFilterBypassHelpersDirectly()
    {
        var controllersRoot = ContextSystemHelpers.RepoPath(
            "Explore.API",
            "Controllers");
        var forbiddenTokens = new[]
        {
            ".IgnoreTenantFilter(",
            ".IgnoreAllFilters(",
            ".IgnoreQueryFilters(",
            ".EnableTenantFilterBypass(",
            "TenantFilterBypassReasons.",
        };

        var violations = new List<string>();
        foreach (var sourceFile in Directory.GetFiles(controllersRoot, "*.cs", SearchOption.AllDirectories))
        {
            var source = await File.ReadAllTextAsync(sourceFile);
            if (forbiddenTokens.Any(token => source.Contains(token, StringComparison.Ordinal)))
            {
                violations.Add(Path.GetRelativePath(ContextSystemHelpers.RepoRoot, sourceFile));
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because("controllers must express cross-tenant intent through commands, queries, and authorization metadata; raw EF tenant-filter bypasses belong in reviewed Persistence/system services only.");
    }

    private static bool IsGeneratedOrNonRuntime(string relativePath)
    {
        return relativePath.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relativePath.Contains($"{Path.DirectorySeparatorChar}Seed{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relativePath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relativePath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static bool IsTenantBypassInfrastructure(string relativePath)
    {
        return string.Equals(
                relativePath,
                Path.Combine("src", "Explore.Persistence", "ExploreDbContext.cs"),
                StringComparison.Ordinal)
            || string.Equals(
                relativePath,
                Path.Combine("src", "Explore.Persistence", "QueryFilters", "QueryFilterExtensions.cs"),
                StringComparison.Ordinal)
            || string.Equals(
                relativePath,
                Path.Combine("src", "Explore.Persistence", "QueryFilters", "TenantFilterBypassReasons.cs"),
                StringComparison.Ordinal);
    }

    [Test]
    [DisplayName("PostgreSQL RLS model must discover all tenant-scoped tables with valid column or parent join")]
    public async Task PostgresTenantRowLevelSecurityModel_MustDiscoverAllTenantTables()
    {
        var options = new DbContextOptionsBuilder<ExploreDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        await using var context = new ExploreDbContext(options);
        var model = context.Model;

        var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(model);

        await Assert.That(tenantTables.Count).IsGreaterThan(200);

        foreach (var table in tenantTables)
        {
            if (table.ParentJoin is null)
            {
                await Assert.That(table.TenantIdColumn).IsNotNull();
            }
            else
            {
                await Assert.That(table.ParentJoin.ParentTableName).IsNotNull();
                await Assert.That(table.ParentJoin.ForeignKeyColumn).IsNotNull();
                await Assert.That(table.ParentJoin.ParentKeyColumn).IsNotNull();
                await Assert.That(table.ParentJoin.ParentTenantIdColumn).IsNotNull();
            }

            var sql = PostgresTenantRowLevelSecurityModel.BuildEnableRlsSql(table, "islamu_event");
            await Assert.That(sql).Contains("ENABLE ROW LEVEL SECURITY");
            await Assert.That(sql).Contains("FORCE ROW LEVEL SECURITY");
            await Assert.That(sql).Contains("CREATE POLICY");
        }
    }
}
