using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Authorization.ParityCorpus;
using Explore.Infrastructure.Services;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

/// <summary>
/// Answers every corpus scenario the Cerbos lane owns against a real PDP running the repository's own
/// policy bundle.
/// <para>
/// Resource attributes are produced by the production
/// <see cref="AuthorizationFactAttributeProjection"/> rather than hand-written here, so this lane also
/// proves the projection is faithful: a projection that dropped or renamed an attribute would change the
/// PDP's answer and fail this test.
/// </para>
/// </summary>
[Category(TestCategories.PolicyContract)]
[NotInParallel("SecurityInfra")]
[ClassDataSource<SecurityInfrastructureFixture>(Shared = SharedType.PerAssembly)]
public sealed class CerbosProviderParityLaneTests : IDisposable
{
    private const string ArtifactDirectory = ".omo/evidence/authorization-platform-redesign/phase2-task23-parity";
    private const string ReportFileName = "cerbos-lane-report.json";
    private const string AuthenticatedUserRole = "islamuevent_authenticated_user";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly CerbosTestClient _cerbos;

    public CerbosProviderParityLaneTests(SecurityInfrastructureFixture infra)
    {
        _cerbos = new CerbosTestClient(infra.CerbosHttpEndpoint);
    }

    public void Dispose() => _cerbos.Dispose();

    [Test]
    public async Task CerbosLane_AnswersEveryCorpusScenarioAsSpecified()
    {
        var diagnostics = new List<CerbosParityDiagnostic>();
        var mismatches = new List<string>();

        foreach (var scenario in ParityCorpus.For(ParityLane.Cerbos))
        {
            var effects = await _cerbos.CheckResourceAsync(
                principalId: PrincipalId(scenario.Subject),
                principalRoles: [AuthenticatedUserRole],
                principalAttrs: PrincipalAttributes(scenario.Subject),
                resourceKind: scenario.ResourceKind,
                resourceId: ParityCorpus.EventId.ToString("D"),
                resourceAttrs: ResourceAttributes(scenario),
                actions: [scenario.Action]);

            var effect = effects.TryGetValue(scenario.Action, out var value) ? value : "EFFECT_UNSPECIFIED";
            var actual = effect == "EFFECT_ALLOW";

            diagnostics.Add(new CerbosParityDiagnostic(
                scenario.Id,
                scenario.Category,
                scenario.Capability,
                Expected: Outcome(scenario.ExpectedAllowed),
                Actual: Outcome(actual),
                Provider: "cerbos-live",
                Effect: effect));

            if (actual != scenario.ExpectedAllowed)
            {
                mismatches.Add($"{scenario.Id} ({scenario.Capability}): expected " +
                    $"{Outcome(scenario.ExpectedAllowed)}, got {effect} — {scenario.Rationale}");
            }
        }

        await WriteReportAsync(diagnostics);

        await Assert.That(mismatches).IsEmpty();
    }

    private static string PrincipalId(ParitySubject subject) => $"parity-{subject}".ToLowerInvariant();

    /// <summary>
    /// Materializes the corpus subject in Cerbos' vocabulary. The membership maps mirror what
    /// <c>CerbosPrincipalBuilder</c> emits, so the derived roles resolve exactly as they do in production.
    /// </summary>
    private static object PrincipalAttributes(ParitySubject subject)
    {
        var tenant = ParityCorpus.TenantId.ToString("D");
        var organization = ParityCorpus.OrganizationId.ToString("D");

        return subject switch
        {
            ParitySubject.StandardUser => new
            {
                isInstanceAdmin = false,
                tenantMemberships = new Dictionary<string, string>(),
                orgMemberships = new Dictionary<string, string>(),
                userId = ParityCorpus.UserId.ToString("D"),
            },

            ParitySubject.TenantAdmin => new
            {
                isInstanceAdmin = false,
                tenantMemberships = new Dictionary<string, string> { [tenant] = "admin" },
                orgMemberships = new Dictionary<string, string>(),
                userId = ParityCorpus.UserId.ToString("D"),
            },

            ParitySubject.OrganizationAdmin => new
            {
                isInstanceAdmin = false,
                tenantMemberships = new Dictionary<string, string>(),
                orgMemberships = new Dictionary<string, string> { [organization] = "admin" },
                userId = ParityCorpus.UserId.ToString("D"),
            },

            ParitySubject.EventOwner => EventRolePrincipalAttributes(tenant, "event.owner"),

            ParitySubject.EventManager => EventRolePrincipalAttributes(tenant, "event.manager"),

            ParitySubject.InstanceAdmin => new
            {
                isInstanceAdmin = true,
                tenantMemberships = new Dictionary<string, string>(),
                orgMemberships = new Dictionary<string, string>(),
                userId = ParityCorpus.UserId.ToString("D"),
            },

            ParitySubject.MachineCaller => new
            {
                isInstanceAdmin = false,
                tenantMemberships = new Dictionary<string, string> { [tenant] = "admin" },
                orgMemberships = new Dictionary<string, string>(),
                userId = ParityCorpus.UserId.ToString("D"),
                is_machine = true,
            },

            ParitySubject.EventOwnerWithoutAdmissionPermission => AdmissionPrincipal(
                tenant,
                ["event.owner"],
                []),

            ParitySubject.AdmissionViewer => AdmissionPrincipal(
                tenant,
                ["event.check_in_staff"],
                ["event_check_in:view"]),

            ParitySubject.AdmissionManager => AdmissionPrincipal(
                tenant,
                ["event.check_in_staff"],
                ["event_check_in:manage"]),

            _ => throw new ArgumentOutOfRangeException(
                nameof(subject),
                subject,
                "Anonymous callers never reach the PDP and must not be routed to the Cerbos lane."),
        };
    }

    private static object AdmissionPrincipal(
        string tenantId,
        string[] roles,
        string[] permissions) => new
    {
        isInstanceAdmin = false,
        tenantMemberships = new Dictionary<string, string>(),
        orgMemberships = new Dictionary<string, string>(),
        userId = ParityCorpus.UserId.ToString("D"),
        eventAssignments = new Dictionary<string, object>
        {
            [ParityCorpus.EventId.ToString("D")] = new
            {
                tenantId,
                roles,
                permissions
            }
        }
    };

    private static object EventRolePrincipalAttributes(string tenant, string role) => new
    {
        isInstanceAdmin = false,
        tenantMemberships = new Dictionary<string, string>(),
        orgMemberships = new Dictionary<string, string>(),
        userId = ParityCorpus.UserId.ToString("D"),
        eventAssignments = new Dictionary<string, object>
        {
            [ParityCorpus.EventId.ToString("D")] = new
            {
                tenantId = tenant,
                roles = new[] { role },
                permissions = new[] { "event:publish" },
            }
        },
    };

    /// <summary>
    /// Uses the production projection so this lane cannot drift from what the Cerbos adapter really sends.
    /// A scenario with no facts sends no attributes, which is exactly what the adapter does.
    /// </summary>
    private static object ResourceAttributes(ParityScenario scenario) =>
        AuthorizationFactAttributeProjection.ToAttributes(scenario.Facts)
            ?? new Dictionary<string, object>();

    private static string Outcome(bool allowed) => allowed ? "allow" : "deny";

    private static async Task WriteReportAsync(IReadOnlyList<CerbosParityDiagnostic> diagnostics)
    {
        var root = FindRepositoryRoot();
        if (root is null)
            return;

        var directory = Path.Combine(root, ArtifactDirectory);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            Path.Combine(directory, ReportFileName),
            JsonSerializer.Serialize(diagnostics, JsonOptions));
    }

    private static string? FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "cerbos")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName;
    }
}

/// <summary>Bounded decision diagnostic. Carries no identifier, subject, or fact value.</summary>
public sealed record CerbosParityDiagnostic(
    string ScenarioId,
    string Category,
    string Capability,
    string Expected,
    string Actual,
    string Provider,
    string Effect);
