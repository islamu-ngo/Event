using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Tenants;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Documents;

namespace Event.Application.UnitTests.Tenants;

/// <summary>Data-origin invariants only; native tests prove issuer and physical transaction custody.</summary>
public sealed class HumanTenantCreationPrincipalProjectionTests
{
    [Test]
    public async Task CompletedNewGraphDoesNotBecomeAuthorityForItsOwnCreation()
    {
        var example = new Example();
        var normalized = HumanTenantCreationPrincipalProjection.ExcludingCompletedEffect(
            example.Original, example.After(), example.Effect, example.Plan, example.Human);

        HumanTenantCreationPrincipalProjection.RequireUnchanged(example.Original, normalized);
        await Assert.That(normalized.TenantMemberships.Length).IsEqualTo(0);
        await Assert.That(normalized.TenantGrants.Length).IsEqualTo(0);
        await Assert.That(normalized.Permissions.SequenceEqual(example.Original.Permissions)).IsTrue();
    }

    [Test]
    public async Task CompletedGraphCannotHideOriginalRolePermissionRevocation()
    {
        var example = new Example();
        var current = example.After() with { Permissions = [Example.Permission(Example.TenantRole, 31)] };
        var normalized = HumanTenantCreationPrincipalProjection.ExcludingCompletedEffect(
            example.Original, current, example.Effect, example.Plan, example.Human);

        await Assert.That(() => HumanTenantCreationPrincipalProjection.RequireUnchanged(example.Original, normalized))
            .Throws<UnauthorizedAccessException>();
    }

    [Test]
    public async Task RoleAlreadyHeldBeforeCreationKeepsItsPermissionChangesVisible()
    {
        var example = new Example();
        Guid priorTenant = Guid.CreateVersion7();
        Guid priorMember = Guid.CreateVersion7();
        var original = example.Original with
        {
            Roles = [.. example.Original.Roles, Example.AdministratorRole],
            Permissions = [.. example.Original.Permissions, Example.Permission(Example.TenantRole, 31)],
            TenantMemberships = [new(priorMember, priorTenant, (int)TenantUserStatusEnum.Active, false)],
            TenantGrants = [new(Guid.CreateVersion7(), priorTenant, priorMember,
                Example.TenantRole, (int)RoleScopeEnum.Tenant, null)],
            Tenants = [new(priorTenant, (int)TenantStatusEnum.Active, true)]
        };
        var current = example.After(original) with
        {
            Permissions = [.. example.Original.Permissions, Example.Permission(Example.TenantRole, 32)]
        };
        var normalized = HumanTenantCreationPrincipalProjection.ExcludingCompletedEffect(
            original, current, example.Effect, example.Plan, example.Human);

        await Assert.That(normalized.Permissions.Any(row => row.RoleId == Example.TenantRole)).IsTrue();
        await Assert.That(normalized.TenantMemberships.SequenceEqual(original.TenantMemberships)).IsTrue();
        await Assert.That(normalized.TenantGrants.SequenceEqual(original.TenantGrants)).IsTrue();
        await Assert.That(() => HumanTenantCreationPrincipalProjection.RequireUnchanged(original, normalized))
            .Throws<UnauthorizedAccessException>();
    }

    [Test]
    public async Task ForeignGrantForTheSameNewRoleCannotBorrowCompletedGraphExclusion()
    {
        var example = new Example();
        var current = example.After();
        Guid foreignTenant = Guid.CreateVersion7();
        Guid foreignMember = Guid.CreateVersion7();
        current = current with
        {
            TenantMemberships = [.. current.TenantMemberships,
                new(foreignMember, foreignTenant, (int)TenantUserStatusEnum.Active, false)],
            TenantGrants = [.. current.TenantGrants,
                new(Guid.CreateVersion7(), foreignTenant, foreignMember, Example.TenantRole, (int)RoleScopeEnum.Tenant, null)]
        };
        var normalized = HumanTenantCreationPrincipalProjection.ExcludingCompletedEffect(
            example.Original, current, example.Effect, example.Plan, example.Human);

        await Assert.That(normalized.TenantMemberships.Single().Id).IsEqualTo(foreignMember);
        await Assert.That(() => HumanTenantCreationPrincipalProjection.RequireUnchanged(example.Original, normalized))
            .Throws<UnauthorizedAccessException>();
    }

    [Test]
    public async Task CandidateIdsWithoutCompletedEffectCannotHideAnySources()
    {
        var example = new Example();
        var normalized = HumanTenantCreationPrincipalProjection.ExcludingCompletedEffect(
            example.Original, example.After(), null, example.Plan, example.Human);

        await Assert.That(normalized.TenantMemberships.Length).IsEqualTo(1);
        await Assert.That(() => HumanTenantCreationPrincipalProjection.RequireUnchanged(example.Original, normalized))
            .Throws<UnauthorizedAccessException>();
    }

    [Test]
    public async Task CompletedIdsCannotHideAnAlteredMembershipOrRevokedGrant()
    {
        var example = new Example();
        var current = example.After() with
        {
            TenantMemberships = [new(example.Effect.MembershipId, example.Effect.TenantId,
                (int)TenantUserStatusEnum.Suspended, false)],
            TenantGrants = [new(example.Effect.GrantId, example.Effect.TenantId, example.Effect.MembershipId,
                Example.TenantRole, (int)RoleScopeEnum.Tenant, DateTime.UtcNow)]
        };

        await Assert.That(() => HumanTenantCreationPrincipalProjection.ExcludingCompletedEffect(
                example.Original, current, example.Effect, example.Plan, example.Human))
            .Throws<UnauthorizedAccessException>();
    }

    private sealed class Example
    {
        internal const int PlatformRole = 1;
        internal const int TenantRole = 2;
        internal static HumanPrincipalRoleFact AdministratorRole => new(TenantRole,
            (int)RoleScopeEnum.Tenant, "tenant.admin", false);
        internal Guid Human { get; } = Guid.CreateVersion7();
        internal HumanPrincipalSourceFacts Original { get; }
        internal HumanTenantCreationPlan Plan { get; }
        internal HumanTenantCreationAdministratorEffect Effect { get; }

        internal Example()
        {
            Guid tenant = Guid.CreateVersion7();
            var branding = TenantBrandingSettingsDocumentDefaults.Create(tenant, "Community");
            var identity = TenantDirectoryOperatorIdentityDocumentDefaults.Create(tenant, "Community");
            Plan = new(new TenantCreationRequest(tenant, "Community", "community", (int)TenantStatusEnum.Active,
                Human, DateTime.UtcNow,
                new(Guid.CreateVersion7(), branding.SchemaVersion, branding.DefaultsVersion, branding.PayloadJson),
                new(Guid.CreateVersion7(), identity.SchemaVersion, identity.DefaultsVersion, identity.PayloadJson)),
                Human, Guid.CreateVersion7(), Guid.CreateVersion7(), false, 0);
            Effect = new(tenant, Human, Plan.AdministratorMembershipId!.Value, Plan.AdministratorGrantId!.Value,
                TenantRole, (int)RoleScopeEnum.Tenant, "tenant.admin", Plan.Creation.TenantStatusId, true);
            Original = new(Human, null, [new(Human, false)], [], [new(Guid.CreateVersion7(), PlatformRole)],
                [new(PlatformRole, (int)RoleScopeEnum.Platform, "platform.admin", true)], [], [], [], [], [],
                [Permission(PlatformRole, 30)], true, [], [], [], [], null);
        }

        internal HumanPrincipalSourceFacts After(HumanPrincipalSourceFacts? original = null)
        {
            var before = original ?? Original;
            return before with
            {
                Roles = before.Roles.Any(row => row.Id == TenantRole) ? before.Roles : [.. before.Roles, AdministratorRole],
                TenantMemberships = [.. before.TenantMemberships,
                    new(Effect.MembershipId, Effect.TenantId, (int)TenantUserStatusEnum.Active, false)],
                TenantGrants = [.. before.TenantGrants,
                    new(Effect.GrantId, Effect.TenantId, Effect.MembershipId, TenantRole, (int)RoleScopeEnum.Tenant, null)],
                Tenants = [.. before.Tenants, new(Effect.TenantId, Plan.Creation.TenantStatusId, true)],
                Permissions = before.Permissions.Any(row => row.RoleId == TenantRole)
                    ? before.Permissions : [.. before.Permissions, Permission(TenantRole, 31)]
            };
        }

        internal static HumanPrincipalPermissionFact Permission(int role, int permission) =>
            new(role, permission, "event.create", true, true, false);
    }
}
