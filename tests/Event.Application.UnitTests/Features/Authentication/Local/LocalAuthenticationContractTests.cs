using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Features.Authentication.Local.Validators;
using Explore.Application.Features.Authentication.Local.Requests.Commands;
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
    public async Task RegistrationValidatorRejectsMalformedProfileAndCredentials()
    {
        var request = new LocalRegistrationRequestDto(
            "not-an-email",
            string.Empty,
            string.Empty,
            string.Empty);

        var result = await new LocalRegistrationRequestDtoValidator().ValidateAsync(request);

        await Assert.That(result.IsValid).IsFalse();
        string[] invalidProperties = result.Errors
            .Select(error => error.PropertyName)
            .ToArray();
        await Assert.That(invalidProperties).Contains(nameof(LocalRegistrationRequestDto.Email));
        await Assert.That(invalidProperties).Contains(nameof(LocalRegistrationRequestDto.Password));
        await Assert.That(invalidProperties).Contains(nameof(LocalRegistrationRequestDto.FirstName));
        await Assert.That(invalidProperties).Contains(nameof(LocalRegistrationRequestDto.LastName));
    }

    [Test]
    public async Task CommandsPreserveTheirImmutableRequestContracts()
    {
        string password = CreateValidPassword();
        var loginRequest = new LocalAuthRequestDto("admin@example.test", password);
        var registrationRequest = new LocalRegistrationRequestDto(
            "admin@example.test",
            password,
            "Site",
            "Administrator");

        var login = new LocalLoginCommand(loginRequest);
        var registration = new LocalRegisterCommand(registrationRequest);

        await Assert.That(login.Request).IsEqualTo(loginRequest);
        await Assert.That(registration.Request).IsEqualTo(registrationRequest);
    }

    [Test]
    public async Task AuthenticatedResponseSnapshotsAssignedRoles()
    {
        var roles = new List<string> { "Admin" };

        LocalAuthResponseDto response = LocalAuthResponseDto.Authenticated(
            Guid.CreateVersion7(),
            "admin@example.test",
            "Site",
            "Administrator",
            true,
            roles,
            Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            DateTimeOffset.UtcNow.AddMinutes(30));
        roles.Add("Unexpected");

        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Roles).IsEquivalentTo(["Admin"]);
    }

    private static string CreateValidPassword() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
}
