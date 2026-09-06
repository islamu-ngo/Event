// ABOUTME: Verifies immutable local authentication contracts and their credential boundary validation.
// ABOUTME: Rejects malformed credentials before Identity access and snapshots issued role collections.

using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Features.Authentication.Local.Validators;
using System.Security.Cryptography;

namespace Event.Application.UnitTests.Features.Authentication.Local;

public sealed class LocalAuthenticationContractTests
{
    [Test]
    public async Task LoginValidatorRejectsMalformedCredentials()
    {
        var request = new LocalAuthRequestDto("not-an-email", string.Empty);

        var result = await new LocalAuthRequestDtoValidator().ValidateAsync(request);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Select(error => error.PropertyName))
            .Contains(nameof(LocalAuthRequestDto.Email));
        await Assert.That(result.Errors.Select(error => error.PropertyName))
            .Contains(nameof(LocalAuthRequestDto.Password));
    }

    [Test]
    public async Task AuthenticatedResponseSnapshotsAssignedRoles()
    {
        var roles = new List<string> { "Admin" };

        LocalAuthResponseDto response = LocalAuthResponseDto.Authenticated(
            userId: Guid.CreateVersion7(),
            email: "admin@example.test",
            firstName: "Site",
            lastName: "Administrator",
            emailVerified: true,
            roles: roles,
            token: Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(30));
        roles.Add("Unexpected");

        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Roles).IsEquivalentTo(["Admin"]);
    }
}
