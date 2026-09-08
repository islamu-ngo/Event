using System.Text.Json;
using Explore.Application.Authorization;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Authorization.ParityCorpus;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Authorization;

/// <summary>
/// Evaluates every corpus scenario the Local lane owns against the real
/// <see cref="FallbackAuthorizationService"/> — no seeded decisions, so a policy change that alters an
/// outcome fails here rather than being absorbed by a mock.
/// </summary>
public sealed class LocalProviderParityLaneTests
{
    private const string ArtifactDirectory = ".omo/evidence/authorization-platform-redesign/phase2-task23-parity";
    private const string ReportFileName = "local-lane-report.json";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    [Test]
    public async Task LocalLane_AnswersEveryCorpusScenarioAsSpecified()
    {
        var diagnostics = new List<ParityDiagnostic>();
        var mismatches = new List<string>();

        foreach (var scenario in ParityCorpus.For(ParityLane.Local))
        {
            var service = CreateService(scenario.Subject);
            var decisions = await service.AuthorizeBatchAsync(
            [
                new AuthorizationRequest(
                    scenario.ResourceKind,
                    ParityCorpus.EventId.ToString("D"),
                    scenario.Action,
                    Facts: scenario.Facts)
            ]);

            var actual = decisions[0].IsAllowed;
            diagnostics.Add(new ParityDiagnostic(
                scenario.Id,
                scenario.Category,
                scenario.Capability,
                Expected: Outcome(scenario.ExpectedAllowed),
                Actual: Outcome(actual),
                Provider: decisions[0].Provider.ProviderId,
                Reason: decisions[0].ReasonCode,
                Revision: decisions[0].Provider.ObservedRevision));

            if (actual != scenario.ExpectedAllowed)
            {
                mismatches.Add($"{scenario.Id} ({scenario.Capability}): expected " +
                    $"{Outcome(scenario.ExpectedAllowed)}, got {Outcome(actual)} — {scenario.Rationale}");
            }
        }

        await WriteReportAsync(diagnostics);

        await Assert.That(mismatches).IsEmpty();
    }

    [Test]
    public async Task LocalLane_OrdinaryPublishAuthorityRemainsCharacterized()
    {
        var publishScenarios = ParityCorpus.For(ParityLane.Local)
            .Where(scenario => scenario.Action == AuthorizationActions.Events.Publish)
            .ToArray();

        await Assert.That(publishScenarios).IsNotEmpty();
        foreach (var scenario in publishScenarios)
        {
            var decision = await CreateService(scenario.Subject).AuthorizeAsync(new AuthorizationRequest(
                scenario.ResourceKind,
                ParityCorpus.EventId.ToString("D"),
                scenario.Action,
                Facts: scenario.Facts));

            await Assert.That(decision.IsAllowed).IsEqualTo(scenario.ExpectedAllowed)
                .Because(scenario.Rationale);
        }
    }

    [Test]
    public async Task ConfigurationManifestExport_RequiresInstanceAdminAndExplicitExportFact()
    {
        var authorizedRequest = new AuthorizationRequest(
            ResourceKinds.InstanceSetting,
            ExportConfigurationManifestQuery.ResourceKey,
            AuthorizationActions.InstanceSettings.View,
            Facts: new ConfigurationManifestExportAuthorizationFacts());
        var missingFactRequest = authorizedRequest with { Facts = null };

        AuthorizationDecision instanceAdmin =
            await CreateService(ParitySubject.InstanceAdmin).AuthorizeAsync(authorizedRequest);
        AuthorizationDecision tenantAdmin =
            await CreateService(ParitySubject.TenantAdmin).AuthorizeAsync(authorizedRequest);
        AuthorizationDecision missingFact =
            await CreateService(ParitySubject.InstanceAdmin).AuthorizeAsync(missingFactRequest);

        await Assert.That(instanceAdmin.IsAllowed).IsTrue();
        await Assert.That(tenantAdmin.IsAllowed).IsFalse();
        await Assert.That(missingFact.IsAllowed).IsFalse();
    }

    /// <summary>
    /// Guards the corpus itself: a Phase 0 category that loses its last scenario would otherwise silently
    /// stop being verified.
    /// </summary>
    [Test]
    public async Task Corpus_CoversEveryRequiredPhase0Category()
    {
        var covered = ParityCorpus.Scenarios
            .Select(scenario => scenario.Category)
            .ToHashSet(StringComparer.Ordinal);

        var missing = ParityCorpus.RequiredCategories
            .Where(category => !covered.Contains(category))
            .ToArray();

        await Assert.That(missing).IsEmpty();
    }

    /// <summary>
    /// Every scenario must state why its expected outcome is correct, so flipping an expectation requires
    /// rewriting an argument rather than a boolean.
    /// </summary>
    [Test]
    public async Task Corpus_EveryScenarioStatesItsRationale()
    {
        var unexplained = ParityCorpus.Scenarios
            .Where(scenario => string.IsNullOrWhiteSpace(scenario.Rationale))
            .Select(scenario => scenario.Id)
            .ToArray();

        await Assert.That(unexplained).IsEmpty();
    }

    /// <summary>
    /// Phase 4 acceptance: equivalent detail and query decisions must agree for the same resource.
    /// <para>
    /// The direction that matters is one-way. A subject allowed to read a <em>collection</em> of a resource
    /// while denied that resource's <em>detail</em> is a disclosure — they receive rows they are not
    /// permitted to open, and the row content, the count, and the pagination shape all leak. The reverse
    /// (detail allowed, collection denied) is merely conservative and is not asserted against.
    /// </para>
    /// <para>
    /// Every catalogued protected collection is checked against every subject in the corpus, using the real
    /// <see cref="FallbackAuthorizationService"/>. The pairing comes from
    /// <see cref="SensitiveCollectionCatalog"/> rather than a hand-written list, so a collection added to
    /// the catalog is covered here automatically instead of quietly escaping the invariant.
    /// </para>
    /// <para>
    /// The two sides are issued the way production issues them, which is not symmetric. A collection request
    /// carries only a resource id — <c>GetModerationReportQueueRequest</c>, for instance, declares no
    /// <c>AuthorizationFacts</c> — whereas a detail request is fact-resolved server-side before it reaches a
    /// provider. Handing both sides null facts instead makes the detail check deny for everyone with
    /// <c>missing_event_context</c> and reports a violation that cannot happen in production. The detail side
    /// therefore gets the facts a trusted resolver would supply.
    /// </para>
    /// <para>
    /// <b>Scope: collections gated on the resource's own read action.</b> A collection gated on a distinct
    /// oversight capability is deliberately a different authority, and comparing it against read is a
    /// category error. `IsInstanceAdminFallbackAllowed` grants instance admins
    /// <c>view-management</c>, <c>moderate-light</c>, <c>moderate-heavy</c>, and <c>unmoderate</c> on events
    /// while withholding <c>view</c> — that is the documented "operates the infrastructure, cannot access
    /// tenant business data" boundary, letting a platform operator act on abuse reports without browsing
    /// tenant content. Asserting read-agreement there would demand the invariant be broken.
    /// </para>
    /// </summary>
    [Test]
    public async Task LocalLane_NeverAllowsASensitiveCollectionWhoseResourceDetailIsDenied()
    {
        var violations = new List<string>();
        var observedCollectionAllows = 0;

        var readGatedCollections = SensitiveCollectionCatalog.Protected
            .Where(collection => collection.Action == AuthorizationActions.View)
            .ToArray();

        await Assert.That(readGatedCollections).IsNotEmpty()
            .Because("the invariant is vacuous if no catalogued collection is gated on the read action.");

        foreach (var collection in readGatedCollections)
        {
            foreach (var subject in Enum.GetValues<ParitySubject>())
            {
                var service = CreateService(subject);
                var resourceId = ParityCorpus.EventId.ToString("D");

                var decisions = await service.AuthorizeBatchAsync(
                [
                    new AuthorizationRequest(
                        collection.ResourceKind,
                        resourceId,
                        collection.Action,
                        Facts: null),
                    new AuthorizationRequest(
                        collection.ResourceKind,
                        resourceId,
                        AuthorizationActions.View,
                        Facts: DetailFactsFor(collection.ResourceKind))
                ]);

                var collectionAllowed = decisions[0].IsAllowed;
                var detailAllowed = decisions[1].IsAllowed;

                if (collectionAllowed)
                {
                    observedCollectionAllows++;
                }

                if (collectionAllowed && !detailAllowed)
                {
                    violations.Add(
                        $"{collection.CollectionName} ({subject}): collection capability " +
                        $"'{collection.ResourceKind}:{collection.Action}' was allowed while detail " +
                        $"'{collection.ResourceKind}:{AuthorizationActions.View}' was denied — rows would be " +
                        "returned that the subject cannot open.");
                }
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because(string.Join(Environment.NewLine, violations));

        // Self-check against silent vacuity. The two requests differ only in their facts — the collection
        // side carries none, the detail side carries what a resolver supplies — so what this actually
        // proves is that a fact-less read never outranks a fact-bearing one, i.e. missing facts fail
        // closed rather than open. If no subject is ever allowed the collection, nothing is being proved
        // and the test would pass no matter how the evaluator changed.
        await Assert.That(observedCollectionAllows).IsGreaterThan(0)
            .Because("no subject was allowed any read-gated sensitive collection, so this invariant "
                + "exercised nothing. Either the corpus subjects or the catalogued collections changed.");
    }

    /// <summary>
    /// The facts a trusted resolver would attach to a detail request for this resource kind. Kinds with no
    /// event context get <c>null</c>, which is what their resolvers genuinely produce.
    /// </summary>
    private static IAuthorizationFacts? DetailFactsFor(string resourceKind) =>
        resourceKind == ResourceKinds.Event ? ParityCorpus.OrganizationOwnedEvent() : null;

    private static FallbackAuthorizationService CreateService(ParitySubject subject)
    {
        var adminContext = Substitute.For<IAdminContext>();
        var machinePrincipalAccessor = Substitute.For<IMachinePrincipalAccessor>();
        var organizationMembers = Substitute.For<IOrganizationMemberRepository>();
        var groupMembers = Substitute.For<IGroupMemberRepository>();
        var eventAuthority = new ParityEventAuthoritySnapshotService(subject);
        var settingsResolver = Substitute.For<IHierarchicalSettingsResolver>();
        var tenantContext = Substitute.For<ITenantContext>();

        tenantContext.TenantId.Returns(ParityCorpus.TenantId);
        machinePrincipalAccessor.IsMachineCaller.Returns(false);
        machinePrincipalAccessor.Current.Returns((ApiKeyPrincipalContext?)null);
        adminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);
        adminContext.IsTenantAdminAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        adminContext.IsOrganizationAdminAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        adminContext.GetAdminTenantIdsAsync(Arg.Any<CancellationToken>()).Returns([]);
        adminContext.GetAdminOrganizationIdsAsync(Arg.Any<CancellationToken>()).Returns([]);
        adminContext.GetAdminGroupIdsAsync(Arg.Any<CancellationToken>()).Returns([]);
        adminContext.UserId.Returns(ParityCorpus.UserId);

        switch (subject)
        {
            case ParitySubject.Anonymous:
                adminContext.UserId.Returns((Guid?)null);
                adminContext.ResolveUserIdAsync(Arg.Any<CancellationToken>()).Returns((Guid?)null);
                break;

            case ParitySubject.StandardUser:
                break;

            case ParitySubject.TenantAdmin:
                adminContext.IsTenantAdminAsync(ParityCorpus.TenantId, Arg.Any<CancellationToken>()).Returns(true);
                adminContext.GetAdminTenantIdsAsync(Arg.Any<CancellationToken>())
                    .Returns([ParityCorpus.TenantId]);
                break;

            case ParitySubject.OrganizationAdmin:
                adminContext.IsOrganizationAdminAsync(ParityCorpus.OrganizationId, Arg.Any<CancellationToken>())
                    .Returns(true);
                adminContext.GetAdminOrganizationIdsAsync(Arg.Any<CancellationToken>())
                    .Returns([ParityCorpus.OrganizationId]);
                break;

            case ParitySubject.EventOwner:
            case ParitySubject.EventManager:
                break;

            case ParitySubject.InstanceAdmin:
                adminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(true);
                break;

            case ParitySubject.MachineCaller:
                machinePrincipalAccessor.IsMachineCaller.Returns(true);
                machinePrincipalAccessor.Current.Returns(new ApiKeyPrincipalContext(
                    "parity-machine-key",
                    ParityCorpus.TenantId,
                    ExternalApiKeyOwnerType.User,
                    ParityCorpus.UserId,
                    [ExternalApiKeyScopes.EventsWrite]));
                break;

            case ParitySubject.EventOwnerWithoutAdmissionPermission:
            case ParitySubject.AdmissionViewer:
            case ParitySubject.AdmissionManager:
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(subject), subject, "Unmapped parity subject.");
        }

        return new FallbackAuthorizationService(
            adminContext,
            machinePrincipalAccessor,
            eventAuthority,
            organizationMembers,
            groupMembers,
            settingsResolver,
            tenantContext,
            Substitute.For<ILogger<FallbackAuthorizationService>>());
    }

    private static string Outcome(bool allowed) => allowed ? "allow" : "deny";

    /// <summary>
    /// Writes only capability, category, outcome, provider, reason, and revision. No subject, tenant,
    /// resource identifier, or fact value ever reaches the artifact.
    /// </summary>
    private static async Task WriteReportAsync(IReadOnlyList<ParityDiagnostic> diagnostics)
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

    private sealed class ParityEventAuthoritySnapshotService(ParitySubject subject) : IEventAuthoritySnapshotService
    {
        public Task<EventAuthoritySnapshot> GetForUserAndEventsAsync(
            Guid tenantId,
            Guid userId,
            IReadOnlyCollection<Guid> eventIds,
            CancellationToken cancellationToken)
        {
            var events = new Dictionary<Guid, EventAuthorityForUser>();
            var authority = subject switch
            {
                ParitySubject.EventOwner => CreateAuthority(
                    "event.owner",
                    [PermissionCodes.EventPublish, PermissionCodes.EventRegistrationManage],
                    isOwner: true,
                    isManager: true),
                ParitySubject.EventManager => CreateAuthority(
                    "event.manager",
                    [PermissionCodes.EventPublish, PermissionCodes.EventRegistrationManage],
                    isOwner: false,
                    isManager: true),
                ParitySubject.EventOwnerWithoutAdmissionPermission => CreateAuthority(
                    "event.owner",
                    [],
                    isOwner: true,
                    isManager: false),
                ParitySubject.AdmissionViewer => CreateAuthority(
                    "event.check_in_staff",
                    [PermissionCodes.EventCheckInView],
                    isOwner: false,
                    isManager: false),
                ParitySubject.AdmissionManager => CreateAuthority(
                    "event.check_in_staff",
                    [PermissionCodes.EventCheckInManage],
                    isOwner: false,
                    isManager: false),
                _ => null
            };

            if (authority is not null)
            {
                foreach (var eventId in eventIds)
                {
                    events[eventId] = authority;
                }
            }

            return Task.FromResult(new EventAuthoritySnapshot(tenantId, userId, events));
        }

        private static EventAuthorityForUser CreateAuthority(
            string roleCode,
            string[] permissionCodes,
            bool isOwner,
            bool isManager) =>
            new(
                new HashSet<string>(StringComparer.Ordinal) { roleCode },
                permissionCodes.ToHashSet(StringComparer.Ordinal),
                isOwner,
                isManager);
    }
}

/// <summary>Bounded decision diagnostic. Deliberately carries no identifier or fact value.</summary>
public sealed record ParityDiagnostic(
    string ScenarioId,
    string Category,
    string Capability,
    string Expected,
    string Actual,
    string Provider,
    string? Reason,
    string? Revision);
