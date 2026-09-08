using System.Text.RegularExpressions;

namespace Event.Architecture.Tests;

public sealed class TicketingLifecycleAuthorityArchitectureTests
{
    private const int LifecycleSourceFileLineCeiling = 1_295;
    private const int CapabilityCoordinatorLineCeiling = 100;
    private const int InventoryRepositoryLineCeiling = 681;
    private const int RegistrationOrderLinkPolicyLineCeiling = 214;

    private static readonly string InventoryContractPath = ContextSystemHelpers.RepoPath(
        "Explore.Application",
        "Contracts",
        "Persistence",
        "IRegistrationInventoryRepository.cs");

    private static readonly string InventoryRepositoryPath = ContextSystemHelpers.RepoPath(
        "Explore.Persistence",
        "Repositories",
        "RegistrationInventoryRepository.cs");

    private static readonly string LifecycleServiceRoot = ContextSystemHelpers.RepoPath(
        "Explore.Application",
        "Services",
        "Registration");

    private static readonly string AuthenticatedLinkPolicyPath = ContextSystemHelpers.RepoPath(
        "Explore.API",
        "Hateoas",
        "Policies",
        "RegistrationOrderLinkPolicy.cs");

    private static readonly string GuestLinkFactoryPath = ContextSystemHelpers.RepoPath(
        "Explore.API",
        "Hateoas",
        "GuestRegistrationOrderHalResourceFactory.cs");

    [Test]
    public async Task PersistenceMustStoreAggregateTransitionsWithoutOwningLifecycleRules()
    {
        string contract = await File.ReadAllTextAsync(InventoryContractPath);
        string repository = await File.ReadAllTextAsync(InventoryRepositoryPath);
        var failures = new List<string>();

        if (contract.Contains("TryTransitionOrderAsync", StringComparison.Ordinal))
        {
            failures.Add("IRegistrationInventoryRepository exposes a status-transition command instead of an entity-first storage primitive.");
        }

        if (repository.Contains("TryTransitionOrderAsync", StringComparison.Ordinal))
        {
            failures.Add("RegistrationInventoryRepository implements lifecycle authority that belongs to RegistrationOrder.");
        }

        if (repository.Contains(
                "SetProperty(order => order.RegistrationOrderStatusId",
                StringComparison.Ordinal))
        {
            failures.Add("RegistrationInventoryRepository directly updates RegistrationOrderStatusId without aggregate mutation.");
        }

        if (repository.Contains("RegistrationOrderRules.CanTransition", StringComparison.Ordinal))
        {
            failures.Add("Persistence evaluates Domain lifecycle policy instead of persisting an aggregate decision.");
        }

        await Assert.That(failures).IsEmpty();
    }

    [Test]
    public async Task ApplicationAndHalMustConsumeAggregateOwnedLifecycleDecisions()
    {
        string[] lifecycleFiles = Directory.GetFiles(
            LifecycleServiceRoot,
            "RegistrationOrderLifecycleService*.cs",
            SearchOption.TopDirectoryOnly);
        string lifecycleSource = string.Join(
            Environment.NewLine,
            await Task.WhenAll(lifecycleFiles.Select(path => File.ReadAllTextAsync(path))));
        string authenticatedLinks = await File.ReadAllTextAsync(AuthenticatedLinkPolicyPath);
        string guestLinks = await File.ReadAllTextAsync(GuestLinkFactoryPath);
        var failures = new List<string>();

        if (lifecycleSource.Contains("TryTransitionOrderAsync", StringComparison.Ordinal))
        {
            failures.Add("RegistrationOrderLifecycleService bypasses aggregate mutation through a persistence transition command.");
        }

        if (lifecycleSource.Contains("RegistrationOrderRules.CanTransition", StringComparison.Ordinal))
        {
            failures.Add("RegistrationOrderLifecycleService reconstructs transition eligibility outside the aggregate.");
        }

        foreach ((string path, string source) in new[]
                 {
                     (AuthenticatedLinkPolicyPath, authenticatedLinks),
                     (GuestLinkFactoryPath, guestLinks)
                 })
        {
            if (Regex.IsMatch(
                    source,
                    @"\bStatusCode\s+(?:is|==|!=)",
                    RegexOptions.CultureInvariant))
            {
                failures.Add($"{Path.GetFileName(path)} reconstructs lifecycle affordances from transport status strings.");
            }

            if (!source.Contains("RegistrationOrderRules", StringComparison.Ordinal))
            {
                failures.Add($"{Path.GetFileName(path)} does not consume the shared Domain lifecycle decision surface.");
            }
        }

        await Assert.That(failures).IsEmpty();
    }

    [Test]
    public async Task LegacyLifecycleSeamsMustShrinkAndRemainBounded()
    {
        string[] lifecycleFiles = Directory.GetFiles(
            LifecycleServiceRoot,
            "RegistrationOrderLifecycleService*.cs",
            SearchOption.TopDirectoryOnly);
        int inventoryLines = File.ReadAllLines(InventoryRepositoryPath).Length;
        int linkPolicyLines = File.ReadAllLines(AuthenticatedLinkPolicyPath).Length;
        var failures = new List<string>();

        foreach (string path in lifecycleFiles)
        {
            int lineCount = File.ReadAllLines(path).Length;
            if (lineCount > LifecycleSourceFileLineCeiling)
            {
                failures.Add(
                    $"{Path.GetFileName(path)} has {lineCount} lines; capability-specific coordinators must keep each lifecycle source at or below {LifecycleSourceFileLineCeiling}.");
            }
        }

        foreach (string fileName in new[]
                 {
                     "RegistrationOrderReadService.cs",
                     "RegistrationOrderTransitionCoordinator.cs"
                 })
        {
            int lineCount = File.ReadAllLines(Path.Combine(LifecycleServiceRoot, fileName)).Length;
            if (lineCount > CapabilityCoordinatorLineCeiling)
            {
                failures.Add(
                    $"{fileName} has {lineCount} lines; a capability-specific coordinator must remain at or below {CapabilityCoordinatorLineCeiling}.");
            }
        }

        if (inventoryLines > InventoryRepositoryLineCeiling)
        {
            failures.Add(
                $"RegistrationInventoryRepository has {inventoryLines} lines; lifecycle-specific persistence must reduce it to at most {InventoryRepositoryLineCeiling}.");
        }

        if (linkPolicyLines > RegistrationOrderLinkPolicyLineCeiling)
        {
            failures.Add(
                $"RegistrationOrderLinkPolicy has {linkPolicyLines} lines; shared decisions must reduce it to at most {RegistrationOrderLinkPolicyLineCeiling}.");
        }

        await Assert.That(failures).IsEmpty();
    }
}
