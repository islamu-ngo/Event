using Explore.Application.Contracts.Identity;
using Explore.Application.DTOs.Management;
using Explore.Application.Features.Management;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Interfaces;
using Explore.Domain.Modules;
using Explore.Domain.Settings.Documents;

namespace Event.Application.UnitTests.Features.Management;

public sealed class ManagedTenantWorkerGraphPlanTests
{
    [Test]
    public async Task LocalCreationLinksTheExactExistingIdentityWithoutCreatingGlobalIdentityRows()
    {
        var (snapshot, bootstrap) = Sources();
        var administrator = new ManagedTenantWorkerAdministratorSource(Subject, Actor, Login, false);
        var graph = ManagedTenantWorkerGraphPlan.Build(snapshot, bootstrap, administrator, AdminRole(), Now);

        await Assert.That(graph.CreatedUser).IsNull();
        await Assert.That(graph.CreatedActor).IsNull();
        await Assert.That(graph.CreatedLogin).IsNull();
        await Assert.That(graph.Result.UserId).IsEqualTo(Subject);
        await Assert.That(graph.Result.UserActorId).IsEqualTo(Actor);
        await Assert.That(graph.Result.UserExternalLoginId).IsEqualTo(Login);
        await Assert.That(graph.Effects.OfType<ITenantEntity>().All(row => row.TenantId == graph.Creation.Tenant.Id)).IsTrue();
        await Assert.That(graph.Effects.OfType<TenantUser>().Single().UserId).IsEqualTo(Subject);
        await Assert.That(graph.Effects.OfType<TenantUserRoleGrant>().Single().TenantUserId)
            .IsEqualTo(graph.Result.TenantUserId);
        await Assert.That(graph.Effects.OfType<TenantSettingsDocument>().Count()).IsEqualTo(2);
    }

    [Test]
    public async Task ALocalAdministratorCannotBeSubstitutedForAnotherExistingAccount()
    {
        var (snapshot, bootstrap) = Sources();

        await Assert.That(() => ManagedTenantWorkerGraphPlan.Build(snapshot, bootstrap,
                new(Guid.CreateVersion7(), Actor, Login, false), AdminRole(), Now))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ALocalAdministratorRequiresTheExistingActorAndLoginTogether()
    {
        var (snapshot, bootstrap) = Sources();

        await Assert.That(() => ManagedTenantWorkerGraphPlan.Build(snapshot, bootstrap,
                new(Subject, null, Login, false), AdminRole(), Now))
            .Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments("other.role", RoleScopeEnum.Tenant)]
    [Arguments("tenant.admin", RoleScopeEnum.Platform)]
    public async Task RoleMetadataCannotRelabelAnUnrelatedRoleAsTenantAdministrator(string code, RoleScopeEnum scope)
    {
        var (snapshot, bootstrap) = Sources();
        var role = new Role { Id = 9, MasterCode = code, FullName = "Role", Scope = scope };

        await Assert.That(() => ManagedTenantWorkerGraphPlan.Build(snapshot, bootstrap,
                new(Subject, Actor, Login, false), role, Now))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task AResolvedPlanCannotReplaceTheOriginalRequestedVersion()
    {
        var (snapshot, bootstrap) = Sources();
        bootstrap.PlanVersion.Id = Guid.CreateVersion7();

        await Assert.That(() => ManagedTenantWorkerGraphPlan.Build(snapshot, bootstrap,
                new(Subject, Actor, Login, false), AdminRole(), Now))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task AnUnrequestedModuleCannotBeAddedToTheNewTenant()
    {
        var (snapshot, bootstrap) = Sources();
        var additional = new ModuleDefinition { Id = Guid.CreateVersion7(), ModuleKey = "other", Name = "Other", IsActive = true };
        var changed = bootstrap with { Modules = bootstrap.Modules.Append(additional).ToArray() };

        await Assert.That(() => ManagedTenantWorkerGraphPlan.Build(snapshot, changed,
                new(Subject, Actor, Login, false), AdminRole(), Now))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task AnAbsentExternalIdentityCreatesOneCoherentAccountActorAndCanonicalLogin()
    {
        var (snapshot, bootstrap) = Sources(external: true);
        var graph = ManagedTenantWorkerGraphPlan.Build(snapshot, bootstrap,
            new(null, null, null, null), AdminRole(), Now);
        var original = snapshot.ReadRequest().Administrator.ExternalIdentity!;

        await Assert.That(graph.CreatedUser).IsNotNull();
        await Assert.That(graph.CreatedActor).IsNotNull();
        await Assert.That(graph.CreatedLogin).IsNotNull();
        await Assert.That(graph.CreatedUser!.Id.Version).IsEqualTo(7);
        await Assert.That(graph.CreatedActor!.UserId).IsEqualTo(graph.CreatedUser.Id);
        await Assert.That(graph.CreatedActor.ActorTypeId).IsEqualTo((int)ActorTypeEnum.User);
        await Assert.That(graph.CreatedActor.Pii.ActorId).IsEqualTo(graph.CreatedActor.Id);
        await Assert.That(graph.CreatedActor.Pii.DisplayName).IsEqualTo(original.DisplayName);
        await Assert.That(graph.CreatedUser.Pii.UserId).IsEqualTo(graph.CreatedUser.Id);
        await Assert.That(graph.CreatedUser.Pii.Email).IsEqualTo(original.Email);
        await Assert.That(graph.CreatedLogin!.UserId).IsEqualTo(graph.CreatedUser.Id);
        await Assert.That(graph.CreatedLogin.ProviderKey).IsEqualTo(ManagedTenantWorkerGraphPlan.Account(snapshot).Value);
        await Assert.That(graph.CreatedLogin.AuthenticationProviderId)
            .IsEqualTo((int)ManagedTenantWorkerGraphPlan.Account(snapshot).ProviderKind);
        await Assert.That(graph.Result.UserId).IsEqualTo(graph.CreatedUser.Id);
        await Assert.That(graph.Result.UserActorId).IsEqualTo(graph.CreatedActor.Id);
        await Assert.That(graph.Result.UserExternalLoginId).IsEqualTo(graph.CreatedLogin.Id);
        await Assert.That(graph.Effects.OfType<TenantUser>().Single().ActorId).IsEqualTo(graph.CreatedActor.Id);
        await Assert.That(graph.UpgradeExistingEmailVerified).IsFalse();
    }

    [Test]
    [Arguments(false, false, false)]
    [Arguments(false, true, true)]
    [Arguments(true, false, false)]
    [Arguments(true, true, false)]
    public async Task ExistingExternalIdentityIsReusedAndVerificationCanOnlyBeUpgraded(
        bool currentVerified, bool requestedVerified, bool expectUpgrade)
    {
        var (snapshot, bootstrap) = Sources(external: true, emailVerified: requestedVerified);
        var graph = ManagedTenantWorkerGraphPlan.Build(snapshot, bootstrap,
            new(Subject, Actor, Login, currentVerified), AdminRole(), Now);

        await Assert.That(graph.CreatedUser).IsNull();
        await Assert.That(graph.CreatedActor).IsNull();
        await Assert.That(graph.CreatedLogin).IsNull();
        await Assert.That(graph.Result.UserId).IsEqualTo(Subject);
        await Assert.That(graph.Result.UserActorId).IsEqualTo(Actor);
        await Assert.That(graph.Result.UserExternalLoginId).IsEqualTo(Login);
        await Assert.That(graph.UpgradeExistingEmailVerified).IsEqualTo(expectUpgrade);
        await Assert.That(graph.Effects.OfType<User>().Count()).IsEqualTo(0);
        await Assert.That(graph.Effects.OfType<Actor>().Count()).IsEqualTo(0);
        await Assert.That(graph.Effects.OfType<UserExternalLogin>().Count()).IsEqualTo(0);
    }

    [Test]
    public async Task ExistingExternalUserWithoutAnActorCreatesOnlyItsMissingActor()
    {
        var (snapshot, bootstrap) = Sources(external: true);
        var graph = ManagedTenantWorkerGraphPlan.Build(snapshot, bootstrap,
            new(Subject, null, Login, true), AdminRole(), Now);

        await Assert.That(graph.CreatedUser).IsNull();
        await Assert.That(graph.CreatedLogin).IsNull();
        await Assert.That(graph.CreatedActor).IsNotNull();
        await Assert.That(graph.CreatedActor!.UserId).IsEqualTo(Subject);
        await Assert.That(graph.Result.UserActorId).IsEqualTo(graph.CreatedActor.Id);
        await Assert.That(graph.Effects.OfType<TenantUser>().Single().ActorId).IsEqualTo(graph.CreatedActor.Id);
    }

    [Test]
    public async Task ExistingExternalUserWithoutTheMatchingLoginCannotBeBorrowed()
    {
        var (snapshot, bootstrap) = Sources(external: true);
        await Assert.That(() => ManagedTenantWorkerGraphPlan.Build(snapshot, bootstrap,
                new(Subject, Actor, null, true), AdminRole(), Now))
            .Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments("https://accounts.google.com/custom", "external-subject", AuthenticationProviderKind.Keycloak, "keycloak")]
    [Arguments("http://accounts.google.com", "external-subject", AuthenticationProviderKind.Keycloak, "keycloak")]
    [Arguments("https://accounts.google.com", "external-subject", AuthenticationProviderKind.Google, "google")]
    [Arguments("https://issuer.example.test", "external-subject", AuthenticationProviderKind.Keycloak, "keycloak")]
    [Arguments("atproto", "did:plc:abcdefghijklmnopqrstuvwx", AuthenticationProviderKind.Atproto, "atproto")]
    public async Task AdministratorBindingsUseTheSameCanonicalProviderAsTheCreatedLogin(
        string authority, string subject, AuthenticationProviderKind provider, string system)
    {
        var (snapshot, bootstrap) = Sources(external: true, authority: authority, externalSubject: subject);
        var graph = ManagedTenantWorkerGraphPlan.Build(snapshot, bootstrap,
            new(null, null, null, null), AdminRole(), Now);
        var account = ManagedTenantWorkerGraphPlan.Account(snapshot);
        var administratorBindings = graph.Effects.OfType<ExternalBinding>()
            .Where(row => row.ExternalSystem != "control-plane").ToArray();

        await Assert.That(account.ProviderKind).IsEqualTo(provider);
        await Assert.That(graph.CreatedLogin!.AuthenticationProviderId).IsEqualTo((int)provider);
        await Assert.That(graph.CreatedLogin.ProviderKey).IsEqualTo(account.Value);
        await Assert.That(administratorBindings.Length).IsEqualTo(5);
        await Assert.That(administratorBindings.All(row => row.ExternalSystem == system)).IsTrue();
        await Assert.That(administratorBindings.All(row => row.ExternalId == subject)).IsTrue();
    }

    private static (ManagedTenantWorkerCreationSnapshot Snapshot, ManagedTenantProvisioningResolvedBootstrap Bootstrap) Sources(
        bool external = false, bool emailVerified = true,
        string authority = "https://issuer.example.test", string externalSubject = "external-subject")
    {
        var request = ManagedTenantProvisioningRequestCodec.Normalize(
            ManagedTenantProvisioningRequestCodec.Deserialize(RequestJson));
        if (external)
            request = request with
            {
                Administrator = new ManagementTenantAdministratorDto
                {
                    ExternalIdentity = new ManagementTenantExternalIdentityDto
                    {
                        IdentityProvider = authority,
                        Subject = externalSubject,
                        Email = "external@example.test",
                        FirstName = "External",
                        LastName = "Administrator",
                        DisplayName = "External Administrator",
                        EmailVerified = emailVerified
                    }
                }
            };
        var operation = new ManagedTenantProvisioningOperation
        {
            Id = Guid.CreateVersion7(),
            ManagedInstanceId = Guid.CreateVersion7(),
            CurrentOutboxMessageId = Guid.CreateVersion7(),
            Status = ManagedTenantProvisioningStatus.Processing,
            ExternalRequestId = request.ExternalRequestId,
            ExternalCustomerReference = request.ExternalCustomerReference,
            TenantSlug = request.TenantSlug,
            RequestJson = ManagedTenantProvisioningRequestCodec.Serialize(request),
            RequestHash = ManagedTenantProvisioningRequestCodec.ComputeHash(request),
            CreatedAt = Now
        };
        var plan = new TenantPlan { Id = Guid.CreateVersion7(), Key = "managed", DisplayName = "Managed" };
        var version = new TenantPlanVersion
        {
            Id = request.Plan.VersionId,
            TenantPlanId = plan.Id,
            TenantPlan = plan,
            CurrencyCode = "EUR",
            BillingPeriod = "monthly"
        };
        var module = new ModuleDefinition { Id = Guid.CreateVersion7(), ModuleKey = "events", Name = "Events", IsActive = true };
        return (ManagedTenantWorkerCreationSnapshot.Capture(operation, operation.CurrentOutboxMessageId),
            new(plan, version, [module], [], new() { DisplayName = request.TenantName }));
    }

    private static Role AdminRole() => new() { Id = 9, MasterCode = "tenant.admin", FullName = "Administrator", Scope = RoleScopeEnum.Tenant };
    private static readonly Guid Subject = Guid.Parse("0199ec4b-0000-7000-8000-000000000001");
    private static readonly Guid Actor = Guid.Parse("0199ec4b-0000-7000-8000-000000000003");
    private static readonly Guid Login = Guid.Parse("0199ec4b-0000-7000-8000-000000000004");
    private static readonly DateTime Now = new(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);
    private const string RequestJson = """
        {"externalRequestId":"request-1","externalCustomerReference":"customer-1","tenantName":"Original Tenant","tenantSlug":"original-tenant",
         "administrator":{"localIdentity":{"localSubjectId":"0199ec4b-0000-7000-8000-000000000001"}},
         "plan":{"key":"managed","versionId":"0199ec4b-0000-7000-8000-000000000002"},"approvedModules":["events"],
         "directoryOperatorIdentity":{"publicName":"Operator","legalName":"Operator ASBL","operatorKindCode":"registered_organization","jurisdictionCountryCode":"BE","registrationIdentifier":"BE 0123.456.789","publicContactEmail":"contact@example.test","legalNoticeUrl":"https://example.test/legal","termsUrl":"https://example.test/terms","privacyUrl":"https://example.test/privacy"}}
        """;
}
