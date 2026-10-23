// Verifies DB-first admin authority claims are correctly added to the ClaimsPrincipal.

using System.Security.Claims;
using Explore.Application.Authentication;
using Explore.Application.Constants;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Infrastructure.Identity;
using Explore.Infrastructure.Tests.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Explore.Infrastructure.Tests.Behaviors;

public class AdminClaimsTransformationTests
{
    private readonly IAdministrativeAuthorityRepository _adminContext;
    private readonly IExternalLoginAuthenticationRepository _userExternalLoginRepository;
    private readonly ILogger<AdminClaimsTransformation> _logger;
    private readonly AdminClaimsTransformation _sut;

    public AdminClaimsTransformationTests()
    {
        _adminContext = Substitute.For<IAdministrativeAuthorityRepository>();
        _userExternalLoginRepository = Substitute.For<IExternalLoginAuthenticationRepository>();
        _logger = Substitute.For<ILogger<AdminClaimsTransformation>>();
        _sut = new AdminClaimsTransformation(_adminContext, _userExternalLoginRepository, _logger);
    }

    [Test]
    public async Task TransformAsync_UnauthenticatedUser_ReturnsPrincipalUnchanged()
    {
        // Arrange
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        // Act
        var result = await _sut.TransformAsync(principal);

        // Assert
        await Assert.That(result).IsEqualTo(principal);
        await Assert.That(result.Identity!.IsAuthenticated).IsFalse();
    }

    [Test]
    public async Task TransformAsync_AuthenticatedUser_NonGuidSub_ReturnsPrincipalUnchanged()
    {
        // Arrange — "sub" claim is not a valid GUID, so userId extraction fails
        var identity = new ClaimsIdentity(new[] { new Claim("sub", "not-a-guid") }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = await _sut.TransformAsync(principal);

        // Assert — no admin context methods should be called
        await Assert.That(result.HasClaim(c => c.Type == AdminClaimTypes.InstanceAdmin)).IsFalse();
        await Assert.That(result.HasClaim(c => c.Type == AdminClaimTypes.TenantAdmin)).IsFalse();
        await Assert.That(result.HasClaim(c => c.Type == AdminClaimTypes.OrganizationAdmin)).IsFalse();
        await _adminContext.DidNotReceive().IsInstanceAdminAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TransformAsync_AuthenticatedUser_NoAdminAuthority_NoClaimsAdded()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var identity = new ClaimsIdentity(new[] { new Claim("sub", userId.ToString()) }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        _adminContext.IsInstanceAdminAsync(userId, Arg.Any<CancellationToken>()).Returns(false);
        _adminContext.GetAdminTenantIdsAsync(userId, Arg.Any<CancellationToken>()).Returns(Array.Empty<Guid>().ToList().AsReadOnly() as IReadOnlyList<Guid>);
        _adminContext.GetAdminOrganizationIdsAsync(userId, Arg.Any<CancellationToken>()).Returns(Array.Empty<Guid>().ToList().AsReadOnly() as IReadOnlyList<Guid>);

        // Act
        var result = await _sut.TransformAsync(principal);

        // Assert
        await Assert.That(result.HasClaim(c => c.Type == AdminClaimTypes.InstanceAdmin)).IsFalse();
        await Assert.That(result.HasClaim(c => c.Type == AdminClaimTypes.TenantAdmin)).IsFalse();
        await Assert.That(result.HasClaim(c => c.Type == AdminClaimTypes.OrganizationAdmin)).IsFalse();
    }

    [Test]
    public async Task TransformAsync_KeycloakGuidSubject_UsesExactProviderLink()
    {
        // Arrange
        var keycloakSubject = Guid.NewGuid();
        var localUserId = Guid.CreateVersion7();
        const string issuer = "http://localhost/realms/ISLAMU";
        const string email = "admin@test.islamu.org";
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("sub", keycloakSubject.ToString()),
            new Claim("iss", issuer),
            new Claim("email", email),
            new Claim("email_verified", "true")
        }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        ProviderAccountKey accountKey =
            PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(issuer, keycloakSubject.ToString());
        _userExternalLoginRepository.GetByProviderAndKey(accountKey)
            .Returns(new UserExternalLogin
            {
                Id = Guid.CreateVersion7(),
                UserId = localUserId,
                User = null!,
                AuthenticationProviderId = (int)"keycloak".ParseAuthenticationProviderKind(),
                AuthenticationProvider = null!,
                ProviderKey = accountKey.Value,
                ProviderDisplayName = "Keycloak",
                CreatedAt = DateTime.UtcNow,
                CreatedBy = localUserId
            });
        _adminContext.IsInstanceAdminAsync(localUserId, Arg.Any<CancellationToken>()).Returns(true);
        _adminContext.GetAdminTenantIdsAsync(localUserId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Guid>().ToList().AsReadOnly() as IReadOnlyList<Guid>);
        _adminContext.GetAdminOrganizationIdsAsync(localUserId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Guid>().ToList().AsReadOnly() as IReadOnlyList<Guid>);

        // Act
        var result = await _sut.TransformAsync(principal);

        // Assert
        await Assert.That(result.HasClaim("internal_user_id", localUserId.ToString())).IsTrue();
        await Assert.That(result.HasClaim("internal_user_id", keycloakSubject.ToString())).IsFalse();
        await Assert.That(result.HasClaim(AdminClaimTypes.InstanceAdmin, "true")).IsTrue();
    }

    [Test]
    [Arguments(ApiAuthenticationSchemeNames.ApiKey)]
    [Arguments(ApiAuthenticationSchemeNames.AdmissionScanner)]
    public async Task TransformAsync_PurposeBoundPrincipalDoesNotGainInternalOrAdminClaims(string scheme)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", Guid.NewGuid().ToString("D"))],
            scheme));
        bool acquiredAdminContext = false;
        var authority = Substitute.For<IAdministrativeAuthorityRepository>();
        authority.IsInstanceAdminAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            acquiredAdminContext = true;
            return Task.FromException<bool>(
                new InvalidOperationException("Purpose-bound authentication cannot acquire administrator persistence."));
        });
        var guarded = new AdminClaimsTransformation(authority, _userExternalLoginRepository, _logger);

        var result = await guarded.TransformAsync(principal);

        await Assert.That(result.HasClaim(c => c.Type == "internal_user_id")).IsFalse();
        await Assert.That(result.HasClaim(c => c.Type == AdminClaimTypes.InstanceAdmin)).IsFalse();
        await Assert.That(acquiredAdminContext).IsFalse();
    }

    [Test]
    public async Task TransformAsync_InstanceAdmin_AddsInstanceAdminClaim()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var identity = new ClaimsIdentity(new[] { new Claim("sub", userId.ToString()) }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        _adminContext.IsInstanceAdminAsync(userId, Arg.Any<CancellationToken>()).Returns(true);
        _adminContext.GetAdminTenantIdsAsync(userId, Arg.Any<CancellationToken>()).Returns(Array.Empty<Guid>().ToList().AsReadOnly() as IReadOnlyList<Guid>);
        _adminContext.GetAdminOrganizationIdsAsync(userId, Arg.Any<CancellationToken>()).Returns(Array.Empty<Guid>().ToList().AsReadOnly() as IReadOnlyList<Guid>);

        // Act
        var result = await _sut.TransformAsync(principal);

        // Assert
        await Assert.That(result.HasClaim(AdminClaimTypes.InstanceAdmin, "true")).IsTrue();
        await Assert.That(result.HasClaim(c => c.Type == AdminClaimTypes.TenantAdmin)).IsFalse();
        await Assert.That(result.HasClaim(c => c.Type == AdminClaimTypes.OrganizationAdmin)).IsFalse();
    }

    [Test]
    public async Task TransformAsync_TenantAdmin_AddsTenantAdminClaimsPerTenant()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var tenantId1 = Guid.NewGuid();
        var tenantId2 = Guid.NewGuid();
        var identity = new ClaimsIdentity(new[] { new Claim("sub", userId.ToString()) }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        _adminContext.IsInstanceAdminAsync(userId, Arg.Any<CancellationToken>()).Returns(false);
        _adminContext.GetAdminTenantIdsAsync(userId, Arg.Any<CancellationToken>()).Returns(new List<Guid> { tenantId1, tenantId2 }.AsReadOnly() as IReadOnlyList<Guid>);
        _adminContext.GetAdminOrganizationIdsAsync(userId, Arg.Any<CancellationToken>()).Returns(Array.Empty<Guid>().ToList().AsReadOnly() as IReadOnlyList<Guid>);

        // Act
        var result = await _sut.TransformAsync(principal);

        // Assert
        await Assert.That(result.HasClaim(AdminClaimTypes.TenantAdmin, tenantId1.ToString())).IsTrue();
        await Assert.That(result.HasClaim(AdminClaimTypes.TenantAdmin, tenantId2.ToString())).IsTrue();
        var tenantClaims = result.FindAll(c => c.Type == AdminClaimTypes.TenantAdmin);
        await Assert.That(tenantClaims.Count()).IsEqualTo(2);
    }

    [Test]
    public async Task TransformAsync_OrganizationAdmin_AddsOrganizationAdminClaimsPerOrg()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var orgId1 = Guid.NewGuid();
        var orgId2 = Guid.NewGuid();
        var orgId3 = Guid.NewGuid();
        var identity = new ClaimsIdentity(new[] { new Claim("sub", userId.ToString()) }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        _adminContext.IsInstanceAdminAsync(userId, Arg.Any<CancellationToken>()).Returns(false);
        _adminContext.GetAdminTenantIdsAsync(userId, Arg.Any<CancellationToken>()).Returns(Array.Empty<Guid>().ToList().AsReadOnly() as IReadOnlyList<Guid>);
        _adminContext.GetAdminOrganizationIdsAsync(userId, Arg.Any<CancellationToken>()).Returns(new List<Guid> { orgId1, orgId2, orgId3 }.AsReadOnly() as IReadOnlyList<Guid>);

        // Act
        var result = await _sut.TransformAsync(principal);

        // Assert
        await Assert.That(result.HasClaim(AdminClaimTypes.OrganizationAdmin, orgId1.ToString())).IsTrue();
        await Assert.That(result.HasClaim(AdminClaimTypes.OrganizationAdmin, orgId2.ToString())).IsTrue();
        await Assert.That(result.HasClaim(AdminClaimTypes.OrganizationAdmin, orgId3.ToString())).IsTrue();
        var orgClaims = result.FindAll(c => c.Type == AdminClaimTypes.OrganizationAdmin);
        await Assert.That(orgClaims.Count()).IsEqualTo(3);
    }

    [Test]
    public async Task TransformAsync_FullAdmin_AddsAllAdminClaims()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var identity = new ClaimsIdentity(new[] { new Claim("sub", userId.ToString()) }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        _adminContext.IsInstanceAdminAsync(userId, Arg.Any<CancellationToken>()).Returns(true);
        _adminContext.GetAdminTenantIdsAsync(userId, Arg.Any<CancellationToken>()).Returns(new List<Guid> { tenantId }.AsReadOnly() as IReadOnlyList<Guid>);
        _adminContext.GetAdminOrganizationIdsAsync(userId, Arg.Any<CancellationToken>()).Returns(new List<Guid> { orgId }.AsReadOnly() as IReadOnlyList<Guid>);
        _adminContext.GetAdminGroupIdsAsync(userId, Arg.Any<CancellationToken>()).Returns([groupId]);

        // Act
        var result = await _sut.TransformAsync(principal);

        // Assert
        await Assert.That(result.HasClaim(AdminClaimTypes.InstanceAdmin, "true")).IsTrue();
        await Assert.That(result.HasClaim(AdminClaimTypes.TenantAdmin, tenantId.ToString())).IsTrue();
        await Assert.That(result.HasClaim(AdminClaimTypes.OrganizationAdmin, orgId.ToString())).IsTrue();
        await Assert.That(result.HasClaim(AdminClaimTypes.GroupAdmin, groupId.ToString())).IsTrue();
    }

    [Test]
    public async Task TransformAsync_ExplicitUserDoesNotConstructOrdinaryAdministrativeContext()
    {
        var userId = Guid.CreateVersion7();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", userId.ToString())], "TestAuth"));
        _adminContext.IsInstanceAdminAsync(userId).Returns(true);
        _adminContext.GetAdminTenantIdsAsync(userId).Returns([]);
        _adminContext.GetAdminOrganizationIdsAsync(userId).Returns([]);
        _adminContext.GetAdminGroupIdsAsync(userId).Returns([]);
        bool acquiredAdminContext = false;
        var services = new ServiceCollection();
        services.AddSingleton(_adminContext);
        services.AddSingleton(_userExternalLoginRepository);
        services.AddSingleton(_logger);
        services.AddTransient<IAdminContext>(_ =>
        {
            acquiredAdminContext = true;
            throw new InvalidOperationException("Pre-tenant claims enrichment must not acquire an ordinary request lease.");
        });
        services.AddTransient<AdminClaimsTransformation>();
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<AdminClaimsTransformation>().TransformAsync(principal);

        await Assert.That(result.HasClaim(AdminClaimTypes.InstanceAdmin, "true")).IsTrue();
        await Assert.That(result.HasClaim(PlatformIdentityClaimTypes.InternalUserId, userId.ToString())).IsTrue();
        await Assert.That(acquiredAdminContext).IsFalse();
    }

    [Test]
    public async Task TransformAsync_AlreadyHasAdminClaims_SkipsTransformation()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("sub", userId.ToString()),
            new Claim(AdminClaimTypes.InstanceAdmin, "true")
        }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = await _sut.TransformAsync(principal);

        // Assert — IAdminContext should never be called since claims already exist
        await _adminContext.DidNotReceive().IsInstanceAdminAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _adminContext.DidNotReceive().GetAdminTenantIdsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _adminContext.DidNotReceive().GetAdminOrganizationIdsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        // Only the original claim should exist (no duplicates)
        var instanceClaims = result.FindAll(c => c.Type == AdminClaimTypes.InstanceAdmin);
        await Assert.That(instanceClaims.Count()).IsEqualTo(1);
    }

    [Test]
    public async Task TransformAsync_AlreadyHasTenantAdminClaim_SkipsTransformation()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("sub", userId.ToString()),
            new Claim(AdminClaimTypes.TenantAdmin, tenantId.ToString())
        }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = await _sut.TransformAsync(principal);

        // Assert — IAdminContext should never be called since claims already exist
        await _adminContext.DidNotReceive().IsInstanceAdminAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TransformAsync_AlreadyHasOrganizationAdminClaim_SkipsTransformation()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("sub", userId.ToString()),
            new Claim(AdminClaimTypes.OrganizationAdmin, orgId.ToString())
        }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = await _sut.TransformAsync(principal);

        // Assert — IAdminContext should never be called since claims already exist
        await _adminContext.DidNotReceive().IsInstanceAdminAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TransformAsync_AdminContextThrowsException_PreservesAuthenticationWithoutDisclosingFailure()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var identity = new ClaimsIdentity(new[] { new Claim("sub", userId.ToString()) }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        string privateFailure = $"private-database-{Guid.CreateVersion7():N}";
        _adminContext.IsInstanceAdminAsync(userId, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException(privateFailure));
        var logger = new TestListLogger<AdminClaimsTransformation>();
        var sut = new AdminClaimsTransformation(_adminContext, _userExternalLoginRepository, logger);

        // Act
        var result = await sut.TransformAsync(principal);

        // Assert — should return principal without admin claims (fail-open)
        await Assert.That(result.HasClaim(c => c.Type == AdminClaimTypes.InstanceAdmin)).IsFalse();
        await Assert.That(result.HasClaim(c => c.Type == AdminClaimTypes.TenantAdmin)).IsFalse();
        await Assert.That(result.HasClaim(c => c.Type == AdminClaimTypes.OrganizationAdmin)).IsFalse();

        await Assert.That(result.Identity!.IsAuthenticated).IsTrue();
        await Assert.That(logger.Entries.Any(entry => entry.Level == LogLevel.Warning)).IsTrue();
        await Assert.That(logger.Entries.All(entry => entry.Exception is null
            && !entry.Message.Contains(privateFailure, StringComparison.Ordinal)
            && entry.Arguments.All(argument => !(argument?.ToString()?.Contains(
                privateFailure, StringComparison.Ordinal) ?? false)))).IsTrue();
    }

    [Test]
    public async Task TransformAsync_InstanceAdminOnly_DoesNotAddTenantOrOrgClaims()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var identity = new ClaimsIdentity(new[] { new Claim("sub", userId.ToString()) }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        _adminContext.IsInstanceAdminAsync(userId, Arg.Any<CancellationToken>()).Returns(true);
        _adminContext.GetAdminTenantIdsAsync(userId, Arg.Any<CancellationToken>()).Returns(Array.Empty<Guid>().ToList().AsReadOnly() as IReadOnlyList<Guid>);
        _adminContext.GetAdminOrganizationIdsAsync(userId, Arg.Any<CancellationToken>()).Returns(Array.Empty<Guid>().ToList().AsReadOnly() as IReadOnlyList<Guid>);

        // Act
        var result = await _sut.TransformAsync(principal);

        // Assert
        var allAdminClaims = result.FindAll(c =>
            c.Type == AdminClaimTypes.InstanceAdmin
            || c.Type == AdminClaimTypes.TenantAdmin
            || c.Type == AdminClaimTypes.OrganizationAdmin);
        await Assert.That(allAdminClaims.Count()).IsEqualTo(1);
        await Assert.That(allAdminClaims.First().Type).IsEqualTo(AdminClaimTypes.InstanceAdmin);
    }

    [Test]
    public async Task TransformAsync_CallsAllAdminContextMethods()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var identity = new ClaimsIdentity(new[] { new Claim("sub", userId.ToString()) }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        _adminContext.IsInstanceAdminAsync(userId, Arg.Any<CancellationToken>()).Returns(false);
        _adminContext.GetAdminTenantIdsAsync(userId, Arg.Any<CancellationToken>()).Returns(Array.Empty<Guid>().ToList().AsReadOnly() as IReadOnlyList<Guid>);
        _adminContext.GetAdminOrganizationIdsAsync(userId, Arg.Any<CancellationToken>()).Returns(Array.Empty<Guid>().ToList().AsReadOnly() as IReadOnlyList<Guid>);

        // Act
        await _sut.TransformAsync(principal);

        // Assert — all three admin resolution methods should be called with the userId
        await _adminContext.Received(1).IsInstanceAdminAsync(userId, Arg.Any<CancellationToken>());
        await _adminContext.Received(1).GetAdminTenantIdsAsync(userId, Arg.Any<CancellationToken>());
        await _adminContext.Received(1).GetAdminOrganizationIdsAsync(userId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TransformAsync_UsesNameIdentifierClaim_WhenSubMissing()
    {
        // Arrange — no "sub" claim, but ClaimTypes.NameIdentifier is present
        var userId = Guid.NewGuid();
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        _adminContext.IsInstanceAdminAsync(userId, Arg.Any<CancellationToken>()).Returns(true);
        _adminContext.GetAdminTenantIdsAsync(userId, Arg.Any<CancellationToken>()).Returns(Array.Empty<Guid>().ToList().AsReadOnly() as IReadOnlyList<Guid>);
        _adminContext.GetAdminOrganizationIdsAsync(userId, Arg.Any<CancellationToken>()).Returns(Array.Empty<Guid>().ToList().AsReadOnly() as IReadOnlyList<Guid>);

        // Act
        var result = await _sut.TransformAsync(principal);

        // Assert — should still resolve admin claims via NameIdentifier fallback
        await Assert.That(result.HasClaim(AdminClaimTypes.InstanceAdmin, "true")).IsTrue();
        await _adminContext.Received(1).IsInstanceAdminAsync(userId, Arg.Any<CancellationToken>());
    }
}
