using System.Text.Json;

namespace Event.Architecture.Tests;

public sealed class TicketingDeploymentCapabilityMatrixTests
{
    private static readonly string RepositoryRoot =
        ContextSystemHelpers.RepoPath();
    private static readonly string MatrixPath = Path.Combine(
        RepositoryRoot,
        "src",
        "Explore.Infrastructure",
        "Deployment",
        "ticketing-capabilities.json");

    [Test]
    public async Task MatrixUsesClosedStatusesAndKeepsLaunchGatedCapabilitiesNonProduction()
    {
        using JsonDocument document = JsonDocument.Parse(
            await File.ReadAllTextAsync(MatrixPath));
        JsonElement root = document.RootElement;
        await Assert.That(root.GetProperty("schemaVersion").GetInt32()).IsEqualTo(1);
        await Assert.That(root.GetProperty("revision").GetString()).IsNotNull();
        await Assert.That(root.GetProperty("referenceTopology").GetString()).IsEqualTo(
            "split-postgresql-quartz-cluster");

        JsonElement.ArrayEnumerator capabilities =
            root.GetProperty("capabilities").EnumerateArray();
        string[] allowed =
        [
            "production-approved",
            "test-only",
            "disabled",
        ];
        var codes = new HashSet<string>(StringComparer.Ordinal);
        int count = 0;
        foreach (JsonElement capability in capabilities)
        {
            count++;
            string code = capability.GetProperty("code").GetString()!;
            string status = capability.GetProperty("status").GetString()!;
            await Assert.That(codes.Add(code)).IsTrue();
            await Assert.That(allowed).Contains(status);
            await Assert.That(status).IsNotEqualTo("production-approved");
            await Assert.That(
                    capability.GetProperty("requiredExternalGates")
                        .GetArrayLength())
                .IsGreaterThan(0);
        }

        await Assert.That(count).IsEqualTo(7);
        await Assert.That(codes).IsEquivalentTo(
        [
            "purchase-governance",
            "participant-readiness",
            "credential-transfer",
            "fair-return-waitlist",
            "event-add-ons",
            "ticketing-recovery",
            "protected-delayed-payout",
        ]);
    }

    [Test]
    public async Task ProtectedDelayedPayoutIsDisabledAndHasNoExecutableSurface()
    {
        using JsonDocument document = JsonDocument.Parse(
            await File.ReadAllTextAsync(MatrixPath));
        JsonElement payout = document.RootElement
            .GetProperty("capabilities")
            .EnumerateArray()
            .Single(capability => string.Equals(
                capability.GetProperty("code").GetString(),
                "protected-delayed-payout",
                StringComparison.Ordinal));
        await Assert.That(payout.GetProperty("status").GetString())
            .IsEqualTo("disabled");
        await Assert.That(payout.GetProperty("reasonCode").GetString())
            .IsEqualTo("separate_workstream_required");

        string[] excluded =
        [
            MatrixPath,
            Path.Combine(
                RepositoryRoot,
                "src",
                "Explore.Infrastructure",
                "Deployment",
                "TicketingDeploymentCapabilityCatalog.cs"),
        ];
        string[] forbiddenTokens =
        [
            "protected-delayed-payout",
            "protected_delayed_payout",
            "ProtectedDelayedPayout",
        ];
        string[] productionRoots =
        [
            "Explore.Domain",
            "Explore.Application",
            "Explore.Persistence",
            "Explore.Infrastructure",
            "Explore.API",
            "Explore.Blazor",
            "Explore.Blazor.Client",
            "Explore.Secrets",
        ];
        string[] files = productionRoots
            .SelectMany(root => Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot, "src", root),
                "*",
                SearchOption.AllDirectories))
            .Where(path =>
                path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Where(path => !excluded.Contains(path, StringComparer.Ordinal))
            .ToArray();
        string[] offenders = files
            .Where(path =>
            {
                string source = File.ReadAllText(path);
                return forbiddenTokens.Any(source.Contains);
            })
            .Select(path => Path.GetRelativePath(RepositoryRoot, path))
            .ToArray();
        await Assert.That(offenders).IsEmpty();
    }

    [Test]
    public async Task OpenApiGeneratedClientAndReleaseFragmentConverge()
    {
        string openApi = await File.ReadAllTextAsync(Path.Combine(
            RepositoryRoot,
            "schemas",
            "openapi_islamu-event.json"));
        await Assert.That(openApi).Contains(
            "/api/deployment/ticketing-capabilities");
        await Assert.That(openApi).DoesNotContain(
            "/api/deployment/protected-delayed-payout");

        string client = await File.ReadAllTextAsync(Path.Combine(
            RepositoryRoot,
            "src",
            "Explore.Blazor.Client",
            "Clients",
            "EventApiTagClients.g.cs"));
        await Assert.That(client).Contains(
            "GetTicketingDeploymentCapabilitiesAsync");
        await Assert.That(client).DoesNotContain(
            "ProtectedDelayedPayoutAsync");

        await Assert.That(File.Exists(Path.Combine(
                RepositoryRoot,
                "docs",
                "internal",
                "releases",
                "changes",
                "CHG-01M15N7V6Q2K8R4Y9T3W5X0ZAB.yaml")))
            .IsTrue();
    }
}
