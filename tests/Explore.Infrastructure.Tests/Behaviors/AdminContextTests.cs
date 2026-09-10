using System.Security.Claims;
using Explore.Application.Authentication;
using Explore.Application.Constants;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Explore.Infrastructure.Tests.Behaviors;

public class AdminContextTests
{
    private const string TestOidcIssuer = "https://identity.example.test/realms/event";

    private static ProviderAccountKey OidcAccountKey(string subject) =>
        PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(TestOidcIssuer, subject);

    [Test]
    public async Task IsInstanceAdminAsync_WhenNoPlatformUserRole_ReturnsFalse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var platformUserRoleRepository = Substitute.For<IPlatformUserRoleRepository>();
        platformUserRoleRepository.IsUserPlatformAdmin(userId).Returns(false);

        var tenantUserRoleGrantRepository = Substitute.For<ITenantUserRoleGrantRepository>();
        var organizationMemberRepository = Substitute.For<IOrganizationMemberRepository>();
        var sut = CreateSut(CreateHttpContextAccessor(userId), platformUserRoleRepository, tenantUserRoleGrantRepository, organizationMemberRepository);

        // Act
        var result = await sut.IsInstanceAdminAsync(userId, CancellationToken.None);

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsInstanceAdminAsync_WhenPlatformUserRoleLookupThrows_ReturnsFalse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var platformUserRoleRepository = Substitute.For<IPlatformUserRoleRepository>();
        platformUserRoleRepository.IsUserPlatformAdmin(userId)
            .ThrowsAsync(new InvalidOperationException("relation \"PlatformUserRoles\" does not exist"));

        var tenantUserRoleGrantRepository = Substitute.For<ITenantUserRoleGrantRepository>();
        var organizationMemberRepository = Substitute.For<IOrganizationMemberRepository>();
        var sut = CreateSut(CreateHttpContextAccessor(userId), platformUserRoleRepository, tenantUserRoleGrantRepository, organizationMemberRepository);

        // Act
        var result = await sut.IsInstanceAdminAsync(userId, CancellationToken.None);

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsInstanceAdminAsync_WhenUserIsDefaultTenantAdminWithoutPlatformRole_ReturnsFalse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var platformUserRoleRepository = Substitute.For<IPlatformUserRoleRepository>();
        platformUserRoleRepository.IsUserPlatformAdmin(userId).Returns(false);

        var tenantUserRoleGrantRepository = Substitute.For<ITenantUserRoleGrantRepository>();
        tenantUserRoleGrantRepository.IsTenantAdmin(PlatformDefaults.DefaultTenantId, userId).Returns(true);
        var organizationMemberRepository = Substitute.For<IOrganizationMemberRepository>();
        var sut = CreateSut(CreateHttpContextAccessor(userId), platformUserRoleRepository, tenantUserRoleGrantRepository, organizationMemberRepository);

        // Act
        var result = await sut.IsInstanceAdminAsync(userId, CancellationToken.None);

        // Assert
        await Assert.That(result).IsFalse();
        await tenantUserRoleGrantRepository.DidNotReceive().IsTenantAdmin(PlatformDefaults.DefaultTenantId, userId);
    }

    [Test]
    public async Task IsInstanceAdminAsync_WhenNoPlatformRoleExists_ReturnsFalse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var platformUserRoleRepository = Substitute.For<IPlatformUserRoleRepository>();
        platformUserRoleRepository.IsUserPlatformAdmin(userId).Returns(false);

        var tenantUserRoleGrantRepository = Substitute.For<ITenantUserRoleGrantRepository>();
        var organizationMemberRepository = Substitute.For<IOrganizationMemberRepository>();
        var sut = CreateSut(CreateHttpContextAccessor(userId), platformUserRoleRepository, tenantUserRoleGrantRepository, organizationMemberRepository);

        // Act
        var result = await sut.IsInstanceAdminAsync(userId, CancellationToken.None);

        // Assert
        await Assert.That(result).IsFalse();
        await tenantUserRoleGrantRepository.DidNotReceive().IsTenantAdmin(Arg.Any<Guid>(), Arg.Any<Guid>());
    }

    [Test]
    public async Task IsInstanceAdminAsync_WhenDefaultTenantAdminIsNotPlatformAdmin_ReturnsFalse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var platformUserRoleRepository = Substitute.For<IPlatformUserRoleRepository>();
        platformUserRoleRepository.IsUserPlatformAdmin(userId).Returns(false);

        var tenantUserRoleGrantRepository = Substitute.For<ITenantUserRoleGrantRepository>();
        tenantUserRoleGrantRepository.IsTenantAdmin(PlatformDefaults.DefaultTenantId, userId).Returns(true);
        var organizationMemberRepository = Substitute.For<IOrganizationMemberRepository>();
        var sut = CreateSut(CreateHttpContextAccessor(userId), platformUserRoleRepository, tenantUserRoleGrantRepository, organizationMemberRepository);

        // Act
        var result = await sut.IsInstanceAdminAsync(userId, CancellationToken.None);

        // Assert
        await Assert.That(result).IsFalse();
        await tenantUserRoleGrantRepository.DidNotReceive().IsTenantAdmin(PlatformDefaults.DefaultTenantId, userId);
    }

    [Test]
    public async Task IsTenantAdminAsync_UsesTenantAdminRoleCheck()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var platformUserRoleRepository = Substitute.For<IPlatformUserRoleRepository>();
        var tenantUserRoleGrantRepository = Substitute.For<ITenantUserRoleGrantRepository>();
        tenantUserRoleGrantRepository.IsTenantAdmin(tenantId, userId).Returns(true);
        var organizationMemberRepository = Substitute.For<IOrganizationMemberRepository>();

        var sut = CreateSut(CreateHttpContextAccessor(userId), platformUserRoleRepository, tenantUserRoleGrantRepository, organizationMemberRepository);

        // Act
        var result = await sut.IsTenantAdminAsync(tenantId, CancellationToken.None);

        // Assert
        await Assert.That(result).IsTrue();
        await tenantUserRoleGrantRepository.Received(1).IsTenantAdmin(tenantId, userId);
        await tenantUserRoleGrantRepository.DidNotReceive().HasActiveTenantUserRoleGrant(Arg.Any<Guid>(), Arg.Any<Guid>());
    }

    [Test]
    public async Task IsTenantAdminAsync_WhenDefaultTenantInstanceAdminWithoutTenantGrant_ReturnsFalse()
    {
        var userId = Guid.NewGuid();
        var platformUserRoleRepository = Substitute.For<IPlatformUserRoleRepository>();
        platformUserRoleRepository.IsUserPlatformAdmin(userId).Returns(true);

        var tenantUserRoleGrantRepository = Substitute.For<ITenantUserRoleGrantRepository>();
        tenantUserRoleGrantRepository.IsTenantAdmin(PlatformDefaults.DefaultTenantId, userId).Returns(false);

        var sut = CreateSut(
            CreateHttpContextAccessor(userId),
            platformUserRoleRepository,
            tenantUserRoleGrantRepository,
            Substitute.For<IOrganizationMemberRepository>());

        var result = await sut.IsTenantAdminAsync(PlatformDefaults.DefaultTenantId, CancellationToken.None);

        await Assert.That(result).IsFalse();
        await tenantUserRoleGrantRepository.Received(1).IsTenantAdmin(PlatformDefaults.DefaultTenantId, userId);
    }

    [Test]
    public async Task ResolveUserIdAsync_WithGoogleIdpAlias_UsesNormalizedExternalLoginProvider()
    {
        var userId = Guid.NewGuid();
        const string subject = "sXzmb2sFh0rG8tVveiNrP3td";

        var externalLoginRepository = Substitute.For<IUserExternalLoginRepository>();
        externalLoginRepository.GetByProviderAndKey(OidcAccountKey(subject))
            .Returns(NewExternalLogin(userId, AuthSchemeNames.Google.ToLowerInvariant(), OidcAccountKey(subject).Value));

        var sut = CreateSut(
            CreateHttpContextAccessor(CreateExternalPrincipal(subject, idp: "google-oauth2")),
            Substitute.For<IPlatformUserRoleRepository>(),
            Substitute.For<ITenantUserRoleGrantRepository>(),
            Substitute.For<IOrganizationMemberRepository>(),
            userExternalLoginRepository: externalLoginRepository);

        var result = await sut.ResolveUserIdAsync(CancellationToken.None);

        await Assert.That(result).IsEqualTo(userId);
        await externalLoginRepository.Received(1).GetByProviderAndKey(OidcAccountKey(subject));
    }

    [Test]
    public async Task IsInstanceAdminAsync_GuidProviderSubjectWithoutExactIssuerLink_CannotInheritInternalAdminAuthority()
    {
        Guid internalAdminId = Guid.CreateVersion7();
        var platformRoles = Substitute.For<IPlatformUserRoleRepository>();
        platformRoles.IsUserPlatformAdmin(internalAdminId).Returns(true);
        var externalLogins = Substitute.For<IUserExternalLoginRepository>();
        externalLogins.GetByProviderAndKey(PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
            "https://attacker.example.test/realms/event",
            internalAdminId.ToString("D")))
            .Returns((UserExternalLogin?)null);
        var principal = CreateExternalPrincipal(
            internalAdminId.ToString("D"),
            idp: "keycloak",
            issuer: "https://attacker.example.test/realms/event");
        var sut = CreateSut(
            CreateHttpContextAccessor(principal),
            platformRoles,
            Substitute.For<ITenantUserRoleGrantRepository>(),
            Substitute.For<IOrganizationMemberRepository>(),
            userExternalLoginRepository: externalLogins);

        bool result = await sut.IsInstanceAdminAsync(CancellationToken.None);

        await Assert.That(result).IsFalse()
            .Because("an OIDC GUID subject is provider-owned and must resolve through its exact issuer-qualified external login");
        await externalLogins.Received(1).GetByProviderAndKey(PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
            "https://attacker.example.test/realms/event",
            internalAdminId.ToString("D")));
    }

    [Test]
    public async Task ResolveUserIdAsync_ConflictingGuidClaimsUsesCanonicalPriorityWithoutProviderLookup()
    {
        var subUserId = Guid.NewGuid();
        var internalUserId = Guid.NewGuid();
        var externalLoginRepository = Substitute.For<IUserExternalLoginRepository>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", subUserId.ToString("D")),
            new Claim("internal_user_id", internalUserId.ToString("D"))
        ], "Bearer"));
        var sut = CreateSut(
            CreateHttpContextAccessor(principal),
            Substitute.For<IPlatformUserRoleRepository>(),
            Substitute.For<ITenantUserRoleGrantRepository>(),
            Substitute.For<IOrganizationMemberRepository>(),
            userExternalLoginRepository: externalLoginRepository);

        var result = await sut.ResolveUserIdAsync(CancellationToken.None);

        await Assert.That(result).IsEqualTo(subUserId);
        await externalLoginRepository.DidNotReceive().GetByProviderAndKey(Arg.Any<ProviderAccountKey>());
    }

    [Test]
    public async Task ResolveUserIdAsync_PurposeBoundPrincipalFailsClosedWithoutProviderLookup()
    {
        var externalLoginRepository = Substitute.For<IUserExternalLoginRepository>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "provider-subject")],
            ApiAuthenticationSchemeNames.ApiKey));
        var sut = CreateSut(
            CreateHttpContextAccessor(principal),
            Substitute.For<IPlatformUserRoleRepository>(),
            Substitute.For<ITenantUserRoleGrantRepository>(),
            Substitute.For<IOrganizationMemberRepository>(),
            userExternalLoginRepository: externalLoginRepository);

        var result = await sut.ResolveUserIdAsync(CancellationToken.None);

        await Assert.That(result).IsNull();
        await externalLoginRepository.DidNotReceive().GetByProviderAndKey(Arg.Any<ProviderAccountKey>());
    }

    [Test]
    public async Task ResolveUserIdAsync_MixedAuthenticatedIdentitiesFailClosedWithoutProviderLookup()
    {
        var externalLoginRepository = Substitute.For<IUserExternalLoginRepository>();
        var principal = new ClaimsPrincipal(
        [
            new ClaimsIdentity([new Claim("sub", "provider-subject")], "Bearer"),
            new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString("D"))], ApiAuthenticationSchemeNames.ApiKey)
        ]);
        var sut = CreateSut(
            CreateHttpContextAccessor(principal),
            Substitute.For<IPlatformUserRoleRepository>(),
            Substitute.For<ITenantUserRoleGrantRepository>(),
            Substitute.For<IOrganizationMemberRepository>(),
            userExternalLoginRepository: externalLoginRepository);

        var result = await sut.ResolveUserIdAsync(CancellationToken.None);

        await Assert.That(result).IsNull();
        await externalLoginRepository.DidNotReceive().GetByProviderAndKey(Arg.Any<ProviderAccountKey>());
    }

    [Test]
    public async Task ResolveUserIdAsync_WithAmbientAtprotoDidClaim_DoesNotUseDidProviderKey()
    {
        const string subject = "handle.example.com";
        const string did = "did:plc:example";
        var externalLoginRepository = Substitute.For<IUserExternalLoginRepository>();
        externalLoginRepository.GetByProviderAndKey(OidcAccountKey(subject))
            .Returns((UserExternalLogin?)null);
        var sut = CreateSut(
            CreateHttpContextAccessor(CreateExternalPrincipal(subject, idp: "atproto", did: did)),
            Substitute.For<IPlatformUserRoleRepository>(),
            Substitute.For<ITenantUserRoleGrantRepository>(),
            Substitute.For<IOrganizationMemberRepository>(),
            userExternalLoginRepository: externalLoginRepository);

        var result = await sut.ResolveUserIdAsync(CancellationToken.None);

        await Assert.That(result).IsNull();
        await externalLoginRepository.Received(1).GetByProviderAndKey(OidcAccountKey(subject));
    }

    [Test]
    public async Task ResolveUserIdAsync_WithNoProviderHints_DefaultsToKeycloakExternalLogin()
    {
        var userId = Guid.NewGuid();
        const string subject = "keycloak-subject";

        var externalLoginRepository = Substitute.For<IUserExternalLoginRepository>();
        externalLoginRepository.GetByProviderAndKey(OidcAccountKey(subject))
            .Returns(NewExternalLogin(userId, AuthSchemeNames.Keycloak.ToLowerInvariant(), OidcAccountKey(subject).Value));

        var sut = CreateSut(
            CreateHttpContextAccessor(CreateExternalPrincipal(subject)),
            Substitute.For<IPlatformUserRoleRepository>(),
            Substitute.For<ITenantUserRoleGrantRepository>(),
            Substitute.For<IOrganizationMemberRepository>(),
            userExternalLoginRepository: externalLoginRepository);

        var result = await sut.ResolveUserIdAsync(CancellationToken.None);

        await Assert.That(result).IsEqualTo(userId);
        await externalLoginRepository.Received(1).GetByProviderAndKey(OidcAccountKey(subject));
    }

    [Test]
    public async Task ResolveUserIdAsync_WithOnlySidClaim_FailsClosedWithoutProviderLookup()
    {
        const string sid = "keycloak-session-id";
        var externalLoginRepository = Substitute.For<IUserExternalLoginRepository>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sid", sid)], "TestAuth"));
        var sut = CreateSut(
            CreateHttpContextAccessor(principal),
            Substitute.For<IPlatformUserRoleRepository>(),
            Substitute.For<ITenantUserRoleGrantRepository>(),
            Substitute.For<IOrganizationMemberRepository>(),
            userExternalLoginRepository: externalLoginRepository);

        var result = await sut.ResolveUserIdAsync(CancellationToken.None);

        await Assert.That(result).IsNull();
        await externalLoginRepository.DidNotReceive().GetByProviderAndKey(Arg.Any<ProviderAccountKey>());
    }

    [Test]
    public async Task ResolveUserIdAsync_WithVerifiedGoogleEmail_DoesNotUseEmailFallback()
    {
        const string subject = "google-subject";
        const string email = "USER@Example.COM";

        var externalLoginRepository = Substitute.For<IUserExternalLoginRepository>();
        externalLoginRepository.GetByProviderAndKey(OidcAccountKey(subject))
            .Returns((UserExternalLogin?)null);

        var sut = CreateSut(
            CreateHttpContextAccessor(CreateExternalPrincipal(
                subject,
                idp: "google",
                issuer: TestOidcIssuer,
                email: email,
                emailVerified: "true")),
            Substitute.For<IPlatformUserRoleRepository>(),
            Substitute.For<ITenantUserRoleGrantRepository>(),
            Substitute.For<IOrganizationMemberRepository>(),
            userExternalLoginRepository: externalLoginRepository);

        var result = await sut.ResolveUserIdAsync(CancellationToken.None);

        await Assert.That(result).IsNull();
        await externalLoginRepository.Received(1).GetByProviderAndKey(OidcAccountKey(subject));
    }

    [Test]
    public async Task ResolveUserIdAsync_WithUnverifiedGoogleEmail_DoesNotUseEmailFallback()
    {
        const string subject = "google-subject";

        var externalLoginRepository = Substitute.For<IUserExternalLoginRepository>();
        externalLoginRepository.GetByProviderAndKey(OidcAccountKey(subject))
            .Returns((UserExternalLogin?)null);

        var sut = CreateSut(
            CreateHttpContextAccessor(CreateExternalPrincipal(
                subject,
                idp: "google",
                email: "user@example.com",
                emailVerified: "false")),
            Substitute.For<IPlatformUserRoleRepository>(),
            Substitute.For<ITenantUserRoleGrantRepository>(),
            Substitute.For<IOrganizationMemberRepository>(),
            userExternalLoginRepository: externalLoginRepository);

        var result = await sut.ResolveUserIdAsync(CancellationToken.None);

        await Assert.That(result).IsNull();
        await externalLoginRepository.Received(1).GetByProviderAndKey(OidcAccountKey(subject));
    }

    [Test]
    public async Task ResolveUserIdAsync_AfterExternalLoginCreated_DoesNotReusePriorMissingResult()
    {
        var userId = Guid.NewGuid();
        const string subject = "keycloak-onboarding-subject";
        UserExternalLogin? externalLogin = null;
        var externalLoginRepository = Substitute.For<IUserExternalLoginRepository>();
        externalLoginRepository.GetByProviderAndKey(OidcAccountKey(subject))
            .Returns(_ => externalLogin);

        var sut = CreateSut(
            CreateHttpContextAccessor(CreateExternalPrincipal(subject)),
            Substitute.For<IPlatformUserRoleRepository>(),
            Substitute.For<ITenantUserRoleGrantRepository>(),
            Substitute.For<IOrganizationMemberRepository>(),
            userExternalLoginRepository: externalLoginRepository);

        var beforeOnboarding = await sut.ResolveUserIdAsync(CancellationToken.None);
        externalLogin = NewExternalLogin(userId, AuthSchemeNames.Keycloak.ToLowerInvariant(), OidcAccountKey(subject).Value);
        var afterOnboarding = await sut.ResolveUserIdAsync(CancellationToken.None);

        await Assert.That(beforeOnboarding).IsNull();
        await Assert.That(afterOnboarding).IsEqualTo(userId);
        await externalLoginRepository.Received(2).GetByProviderAndKey(OidcAccountKey(subject));
    }

    [Test]
    public async Task IsGroupAdminAsync_WhenMembershipHasGroupAdminRole_ReturnsTrue()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        var platformUserRoleRepository = Substitute.For<IPlatformUserRoleRepository>();
        var tenantUserRoleGrantRepository = Substitute.For<ITenantUserRoleGrantRepository>();
        var organizationMemberRepository = Substitute.For<IOrganizationMemberRepository>();
        var groupMemberRepository = Substitute.For<IGroupMemberRepository>();

        groupMemberRepository.GetByGroupAndUser(groupId, userId)
            .Returns(NewGroupMember(groupId, userId, RoleEnum.GroupAdmin, Guid.NewGuid()));

        var sut = CreateSutWithGroupMembers(
            CreateHttpContextAccessor(userId),
            platformUserRoleRepository,
            tenantUserRoleGrantRepository,
            organizationMemberRepository,
            groupMemberRepository);

        // Act
        var result = await sut.IsGroupAdminAsync(groupId, CancellationToken.None);

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsGroupAdminAsync_WhenMembershipHasNonAdminRole_ReturnsFalse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        var platformUserRoleRepository = Substitute.For<IPlatformUserRoleRepository>();
        var tenantUserRoleGrantRepository = Substitute.For<ITenantUserRoleGrantRepository>();
        var organizationMemberRepository = Substitute.For<IOrganizationMemberRepository>();
        var groupMemberRepository = Substitute.For<IGroupMemberRepository>();

        groupMemberRepository.GetByGroupAndUser(groupId, userId)
            .Returns(NewGroupMember(groupId, userId, RoleEnum.GroupMember, Guid.NewGuid()));

        var sut = CreateSutWithGroupMembers(
            CreateHttpContextAccessor(userId),
            platformUserRoleRepository,
            tenantUserRoleGrantRepository,
            organizationMemberRepository,
            groupMemberRepository);

        // Act
        var result = await sut.IsGroupAdminAsync(groupId, CancellationToken.None);

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsGroupAdminAsync_WhenNoMembership_ReturnsFalse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        var platformUserRoleRepository = Substitute.For<IPlatformUserRoleRepository>();
        var tenantUserRoleGrantRepository = Substitute.For<ITenantUserRoleGrantRepository>();
        var organizationMemberRepository = Substitute.For<IOrganizationMemberRepository>();
        var groupMemberRepository = Substitute.For<IGroupMemberRepository>();

        groupMemberRepository.GetByGroupAndUser(groupId, userId).Returns((GroupMember?)null);

        var sut = CreateSutWithGroupMembers(
            CreateHttpContextAccessor(userId),
            platformUserRoleRepository,
            tenantUserRoleGrantRepository,
            organizationMemberRepository,
            groupMemberRepository);

        // Act
        var result = await sut.IsGroupAdminAsync(groupId, CancellationToken.None);

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsGroupAdminAsync_WithNoAuthenticatedUser_ReturnsFalse()
    {
        // Arrange
        var groupId = Guid.NewGuid();

        var platformUserRoleRepository = Substitute.For<IPlatformUserRoleRepository>();
        var tenantUserRoleGrantRepository = Substitute.For<ITenantUserRoleGrantRepository>();
        var organizationMemberRepository = Substitute.For<IOrganizationMemberRepository>();
        var groupMemberRepository = Substitute.For<IGroupMemberRepository>();

        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns(new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) });

        var sut = CreateSutWithGroupMembers(
            httpContextAccessor,
            platformUserRoleRepository,
            tenantUserRoleGrantRepository,
            organizationMemberRepository,
            groupMemberRepository);

        // Act
        var result = await sut.IsGroupAdminAsync(groupId, CancellationToken.None);

        // Assert
        await Assert.That(result).IsFalse();
        await groupMemberRepository.DidNotReceive().GetByGroupAndUser(Arg.Any<Guid>(), Arg.Any<Guid>());
    }

    [Test]
    public async Task GetAdminGroupIdsAsync_ForUserId_FiltersNonAdminMemberships()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var adminGroupId = Guid.NewGuid();
        var memberGroupId = Guid.NewGuid();

        var tenantId = Guid.NewGuid();
        var memberships = new List<GroupMember>
        {
            NewGroupMember(adminGroupId, userId, RoleEnum.GroupAdmin, tenantId),
            NewGroupMember(memberGroupId, userId, RoleEnum.GroupMember, tenantId)
        };

        var platformUserRoleRepository = Substitute.For<IPlatformUserRoleRepository>();
        var tenantUserRoleGrantRepository = Substitute.For<ITenantUserRoleGrantRepository>();
        var organizationMemberRepository = Substitute.For<IOrganizationMemberRepository>();
        var groupMemberRepository = Substitute.For<IGroupMemberRepository>();
        groupMemberRepository.GetMembershipsByUser(userId).Returns(memberships);

        var sut = CreateSutWithGroupMembers(
            CreateHttpContextAccessor(userId),
            platformUserRoleRepository,
            tenantUserRoleGrantRepository,
            organizationMemberRepository,
            groupMemberRepository);

        // Act
        var result = await sut.GetAdminGroupIdsAsync(userId, CancellationToken.None);

        // Assert
        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result).Contains(adminGroupId);
        await Assert.That(result).DoesNotContain(memberGroupId);
    }

    [Test]
    public async Task TenantFilteredAdminScopeQueries_ExcludeOtherTenantMemberships()
    {
        var userId = Guid.CreateVersion7();
        var tenantId = Guid.CreateVersion7();
        var otherTenantId = Guid.CreateVersion7();
        var organizationId = Guid.CreateVersion7();
        var otherOrganizationId = Guid.CreateVersion7();
        var groupId = Guid.CreateVersion7();
        var otherGroupId = Guid.CreateVersion7();
        var organizationMemberRepository = Substitute.For<IOrganizationMemberRepository>();
        var groupMemberRepository = Substitute.For<IGroupMemberRepository>();
        organizationMemberRepository.GetMembershipsByUser(userId, Arg.Any<CancellationToken>()).Returns(
        [
            NewOrganizationMember(organizationId, userId, RoleEnum.OrgAdmin, tenantId),
            NewOrganizationMember(otherOrganizationId, userId, RoleEnum.OrgAdmin, otherTenantId)
        ]);
        groupMemberRepository.GetMembershipsByUser(userId, Arg.Any<CancellationToken>()).Returns(
        [
            NewGroupMember(groupId, userId, RoleEnum.GroupAdmin, tenantId),
            NewGroupMember(otherGroupId, userId, RoleEnum.GroupAdmin, otherTenantId)
        ]);
        var sut = CreateSutWithGroupMembers(
            CreateHttpContextAccessor(userId),
            Substitute.For<IPlatformUserRoleRepository>(),
            Substitute.For<ITenantUserRoleGrantRepository>(),
            organizationMemberRepository,
            groupMemberRepository);

        IReadOnlyList<Guid> organizationIds = await sut.GetAdminOrganizationIdsAsync(userId, tenantId);
        IReadOnlyList<Guid> groupIds = await sut.GetAdminGroupIdsAsync(userId, tenantId);

        await Assert.That(organizationIds).IsEquivalentTo([organizationId]);
        await Assert.That(groupIds).IsEquivalentTo([groupId]);
    }

    private static AdminContext CreateSutWithGroupMembers(
        IHttpContextAccessor httpContextAccessor,
        IPlatformUserRoleRepository platformUserRoleRepository,
        ITenantUserRoleGrantRepository tenantUserRoleGrantRepository,
        IOrganizationMemberRepository organizationMemberRepository,
        IGroupMemberRepository groupMemberRepository)
    {
        var userExternalLoginRepository = Substitute.For<IUserExternalLoginRepository>();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = Substitute.For<ILogger<AdminContext>>();

        return new AdminContext(
            httpContextAccessor,
            platformUserRoleRepository,
            tenantUserRoleGrantRepository,
            organizationMemberRepository,
            groupMemberRepository,
            userExternalLoginRepository,
            cache,
            logger);
    }

    [Test]
    public async Task GetAdminTenantIdsAsync_FiltersOutNonAdminMemberships()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var adminTenantId = Guid.NewGuid();
        var ownerTenantId = Guid.NewGuid();
        var memberTenantId = Guid.NewGuid();

        var memberships = new List<TenantUserRoleGrant>
        {
            NewGrant(userId, adminTenantId, RoleEnum.TenantAdmin),
            NewGrant(userId, ownerTenantId, RoleEnum.TenantAdmin),
            NewGrant(userId, memberTenantId, RoleEnum.TenantMember)
        };

        var platformUserRoleRepository = Substitute.For<IPlatformUserRoleRepository>();
        var tenantUserRoleGrantRepository = Substitute.For<ITenantUserRoleGrantRepository>();
        tenantUserRoleGrantRepository.GetByUserId(userId).Returns(memberships);
        var organizationMemberRepository = Substitute.For<IOrganizationMemberRepository>();

        var sut = CreateSut(CreateHttpContextAccessor(userId), platformUserRoleRepository, tenantUserRoleGrantRepository, organizationMemberRepository);

        // Act
        var result = await sut.GetAdminTenantIdsAsync(userId, CancellationToken.None);

        // Assert
        await Assert.That(result.Count).IsEqualTo(2);
        await Assert.That(result).Contains(adminTenantId);
        await Assert.That(result).Contains(ownerTenantId);
        await Assert.That(result).DoesNotContain(memberTenantId);
    }

    [Test]
    public async Task GetAdminTenantIdsAsync_WhenSingleTenantInstanceAdminWithoutTenantGrant_DoesNotAddDefaultTenant()
    {
        var userId = Guid.NewGuid();
        var platformUserRoleRepository = Substitute.For<IPlatformUserRoleRepository>();
        platformUserRoleRepository.IsUserPlatformAdmin(userId).Returns(true);

        var tenantUserRoleGrantRepository = Substitute.For<ITenantUserRoleGrantRepository>();
        tenantUserRoleGrantRepository.GetByUserId(userId).Returns([]);

        var sut = CreateSut(
            CreateHttpContextAccessor(userId),
            platformUserRoleRepository,
            tenantUserRoleGrantRepository,
            Substitute.For<IOrganizationMemberRepository>());

        var result = await sut.GetAdminTenantIdsAsync(userId, CancellationToken.None);

        await Assert.That(result).DoesNotContain(PlatformDefaults.DefaultTenantId);
        await tenantUserRoleGrantRepository.Received(1).GetByUserId(userId);
    }

    private static TenantUserRoleGrant NewGrant(Guid userId, Guid tenantId, RoleEnum role) => new()
    {
        Id = Guid.NewGuid(),
        TenantUserId = Guid.NewGuid(),
        TenantUser = new TenantUser
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Tenant = null!,
            UserId = userId,
            User = null!,
            StatusId = (int)TenantUserStatusEnum.Active
        },
        TenantId = tenantId,
        Tenant = null!,
        RoleId = (int)role,
        RoleScopeId = (int)RoleScopeEnum.Tenant,
        Role = null!
    };

    private static OrganizationMember NewOrganizationMember(
        Guid organizationId,
        Guid userId,
        RoleEnum role,
        Guid tenantId)
    {
        var organization = new Organization
        {
            Id = organizationId,
            Pii = new OrganizationPii { FullName = "Organization" }
        };
        var participation = new OrganizationTenant
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            Organization = organization,
            TenantId = tenantId,
            Tenant = null!,
            ApprovalStatus = null!
        };
        return new OrganizationMember
        {
            Id = Guid.CreateVersion7(),
            OrganizationTenantId = participation.Id,
            OrganizationTenant = participation,
            UserId = userId,
            User = null!,
            RoleId = (int)role,
            Role = null!,
            TenantId = tenantId,
            Tenant = null!
        };
    }

    private static GroupMember NewGroupMember(Guid groupId, Guid userId, RoleEnum role, Guid tenantId)
    {
        var group = new Group { Id = groupId, FullName = "Group" };
        var participation = new GroupTenant
        {
            Id = Guid.CreateVersion7(),
            GroupId = groupId,
            Group = group,
            TenantId = tenantId,
            Tenant = null!,
            ApprovalStatus = null!
        };
        return new GroupMember
        {
            Id = Guid.CreateVersion7(),
            GroupTenantId = participation.Id,
            GroupTenant = participation,
            UserId = userId,
            User = null!,
            RoleId = (int)role,
            Role = null!,
            TenantId = tenantId,
            Tenant = null!
        };
    }

    private static AdminContext CreateSut(
        IHttpContextAccessor httpContextAccessor,
        IPlatformUserRoleRepository platformUserRoleRepository,
        ITenantUserRoleGrantRepository tenantUserRoleGrantRepository,
        IOrganizationMemberRepository organizationMemberRepository,
        IUserExternalLoginRepository? userExternalLoginRepository = null)
    {
        var groupMemberRepository = Substitute.For<IGroupMemberRepository>();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = Substitute.For<ILogger<AdminContext>>();

        return new AdminContext(
            httpContextAccessor,
            platformUserRoleRepository,
            tenantUserRoleGrantRepository,
            organizationMemberRepository,
            groupMemberRepository,
            userExternalLoginRepository ?? Substitute.For<IUserExternalLoginRepository>(),
            cache,
            logger);
    }

    private static IHttpContextAccessor CreateHttpContextAccessor(ClaimsPrincipal principal)
    {
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext
        {
            User = principal
        };

        httpContextAccessor.HttpContext.Returns(httpContext);
        return httpContextAccessor;
    }

    private static IHttpContextAccessor CreateHttpContextAccessor(Guid userId)
    {
        return CreateHttpContextAccessor(CreatePrincipal(userId));
    }

    private static ClaimsPrincipal CreatePrincipal(Guid userId)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim("internal_user_id", userId.ToString()),
            new Claim("sub", userId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        ], "TestAuth");

        return new ClaimsPrincipal(identity);
    }

    private static ClaimsPrincipal CreateExternalPrincipal(
        string subject,
        string? idp = null,
        string? issuer = null,
        string? did = null,
        string? email = null,
        string? emailVerified = null)
    {
        issuer ??= TestOidcIssuer;
        var claims = new List<Claim> { new("sub", subject) };
        if (!string.IsNullOrWhiteSpace(idp))
            claims.Add(new Claim("idp", idp));

        if (!string.IsNullOrWhiteSpace(issuer))
            claims.Add(new Claim("iss", issuer));

        if (!string.IsNullOrWhiteSpace(did))
            claims.Add(new Claim("did", did));

        if (!string.IsNullOrWhiteSpace(email))
            claims.Add(new Claim("email", email));

        if (!string.IsNullOrWhiteSpace(emailVerified))
            claims.Add(new Claim("email_verified", emailVerified));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    private static UserExternalLogin NewExternalLogin(Guid userId, string provider, string providerKey)
    {
        return new UserExternalLogin
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            User = NewUser(userId, "user@example.com"),
            AuthenticationProviderId = (int)provider.ParseAuthenticationProviderKind(),
            AuthenticationProvider = null!,
            ProviderKey = providerKey,
            ProviderDisplayName = provider
        };
    }

    private static User NewUser(Guid userId, string email)
    {
        return new User
        {
            Id = userId,
            Pii = new UserPii
            {
                UserId = userId,
                Email = email,
                FirstName = "Test",
                LastName = "User"
            }
        };
    }
}
