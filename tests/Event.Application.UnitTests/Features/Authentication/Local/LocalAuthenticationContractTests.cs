using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Features.Authentication.Local.Validators;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Identity;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.Onboarding.Validators;
using Explore.Domain.Enums;
using System.Security.Cryptography;
using System.Text.Json;

namespace Event.Application.UnitTests.Features.Authentication.Local;

public sealed class LocalAuthenticationContractTests
{
    [Test]
    public async Task SessionAuthorityRejectsMissingSubjectAndInvalidStamp()
    {
        Guid subjectId = Guid.CreateVersion7();
        string stamp = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var invalidAuthorities = new (Guid SubjectId, string? Stamp)[]
        {
            (Guid.Empty, stamp),
            (subjectId, null),
            (subjectId, string.Empty),
            (subjectId, " "),
            (subjectId, Convert.ToHexString(RandomNumberGenerator.GetBytes(129)))
        };

        foreach (var invalid in invalidAuthorities)
        {
            bool rejected = false;
            try
            {
                _ = new LocalSessionAuthority(
                    localSubjectId: invalid.SubjectId, securityStamp: invalid.Stamp!, emailVerified: true);
            }
            catch (ArgumentException)
            {
                rejected = true;
            }

            await Assert.That(rejected).IsTrue();
        }
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task SigningSubjectRequiresAuthorityAndKeepsItsDiagnosticsValueFree(bool emailVerified)
    {
        Guid subjectId = Guid.CreateVersion7();
        string stamp = Convert.ToHexString(RandomNumberGenerator.GetBytes(128));
        string email = $"{Guid.CreateVersion7():N}@example.test";
        string firstName = Guid.CreateVersion7().ToString("N");
        string lastName = Guid.CreateVersion7().ToString("N");
        var authority = new LocalSessionAuthority(
            localSubjectId: subjectId, securityStamp: stamp, emailVerified: emailVerified);
        var roles = new List<string> { "Admin" };
        var subject = new LocalJwtTokenSubject(
            authority: authority, email: email, firstName: firstName, lastName: lastName,
            roles: roles);
        roles.Add("Unassigned");

        await Assert.That(subject.Authority.LocalSubjectId).IsEqualTo(subjectId);
        await Assert.That(subject.Authority.EmailVerified).IsEqualTo(emailVerified);
        await Assert.That(string.Equals(subject.Authority.SecurityStamp, stamp, StringComparison.Ordinal)).IsTrue();
        await Assert.That(subject.Roles).IsEquivalentTo(["Admin"]);
        string diagnostic = $"{authority} {subject}";
        await Assert.That(diagnostic.Contains(stamp, StringComparison.Ordinal)
            || diagnostic.Contains(subjectId.ToString("D"), StringComparison.Ordinal)
            || diagnostic.Contains(email, StringComparison.Ordinal)
            || diagnostic.Contains(firstName, StringComparison.Ordinal)
            || diagnostic.Contains(lastName, StringComparison.Ordinal)).IsFalse();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
        {
            _ = new LocalJwtTokenSubject(
                authority: null!, email: email, firstName: firstName, lastName: lastName,
                roles: []);
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task ReplacementResponseCarriesNoOrdinarySessionOrProfile()
    {
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var challenge = new LocalIssuedReplacementChallenge(token: token, expiresAt: DateTimeOffset.UtcNow.AddMinutes(5));
        LocalAuthResponseDto response = LocalAuthResponseDto.ReplacementRequired(challenge: challenge);

        await Assert.That(response.Outcome).IsEqualTo(LocalAuthOutcome.ReplacementRequired);
        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Failure).IsNull();
        await Assert.That(response.Token).IsNull();
        await Assert.That(response.ExpiresAt).IsNull();
        await Assert.That(response.UserId).IsNull();
        await Assert.That(response.Email).IsNull();
        await Assert.That(response.FirstName).IsNull();
        await Assert.That(response.LastName).IsNull();
        await Assert.That(response.Roles.Count).IsEqualTo(0);
        await Assert.That(response.ToString().Contains(token, StringComparison.Ordinal)
            || challenge.ToString().Contains(token, StringComparison.Ordinal)).IsFalse();
        using JsonDocument json = JsonSerializer.SerializeToDocument(response, JsonSerializerOptions.Web);
        await Assert.That(json.RootElement.GetProperty("success").GetBoolean()).IsFalse();
        await Assert.That(json.RootElement.GetProperty("token").ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(json.RootElement.GetProperty("replacementChallenge").GetProperty("token").GetString()).IsEqualTo(token);
        await Assert.That(json.RootElement.TryGetProperty("outcome", out _)).IsFalse();
    }

    [Test]
    public async Task ReplacementRequestDiagnosticTextDoesNotExposePasswordOrStamp()
    {
        string stamp = Guid.CreateVersion7().ToString("N");
        string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var subject = new LocalCredentialReplacementSubject(
            localSubjectId: Guid.CreateVersion7(), operationId: Guid.CreateVersion7(), securityStamp: stamp);
        var authority = new LocalCredentialReplacementAuthority(subject: subject, issuedAtUtc: now, expiresAtUtc: now.AddMinutes(5));
        var request = new LocalCredentialReplacementRequest(authority: authority, newPassword: password);

        string diagnostic = $"{subject} {authority} {request}";

        await Assert.That(diagnostic.Contains(stamp, StringComparison.Ordinal)
            || diagnostic.Contains(password, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    [Arguments("operator")]
    [Arguments("operator.name-1")]
    [Arguments("operator@example.test")]
    public async Task LoginValidatorAcceptsBoundedUsernameOrEmail(string identifier)
    {
        var result = await new LocalAuthRequestDtoValidator().ValidateAsync(
            new LocalAuthRequestDto(identifier, $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}"));
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    [Arguments("invalid@")]
    [Arguments("@example.test")]
    [Arguments("operator name")]
    [Arguments("operator\nname")]
    [Arguments("")]
    public async Task LoginValidatorRejectsInvalidIdentifiers(string identifier)
    {
        var result = await new LocalAuthRequestDtoValidator().ValidateAsync(
            new LocalAuthRequestDto(identifier, $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}"));
        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task LocalSetupValidatorPreservesOptionalEmailAndSeparateLegalIdentity()
    {
        var request = new CompleteLocalInstanceOnboardingRequestDto
        {
            OperationId = Guid.CreateVersion7(),
            Username = $"operator-{Guid.CreateVersion7():N}",
            TemporaryPassword = $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}",
            Settings = new CompleteInstanceOnboardingRequest
            {
                DeploymentMode = DeploymentMode.MultiTenant,
                SiteProfile = new SelfHostOnboardingProfileDto { SiteName = "Local operator" }
            }
        };
        var validator = new CompleteLocalInstanceOnboardingRequestDtoValidator();
        await Assert.That((await validator.ValidateAsync(request)).IsValid).IsTrue();
        var invalid = new[]
        {
            request with { OperationId = Guid.Empty },
            request with { Username = "operator@example.test" },
            request with { Username = new string('x', 257) },
            request with { Email = "invalid@" },
            request with { Email = string.Empty },
            request with { TemporaryPassword = new string('x', 129) },
            request with { Settings = request.Settings with { DeploymentMode = DeploymentMode.SingleTenant } }
        };
        foreach (var item in invalid)
        {
            var result = await validator.ValidateAsync(item);
            await Assert.That(result.IsValid).IsFalse();
            await Assert.That(result.Errors.Any(error => error.ErrorMessage.Contains(request.TemporaryPassword, StringComparison.Ordinal))).IsFalse();
        }
        await Assert.That(request.ToString().Contains(request.TemporaryPassword, StringComparison.Ordinal)
            || request.ToString().Contains(request.Username, StringComparison.Ordinal)).IsFalse();
        using JsonDocument json = JsonSerializer.SerializeToDocument(request, JsonSerializerOptions.Web);
        await Assert.That(json.RootElement.GetProperty("email").ValueKind).IsEqualTo(JsonValueKind.Null);
        using JsonDocument login = JsonSerializer.SerializeToDocument(new LocalAuthRequestDto(request.Username, request.TemporaryPassword), JsonSerializerOptions.Web);
        await Assert.That(login.RootElement.GetProperty("identifier").GetString()).IsEqualTo(request.Username);
        await Assert.That(login.RootElement.TryGetProperty("email", out _)).IsFalse();
    }

    [Test]
    public async Task LoginValidatorRejectsMalformedCredentials()
    {
        var request = new LocalAuthRequestDto("invalid@", string.Empty);

        var result = await new LocalAuthRequestDtoValidator().ValidateAsync(request);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Select(error => error.PropertyName))
            .Contains(nameof(LocalAuthRequestDto.Identifier));
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
        await Assert.That(response.Failure).IsNull();
        await Assert.That(response.FailureCode).IsEqualTo(string.Empty);
        await Assert.That(response.Roles).IsEquivalentTo(["Admin"]);
    }

    [Test]
    public async Task LoginRequestDiagnosticTextDoesNotDiscloseCredentials()
    {
        string email = $"request-{Guid.CreateVersion7():N}@example.test";
        string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var request = new LocalAuthRequestDto(Identifier: email, Password: password);

        string diagnostic = request.ToString();

        await Assert.That(diagnostic.Contains(password, StringComparison.Ordinal)
            || diagnostic.Contains(email, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task AuthenticatedResponseDiagnosticTextDoesNotDiscloseSessionOrProfile()
    {
        string email = $"response-{Guid.CreateVersion7():N}@example.test";
        string firstName = $"First-{Guid.CreateVersion7():N}";
        string lastName = $"Last-{Guid.CreateVersion7():N}";
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        LocalAuthResponseDto response = LocalAuthResponseDto.Authenticated(
            userId: Guid.CreateVersion7(), email: email, firstName: firstName, lastName: lastName,
            emailVerified: true, roles: [], token: token, expiresAt: DateTimeOffset.UtcNow.AddMinutes(5));

        string diagnostic = response.ToString();

        await Assert.That(diagnostic.Contains(token, StringComparison.Ordinal)
            || diagnostic.Contains(email, StringComparison.Ordinal)
            || diagnostic.Contains(firstName, StringComparison.Ordinal)
            || diagnostic.Contains(lastName, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task IssuedTokenDiagnosticTextDoesNotDiscloseBearerMaterial()
    {
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var issued = new LocalIssuedToken(Token: token, ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(5));

        string diagnostic = issued.ToString();

        await Assert.That(diagnostic.Contains(token, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    [Arguments((LocalAuthFailure)0)]
    [Arguments((LocalAuthFailure)int.MaxValue)]
    public async Task UndefinedFailureCannotConstructAnAuthenticationResult(LocalAuthFailure failure)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
        {
            _ = LocalAuthResponseDto.Failed(failure);
            return Task.CompletedTask;
        });
    }

    [Test]
    [Arguments(LocalAuthFailure.InvalidRequest, "invalid_request")]
    [Arguments(LocalAuthFailure.InvalidCredentials, "invalid_credentials")]
    [Arguments(LocalAuthFailure.AccountLocked, "account_locked")]
    [Arguments(LocalAuthFailure.ProviderInactive, "provider_inactive")]
    [Arguments(LocalAuthFailure.UserSynchronizationFailed, "user_sync_failed")]
    [Arguments(LocalAuthFailure.AuthenticationFailed, "authentication_failed")]
    [Arguments(LocalAuthFailure.EmailVerificationRequired, "email_verification_required")]
    public async Task FailureContainsNoSessionAuthorityAndSerializesOnlyItsPublicCode(
        LocalAuthFailure failure, string expectedCode)
    {
        LocalAuthResponseDto response = LocalAuthResponseDto.Failed(failure);

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Failure).IsEqualTo(failure);
        await Assert.That(response.FailureCode).IsEqualTo(expectedCode);
        await Assert.That(response.UserId).IsNull();
        await Assert.That(response.Email).IsNull();
        await Assert.That(response.FirstName).IsNull();
        await Assert.That(response.LastName).IsNull();
        await Assert.That(response.EmailVerified).IsFalse();
        await Assert.That(response.Roles.Count).IsEqualTo(0);
        await Assert.That(response.Token).IsNull();
        await Assert.That(response.ExpiresAt).IsNull();

        using JsonDocument json = JsonSerializer.SerializeToDocument(response, JsonSerializerOptions.Web);

        await Assert.That(json.RootElement.GetProperty("success").GetBoolean()).IsFalse();
        await Assert.That(json.RootElement.GetProperty("failureCode").GetString()).IsEqualTo(expectedCode);
        await Assert.That(json.RootElement.TryGetProperty("failure", out _)).IsFalse();
        await Assert.That(json.RootElement.GetProperty("token").ValueKind).IsEqualTo(JsonValueKind.Null);
    }
}
