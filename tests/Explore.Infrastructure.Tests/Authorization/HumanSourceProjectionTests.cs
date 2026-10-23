using System.Collections.Immutable;
using System.Security.Claims;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Application.Settings;
using Explore.Application.Utilities;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;

namespace Explore.Infrastructure.Tests.Authorization;

public sealed class HumanSourceProjectionTests
{
    private static readonly Guid UserId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000081");
    private static readonly Guid OtherId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000082");
    private static readonly Guid TenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000083");
    private static readonly Guid BindingId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000084");
    private static readonly Guid OperationId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000085");
    private static readonly Guid ActorId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000086");
    private const string GoogleAccount = "oidc:27:https://accounts.google.com:OriginalSubject";
    private static readonly HumanAuthorizationRouteDeployment Instance =
        new("cerbos", "http://localhost:3593", true);

    [Test]
    public async Task CurrentExternalBindingIsAcceptedWithoutLocalCredentialFacts()
    {
        HumanCommitSubject subject = await ResolveSubject();

        HumanPrincipalSourceValidation.RequireCurrent(subject, null, SourceFacts());

        await Assert.That(subject.PrincipalId).IsEqualTo(UserId);
        await Assert.That(subject.AccountKey!.Value).IsEqualTo(GoogleAccount);
    }

    [Test]
    [Arguments("principal")]
    [Arguments("missing-user")]
    [Arguments("ambiguous-user")]
    [Arguments("different-user")]
    [Arguments("deleted-user")]
    [Arguments("missing-binding")]
    [Arguments("ambiguous-binding")]
    [Arguments("reassigned-binding")]
    [Arguments("different-provider")]
    public async Task ExternalAccountContinuityRejectsMissingAmbiguousOrChangedSources(string change)
    {
        HumanCommitSubject subject = await ResolveSubject();
        HumanPrincipalSourceFacts original = SourceFacts();
        HumanPrincipalSourceFacts changed = change switch
        {
            "principal" => original with { PrincipalId = OtherId },
            "missing-user" => original with { Users = [] },
            "ambiguous-user" => original with { Users = [original.Users[0], original.Users[0]] },
            "different-user" => original with { Users = [original.Users[0] with { Id = OtherId }] },
            "deleted-user" => original with { Users = [original.Users[0] with { IsDeleted = true }] },
            "missing-binding" => original with { Bindings = [] },
            "ambiguous-binding" => original with
            {
                Bindings = [original.Bindings[0], original.Bindings[0] with { Id = OtherId }]
            },
            "reassigned-binding" => original with
            {
                Bindings = [original.Bindings[0] with { UserId = OtherId }]
            },
            "different-provider" => original with
            {
                Bindings = [original.Bindings[0] with
                {
                    AuthenticationProviderId = (int)AuthenticationProviderKind.Keycloak
                }]
            },
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };

        Assert.Throws<UnauthorizedAccessException>(() =>
            HumanPrincipalSourceValidation.RequireCurrent(subject, null, changed));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExternalAccountCannotAcquireLocalSessionAuthority(bool localFacts)
    {
        HumanCommitSubject subject = await ResolveSubject();
        HumanPrincipalSourceFacts facts = SourceFacts();
        LocalSessionAuthority? session = localFacts ? null : Session();
        if (localFacts)
            facts = facts with { LocalSession = ReadyLocalFacts() };

        Assert.Throws<UnauthorizedAccessException>(() =>
            HumanPrincipalSourceValidation.RequireCurrent(subject, session, facts));
    }

    [Test]
    public async Task InternalUserClaimAloneCannotSatisfyProviderSourceContinuity()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sid", UserId.ToString("D"))], "interactive"));
        HumanCommitSubject? subject = await HumanCommitSubject.ResolveAsync(
            principal, new IdentityQuery(), CancellationToken.None);
        await Assert.That(subject).IsNotNull();
        await Assert.That(subject!.AccountKey).IsNull();

        Assert.Throws<UnauthorizedAccessException>(() =>
            HumanPrincipalSourceValidation.RequireCurrent(subject!, null, SourceFacts()));
    }

    [Test]
    [Arguments(LocalCredentialOperationKind.Create)]
    [Arguments(LocalCredentialOperationKind.Reset)]
    public async Task ReadyLocalCredentialRequiresTheCompletedOperationBinding(LocalCredentialOperationKind kind)
    {
        HumanCommitSubject subject = await ResolveSubject(local: true);
        HumanPrincipalLocalSessionFacts local = ReadyLocalFacts();
        local = local with { Operations = [local.Operations[0] with { Kind = (int)kind }] };

        HumanPrincipalSourceValidation.RequireCurrent(subject, Session(), LocalSourceFacts(local));

        await Assert.That(subject.AccountKey!.ProviderKind).IsEqualTo(AuthenticationProviderKind.Local);
    }

    [Test]
    [Arguments("missing-authority")]
    [Arguments("foreign-authority")]
    [Arguments("missing-local-facts")]
    [Arguments("missing-account")]
    [Arguments("ambiguous-account")]
    [Arguments("foreign-account")]
    [Arguments("password-removed")]
    [Arguments("stamp-changed")]
    [Arguments("email-changed")]
    [Arguments("missing-state")]
    [Arguments("provisioning")]
    [Arguments("change-required")]
    [Arguments("foreign-state")]
    [Arguments("missing-operation")]
    [Arguments("unrelated-operation")]
    [Arguments("foreign-operation")]
    [Arguments("unknown-kind")]
    [Arguments("unfinished-operation")]
    [Arguments("superseded-operation")]
    [Arguments("abandoned-operation")]
    [Arguments("different-binding")]
    [Arguments("missing-actor")]
    [Arguments("different-actor")]
    [Arguments("foreign-actor")]
    [Arguments("nonpersonal-actor")]
    [Arguments("deleted-actor")]
    [Arguments("suspended-actor")]
    public async Task LocalCredentialContinuityFailsClosedForEachRevocationInput(string change)
    {
        HumanCommitSubject subject = await ResolveSubject(local: true);
        HumanPrincipalLocalSessionFacts local = ReadyLocalFacts();
        HumanPrincipalLocalAccountFact account = local.Accounts[0];
        HumanPrincipalLocalOperationFact operation = local.Operations[0];
        HumanPrincipalPersonalActorFact actor = local.PersonalActors[0];
        LocalSessionAuthority? session = change switch
        {
            "missing-authority" => null,
            "foreign-authority" => new(OtherId, Guid.NewGuid().ToString("N"), true),
            _ => Session()
        };
        HumanPrincipalLocalSessionFacts? changed = change switch
        {
            "missing-local-facts" => null,
            "missing-account" => local with { Accounts = [] },
            "ambiguous-account" => local with { Accounts = [account, account] },
            "foreign-account" => local with { Accounts = [account with { Id = OtherId }] },
            "password-removed" => local with { Accounts = [account with { HasPassword = false }] },
            "stamp-changed" => local with { Accounts = [account with { StampMatches = false }] },
            "email-changed" => local with { Accounts = [account with { EmailConfirmed = false }] },
            "missing-state" => local with { CredentialState = null },
            "provisioning" => local with { CredentialState = State(LocalCredentialState.ProvisioningPending) },
            "change-required" => local with { CredentialState = State(LocalCredentialState.ChangeRequired) },
            "foreign-state" => local with { CredentialState = State(user: OtherId) },
            "missing-operation" => local with { Operations = [] },
            "unrelated-operation" => local with { Operations = [operation with { Id = OtherId }] },
            "foreign-operation" => local with { Operations = [operation with { LocalSubjectId = OtherId }] },
            "unknown-kind" => local with { Operations = [operation with { Kind = int.MaxValue }] },
            "unfinished-operation" => local with
            {
                Operations = [operation with { Stage = (int)LocalCredentialOperationStage.ChangeRequired }]
            },
            "superseded-operation" => local with
            {
                Operations = [operation with { Stage = (int)LocalCredentialOperationStage.Superseded }]
            },
            "abandoned-operation" => local with
            {
                Operations = [operation with { Stage = (int)LocalCredentialOperationStage.Abandoned }]
            },
            "different-binding" => local with { Operations = [operation with { ExternalLoginId = OtherId }] },
            "missing-actor" => local with { PersonalActors = [] },
            "different-actor" => local with { PersonalActors = [actor with { Id = OtherId }] },
            "foreign-actor" => local with { PersonalActors = [actor with { UserId = OtherId }] },
            "nonpersonal-actor" => local with { PersonalActors = [actor with { ActorTypeId = int.MaxValue }] },
            "deleted-actor" => local with { PersonalActors = [actor with { IsDeleted = true }] },
            "suspended-actor" => local with { PersonalActors = [actor with { IsSuspended = true }] },
            "missing-authority" or "foreign-authority" => local,
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };

        Assert.Throws<UnauthorizedAccessException>(() =>
            HumanPrincipalSourceValidation.RequireCurrent(subject, session, LocalSourceFacts(changed)));
    }

    [Test]
    [Arguments(false)]
    [Arguments(null)]
    public async Task UnverifiedLocalEmailRemainsUsableOnlyWithoutEnabledDelivery(bool? delivery)
    {
        HumanCommitSubject subject = await ResolveSubject(local: true);
        HumanPrincipalLocalSessionFacts local = ReadyLocalFacts();
        local = local with
        {
            Accounts = [local.Accounts[0] with { EmailConfirmed = false }],
            EmailDeliveryEnabled = delivery
        };

        HumanPrincipalSourceValidation.RequireCurrent(subject, Session(verified: false), LocalSourceFacts(local));
    }

    [Test]
    public async Task EnablingEmailDeliveryRevokesAnUnverifiedLocalSession()
    {
        HumanCommitSubject subject = await ResolveSubject(local: true);
        HumanPrincipalLocalSessionFacts local = ReadyLocalFacts();
        local = local with
        {
            Accounts = [local.Accounts[0] with { EmailConfirmed = false }],
            EmailDeliveryEnabled = true
        };

        Assert.Throws<UnauthorizedAccessException>(() =>
            HumanPrincipalSourceValidation.RequireCurrent(subject, Session(verified: false), LocalSourceFacts(local)));
    }

    [Test]
    [Arguments("local", HumanAuthorizationRouteProvider.Local)]
    [Arguments("cerbos", HumanAuthorizationRouteProvider.CerbosInstance)]
    public async Task PlatformGovernanceNeverUsesTheTenantsByoDecisionEndpoint(
        string provider, HumanAuthorizationRouteProvider expected)
    {
        HumanAuthorizationRoute route = HumanAuthorizationRouteProjector.Project(
            ByoFacts(), Instance with { Provider = provider }, settingsGovernance: true,
            localDecisionSourcesCoLocated: true);

        await Assert.That(route.Provider).IsEqualTo(expected);
        await Assert.That(route.Endpoint).IsEqualTo(provider == "cerbos" ? "http://localhost:3593" : null);
        await Assert.That(route.AdminEndpoint).IsNull();
    }

    [Test]
    [Arguments("local")]
    [Arguments("cerbos")]
    public async Task TenantByoPrecedesEitherDeploymentProvider(string provider)
    {
        HumanAuthorizationRoute route = HumanAuthorizationRouteProjector.Project(
            ByoFacts(), Instance with { Provider = provider }, settingsGovernance: false);

        await Assert.That(route.Provider).IsEqualTo(HumanAuthorizationRouteProvider.CerbosTenant);
        await Assert.That(route.Endpoint).IsEqualTo("https://tenant-pdp.example.test:3593");
        await Assert.That(route.PolicyScope).IsEqualTo(TenantId.ToString("D"));
        await Assert.That(route.PolicyVersion).IsNull();
        await Assert.That(route.DenialReason).IsNull();
    }

    [Test]
    [Arguments("missing")]
    [Arguments("empty")]
    [Arguments("whitespace")]
    [Arguments("malformed-empty-string")]
    public async Task MissingTenantEndpointDeniesRatherThanFallingBackToTheInstance(string endpoint)
    {
        HumanAuthorizationRouteFacts facts = Set(ByoFacts(), GovernanceSettingKeys.Cerbos.CustomEndpoint,
            system: null, tenant: endpoint switch
            {
                "missing" => null,
                "empty" => "\"\"",
                "whitespace" => "\"   \"",
                "malformed-empty-string" => "\"",
                _ => throw new ArgumentOutOfRangeException(nameof(endpoint))
            });

        HumanAuthorizationRoute route = HumanAuthorizationRouteProjector.Project(facts, Instance, false);

        await Assert.That(route.Provider).IsEqualTo(HumanAuthorizationRouteProvider.Denied);
        await Assert.That(route.DenialReason).IsEqualTo("custom_endpoint_missing");
        await Assert.That(route.Endpoint).IsNull();
    }

    [Test]
    [Arguments("principal")]
    [Arguments("tenant")]
    [Arguments("default-settings")]
    [Arguments("missing-key")]
    [Arguments("extra-key")]
    [Arguments("duplicate-key")]
    [Arguments("wrong-order")]
    [Arguments("unknown-key")]
    [Arguments("tenant-row-without-tenant")]
    public void RouteProjectionRejectsMalformedFixedKeyFacts(string change)
    {
        HumanAuthorizationRouteFacts facts = ByoFacts();
        facts = change switch
        {
            "principal" => facts with { PrincipalId = Guid.Empty },
            "tenant" => facts with { SelectedTenantId = Guid.Empty },
            "default-settings" => facts with { Settings = default },
            "missing-key" => facts with { Settings = facts.Settings.RemoveAt(0) },
            "extra-key" => facts with { Settings = facts.Settings.Add(facts.Settings[0]) },
            "duplicate-key" => facts with { Settings = facts.Settings.SetItem(1, facts.Settings[0]) },
            "wrong-order" => facts with { Settings = facts.Settings.Reverse().ToImmutableArray() },
            "unknown-key" => facts with
            {
                Settings = facts.Settings.SetItem(0, facts.Settings[0] with { Key = "unknown.setting" })
            },
            "tenant-row-without-tenant" => facts with { SelectedTenantId = null },
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };

        Assert.Throws<InvalidOperationException>(() =>
            HumanAuthorizationRouteProjector.Project(facts, Instance, false));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LocalRouteRequiresExplicitColocatedDecisionSources(bool colocated)
    {
        HumanAuthorizationRoute route = HumanAuthorizationRouteProjector.Project(
            RouteFacts(), Instance with { Provider = "local" }, false, colocated);

        await Assert.That(route.Provider).IsEqualTo(
            colocated ? HumanAuthorizationRouteProvider.Local : HumanAuthorizationRouteProvider.Denied);
        await Assert.That(route.DenialReason).IsEqualTo(
            colocated ? null : "local_decision_sources_not_colocated");
        await Assert.That(route.Endpoint).IsNull();
        await Assert.That(route.PolicyScope).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task InstanceLockControlsWhetherTenantModeCanSelectByo(bool locked)
    {
        HumanAuthorizationRouteFacts facts = Set(ByoFacts(), GovernanceSettingKeys.Cerbos.Mode,
            "\"shared\"", "\"custom_endpoint\"", locked);

        HumanAuthorizationRoute route = HumanAuthorizationRouteProjector.Project(facts, Instance, false);

        await Assert.That(route.Provider).IsEqualTo(locked
            ? HumanAuthorizationRouteProvider.CerbosInstance : HumanAuthorizationRouteProvider.CerbosTenant);
        await Assert.That(route.Endpoint).IsEqualTo(locked
            ? "http://localhost:3593" : "https://tenant-pdp.example.test:3593");
    }

    [Test]
    public async Task LockedEndpointWinsWithoutDisablingTenantByoMode()
    {
        HumanAuthorizationRouteFacts facts = Set(ByoFacts(), GovernanceSettingKeys.Cerbos.CustomEndpoint,
            "\"locked-pdp.example.test:3593\"", "\"tenant-pdp.example.test:3593\"", locked: true);

        HumanAuthorizationRoute route = HumanAuthorizationRouteProjector.Project(facts, Instance, false);

        await Assert.That(route.Provider).IsEqualTo(HumanAuthorizationRouteProvider.CerbosTenant);
        await Assert.That(route.Endpoint).IsEqualTo("https://locked-pdp.example.test:3593");
    }

    [Test]
    [Arguments(null, HumanAuthorizationRouteProvider.Local)]
    [Arguments("\"cerbos\"", HumanAuthorizationRouteProvider.CerbosInstance)]
    [Arguments("\"unsupported\"", HumanAuthorizationRouteProvider.Local)]
    public async Task PersistedInstanceProviderRatherThanTenantOverrideSelectsPlatformRoute(
        string? persisted, HumanAuthorizationRouteProvider expected)
    {
        HumanAuthorizationRouteFacts facts = Set(RouteFacts(), GovernanceSettingKeys.Security.AuthorizationProvider,
            persisted, "\"local\"");

        HumanAuthorizationRoute route = HumanAuthorizationRouteProjector.Project(
            facts, Instance with { Provider = null }, true, localDecisionSourcesCoLocated: true);

        await Assert.That(route.Provider).IsEqualTo(expected);
    }

    [Test]
    public async Task DeploymentOverrideAndSdkEndpointWinOverPersistedRouteRows()
    {
        HumanAuthorizationRouteFacts facts = Set(RouteFacts(), GovernanceSettingKeys.Security.AuthorizationProvider,
            "\"local\"");
        facts = Set(facts, GovernanceSettingKeys.Cerbos.GrpcEndpoint, "\"persisted-pdp.example.test:3593\"");

        HumanAuthorizationRoute route = HumanAuthorizationRouteProjector.Project(facts, Instance, false);

        await Assert.That(route.Provider).IsEqualTo(HumanAuthorizationRouteProvider.CerbosInstance);
        await Assert.That(route.Endpoint).IsEqualTo("http://localhost:3593");
    }

    [Test]
    public async Task DeploymentCaptureDoesNotFollowLaterOptionsMutation()
    {
        var authorization = new AuthorizationProviderDeploymentOptions { Provider = " CERBOS " };
        var cerbos = new CerbosSettings { GrpcEndpoint = "http://localhost:3593/", UsePolicyScope = true };
        HumanAuthorizationRouteDeployment captured = HumanAuthorizationRouteDeployment.Capture(authorization, cerbos);
        authorization.Provider = "local";
        cerbos.GrpcEndpoint = "changed-pdp.example.test:3593";
        cerbos.UsePolicyScope = false;

        HumanAuthorizationRoute route = HumanAuthorizationRouteProjector.Project(RouteFacts(), captured, false);

        await Assert.That(route.Provider).IsEqualTo(HumanAuthorizationRouteProvider.CerbosInstance);
        await Assert.That(route.Endpoint).IsEqualTo("http://localhost:3593");
        await Assert.That(route.PolicyScope).IsEqualTo(TenantId.ToString("D"));
        await Assert.That(route.PolicyVersion).IsNull();
    }

    [Test]
    public async Task AdminEndpointChangeDoesNotInvalidateAnOtherwiseIdenticalDecisionRoute()
    {
        HumanAuthorizationRoute before = HumanAuthorizationRouteProjector.Project(ByoFacts(), Instance, false);
        HumanAuthorizationRoute after = HumanAuthorizationRouteProjector.Project(
            Set(ByoFacts(), GovernanceSettingKeys.Cerbos.CustomAdminEndpoint,
                tenant: "\"admin.example.test:3593\""), Instance, false);

        await Assert.That(after.AdminEndpoint).IsEqualTo("https://admin.example.test:3593");
        await Assert.That(before.AdminEndpoint).IsNull();
        await Assert.That(before.IsSemanticallyEquivalentTo(after)).IsTrue();
        await Assert.That(after.IsSemanticallyEquivalentTo(before)).IsTrue();
    }

    [Test]
    [Arguments("principal")]
    [Arguments("tenant")]
    [Arguments("provider")]
    [Arguments("endpoint")]
    [Arguments("scope")]
    [Arguments("version")]
    [Arguments("denial")]
    public async Task SemanticComparisonIncludesEveryDecisionRouteInput(string change)
    {
        HumanAuthorizationRoute before = HumanAuthorizationRouteProjector.Project(ByoFacts(), Instance, false);
        HumanAuthorizationRoute after = change switch
        {
            "principal" => before with { PrincipalId = OtherId },
            "tenant" => before with { SelectedTenantId = OtherId },
            "provider" => before with { Provider = HumanAuthorizationRouteProvider.CerbosInstance },
            "endpoint" => before with { Endpoint = "https://replacement.example.test:3593" },
            "scope" => before with { PolicyScope = null },
            "version" => before with { PolicyVersion = "replacement" },
            "denial" => before with { DenialReason = "route_revoked" },
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };

        await Assert.That(before.IsSemanticallyEquivalentTo(after)).IsFalse();
        await Assert.That(after.IsSemanticallyEquivalentTo(before)).IsFalse();
    }

    [Test]
    public async Task EquivalentEndpointRepresentationsDoNotCauseFalseRouteDrift()
    {
        HumanAuthorizationRoute before = HumanAuthorizationRouteProjector.Project(ByoFacts(), Instance, false);
        HumanAuthorizationRoute after = HumanAuthorizationRouteProjector.Project(
            Set(ByoFacts(), GovernanceSettingKeys.Cerbos.CustomEndpoint,
                tenant: "\" https://tenant-pdp.example.test:3593/ \""), Instance, false);

        await Assert.That(after.Endpoint).IsEqualTo("https://tenant-pdp.example.test:3593");
        await Assert.That(before.IsSemanticallyEquivalentTo(after)).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PolicyScopeRequiresBothSelectedTenantAndEnabledDeployment(bool tenantSelected)
    {
        HumanAuthorizationRouteFacts facts = RouteFacts() with { SelectedTenantId = tenantSelected ? TenantId : null };
        HumanAuthorizationRoute scoped = HumanAuthorizationRouteProjector.Project(facts, Instance, false);
        HumanAuthorizationRoute unscoped = HumanAuthorizationRouteProjector.Project(
            facts, Instance with { UsePolicyScope = false }, false);

        await Assert.That(scoped.PolicyScope).IsEqualTo(tenantSelected ? TenantId.ToString("D") : null);
        await Assert.That(unscoped.PolicyScope).IsNull();
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    public async Task MissingInstanceEndpointCannotBecomeAnInstanceDecisionRoute(string endpoint)
    {
        HumanAuthorizationRoute route = HumanAuthorizationRouteProjector.Project(
            RouteFacts(), Instance with { InstanceGrpcEndpoint = endpoint }, false);

        await Assert.That(route.Provider).IsEqualTo(HumanAuthorizationRouteProvider.Denied);
        await Assert.That(route.DenialReason).IsEqualTo("instance_endpoint_missing");
        await Assert.That(route.Endpoint).IsNull();
    }

    [Test]
    public async Task MalformedNonemptyTenantEndpointDoesNotSilentlySwitchToThePlatformProvider()
    {
        HumanAuthorizationRoute route = HumanAuthorizationRouteProjector.Project(
            Set(ByoFacts(), GovernanceSettingKeys.Cerbos.CustomEndpoint,
                tenant: "\"https://tenant-pdp.example.test/not-a-grpc-root\""), Instance, false);

        // Projection selects an evaluator, not a grant. Invalid nonempty targets retain the
        // native resolver's selection semantics rather than falling back to a different PDP.
        await Assert.That(route.Provider).IsEqualTo(HumanAuthorizationRouteProvider.CerbosTenant);
        await Assert.That(route.Endpoint).IsEqualTo("https://tenant-pdp.example.test/not-a-grpc-root");
        await Assert.That(GrpcEndpointNormalizer.IsValid(route.Endpoint)).IsFalse();
    }

    private static async Task<HumanCommitSubject> ResolveSubject(bool local = false)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", local ? UserId.ToString("D") : "OriginalSubject"),
            new Claim("iss", local ? LocalIdentityOptions.Issuer : "https://accounts.google.com"),
            new Claim("auth_provider", local ? "local" : "google")
        ], "interactive"));
        return await HumanCommitSubject.ResolveAsync(principal, new IdentityQuery(), CancellationToken.None)
            ?? throw new InvalidOperationException("The controlled provider binding did not resolve.");
    }

    private sealed class IdentityQuery : IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>
    {
        public Task<Guid?> QueryAsync(
            ResolveCurrentUserIdByIdentityRequest query, CancellationToken cancellationToken = default) =>
            Task.FromResult<Guid?>(query.Email is null && !query.EmailVerified
                && (query.Provider == "google" && query.ProviderId == GoogleAccount
                    || query.Provider == "local" && query.ProviderId == UserId.ToString("D"))
                    ? UserId : null);
    }

    private static HumanPrincipalSourceFacts SourceFacts() => new(
        UserId, TenantId, [new(UserId, false)],
        [new(BindingId, UserId, (int)AuthenticationProviderKind.Google, GoogleAccount)],
        [], [], [], [], [], [], [], [], false, [], [], [], [], null);

    private static HumanPrincipalSourceFacts LocalSourceFacts(HumanPrincipalLocalSessionFacts? local) =>
        SourceFacts() with
        {
            Bindings = [new(BindingId, UserId, (int)AuthenticationProviderKind.Local, UserId.ToString("D"))],
            LocalSession = local
        };

    private static LocalSessionAuthority Session(bool verified = true) =>
        new(UserId, Guid.NewGuid().ToString("N"), verified);

    private static LocalCredentialStateMetadata State(
        LocalCredentialState state = LocalCredentialState.Ready, Guid? user = null) =>
        new(LocalCredentialStateMetadata.CurrentVersion, state, OperationId, user ?? UserId);

    private static HumanPrincipalLocalSessionFacts ReadyLocalFacts() => new(
        [new(UserId, true, true, true)], State(),
        [new(OperationId, UserId, (int)LocalCredentialOperationKind.Create,
            (int)LocalCredentialOperationStage.Replaced, ActorId, BindingId)],
        [new(ActorId, UserId, (int)ActorTypeEnum.User, false, false)], true);

    private static HumanAuthorizationRouteFacts RouteFacts() => new(
        UserId, TenantId, HumanAuthorizationRouteFacts.Keys
            .Select(key => new HumanAuthorizationRouteSettingFact(key, null, null)).ToImmutableArray());

    private static HumanAuthorizationRouteFacts ByoFacts()
    {
        HumanAuthorizationRouteFacts facts = Set(RouteFacts(), GovernanceSettingKeys.Cerbos.TenantCustomizationEnabled,
            "true");
        facts = Set(facts, GovernanceSettingKeys.Cerbos.Mode, tenant: "\"custom_endpoint\"");
        return Set(facts, GovernanceSettingKeys.Cerbos.CustomEndpoint,
            tenant: "\"tenant-pdp.example.test:3593\"");
    }

    private static HumanAuthorizationRouteFacts Set(
        HumanAuthorizationRouteFacts facts, string key, string? system = null, string? tenant = null,
        bool locked = false) =>
        facts with
        {
            Settings = facts.Settings.Select(row => row.Key != key ? row : new HumanAuthorizationRouteSettingFact(
                key,
                system is null ? null : new SystemSettingFacts(system, SettingValueType.String, locked, null, null, null),
                tenant is null ? null : new TenantSettingFacts(tenant, false))).ToImmutableArray()
        };
}
