
using System.IdentityModel.Tokens.Jwt;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Configuration;
using Explore.Application.Constants;
using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Event.API.IntegrationTests.Features;

[NotInParallel]
public sealed class LocalCredentialReplacementHttpTests
{
    public enum SessionAdmissionBoundary { Http, NativeAuthentication }
    private const string ReplacementPath = "/api/auth/local/credential-replacement";

    public enum InvalidChallenge
    {
        Expired,
        TamperedSignature,
        WrongPurpose,
        WrongAudience,
        WrongTokenType,
        DuplicateOperationClaim,
        MissingSecurityStamp,
        MultipleAudiences,
        LifetimeTooLong,
        FutureIssuedAt,
        MalformedIssuedAt,
        NoncanonicalSubject,
        EmptyGuidSubject,
        ForbiddenEmail,
        ForbiddenRole,
        DuplicateJsonOperation
    }

    public enum InvalidReplacementBody
    {
        MissingPassword,
        BlankPassword,
        TooLongPassword,
        UnknownAccount,
        UnknownAuthority
    }

    public enum AdminAuthorityConsumer
    {
        Context,
        ClaimsTransformation
    }

    [Test]
    [Arguments(AdminAuthorityConsumer.Context)]
    [Arguments(AdminAuthorityConsumer.ClaimsTransformation)]
    public async Task InvalidLocalProviderProjectionCannotFallBackToAnotherAccountsAdministratorId(AdminAuthorityConsumer consumer)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        Guid administratorId;
        await using (ExploreDbContext seed = factory.CreateDatabase())
        {
            administratorId = (await seed.InstanceBootstrapStates.SingleAsync(CancellationToken)).CompletedByUserId!.Value;
            Role role = await seed.Set<Role>().SingleAsync(candidate => candidate.MasterCode == "platform.admin", CancellationToken);
            seed.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(), UserId = administratorId, User = null!,
                RoleId = role.Id, Role = role, GrantedAt = DateTime.UtcNow
            });
            await seed.SaveChangesAsync(CancellationToken);
            await Assert.That(await seed.UserExternalLogins.AnyAsync(login => login.UserId == administratorId, CancellationToken)).IsFalse();
        }
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        await Assert.That(await scope.ServiceProvider.GetRequiredService<IPlatformUserRoleRepository>()
            .IsUserPlatformAdmin(administratorId)).IsTrue();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", administratorId.ToString("D")),
            new Claim("auth_provider", "local"),
            new Claim("iss", "https://trusted.example.test/realms/external")
        ], authenticationType: "Bearer"));
        await Assert.That(principal.GetProviderIdentity()).IsNull();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        HttpContext? previous = accessor.HttpContext;
        try
        {
            accessor.HttpContext = new DefaultHttpContext { User = principal, RequestServices = scope.ServiceProvider };
            if (consumer == AdminAuthorityConsumer.Context)
            {
                var admin = scope.ServiceProvider.GetRequiredService<IAdminContext>();
                Guid? resolved = await admin.ResolveUserIdAsync(CancellationToken);
                bool instanceAdmin = await admin.IsInstanceAdminAsync(CancellationToken);

                await Assert.That(resolved).IsNull();
                await Assert.That(instanceAdmin).IsFalse();
            }
            else
            {
                ClaimsPrincipal enriched = await scope.ServiceProvider.GetRequiredService<IClaimsTransformation>().TransformAsync(principal);

                await Assert.That(enriched.HasClaim(claim => claim.Type == AdminClaimTypes.InstanceAdmin)).IsFalse();
                await Assert.That(enriched.HasClaim(claim => claim.Type == PlatformIdentityClaimTypes.InternalUserId)).IsFalse();
            }
        }
        finally
        {
            accessor.HttpContext = previous;
        }
    }

    [Test]
    public async Task OrdinaryBearerCanSynchronizeButCannotAuthorizeCredentialReplacement()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using HttpClient client = CreateClient(factory);
        Credential credential = await SeedChangeRequiredAsync(factory);
        string readyPassword = NewPassword();
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            Snapshot current = await ReadAsync(factory, credential);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var authority = new LocalCredentialReplacementAuthority(
                subject: new LocalCredentialReplacementSubject(localSubjectId: credential.Receipt.LocalSubjectId,
                    operationId: credential.Receipt.OperationId, securityStamp: current.SecurityStamp!),
                issuedAtUtc: now, expiresAtUtc: now.AddMinutes(5));
            LocalCredentialReplacementOutcome readied = await scope.ServiceProvider.GetRequiredService<ILocalCredentialAdministration>()
                .ReplaceAsync(new LocalCredentialReplacementRequest(authority: authority, newPassword: readyPassword), CancellationToken);
            await Assert.That(readied).IsEqualTo(LocalCredentialReplacementOutcome.Replaced);
        }
        string accessToken = await LoginForAccessTokenAsync(client,
            new LocalAuthRequestDto(Identifier: credential.Login.Identifier, Password: readyPassword));
        using (var sync = new HttpRequestMessage(HttpMethod.Post, "/api/User/sync"))
        {
            sync.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using HttpResponseMessage positive = await client.SendAsync(sync, CancellationToken);
            await Assert.That(positive.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
        Snapshot before = await ReadAsync(factory, credential);

        using HttpResponseMessage denied = await ReplaceAsync(client: client, token: accessToken, password: NewPassword());

        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await AssertUnchangedAsync(factory, credential, before);
    }

    [Test]
    [Arguments(SessionAdmissionBoundary.Http)]
    [Arguments(SessionAdmissionBoundary.NativeAuthentication)]
    public async Task CommittedResetRevokesPreviouslyIssuedBearerBeforePrincipalEnrichmentAndFreshLoginRestoresAccess(
        SessionAdmissionBoundary boundary)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using HttpClient client = CreateClient(factory);
        Credential credential = await SeedChangeRequiredAsync(factory);
        string readyPassword = NewPassword();
        await CompletePrivateReplacementAsync(operationId: credential.Receipt.OperationId, password: readyPassword);
        string bearer = await LoginForAccessTokenAsync(client,
            new LocalAuthRequestDto(Identifier: credential.Login.Identifier, Password: readyPassword));
        using (HttpResponseMessage positive = await SynchronizeAsync(bearer))
            await Assert.That(positive.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await AssertCurrentUserAsync(bearer);
        Snapshot before = await ReadAsync(factory, credential);
        var resetRequest = new LocalCredentialResetRequest(operationId: Guid.CreateVersion7(),
            initiatingApplicationUserId: credential.Receipt.InitiatingApplicationUserId,
            localSubjectId: credential.Receipt.LocalSubjectId, expectedCurrentOperationId: credential.Receipt.OperationId,
            expectedCurrentOperationConcurrencyStamp: before.OperationStamp, reason: "Supervised bearer revocation");
        await using (AsyncServiceScope reset = factory.Services.CreateAsyncScope())
            await Assert.That((await reset.ServiceProvider.GetRequiredService<ILocalCredentialAdministration>()
                .ResetAsync(resetRequest, CancellationToken)).Outcome).IsEqualTo(LocalCredentialResetOutcome.Reset);

        if (boundary == SessionAdmissionBoundary.NativeAuthentication)
        {
            await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
            var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            context.Request.Headers.Authorization = $"Bearer {bearer}";
            AuthenticateResult authentication = await scope.ServiceProvider.GetRequiredService<IAuthenticationService>()
                .AuthenticateAsync(context, ApiAuthenticationSchemeNames.LocalIdentity);
            await Assert.That(authentication.Succeeded).IsFalse();
            await Assert.That(authentication.Principal).IsNull();
        }
        else
        {
            using HttpResponseMessage denied = await SynchronizeAsync(bearer);
            await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }

        string newPassword = NewPassword();
        await CompletePrivateReplacementAsync(operationId: resetRequest.OperationId, password: newPassword);
        string freshBearer = await LoginForAccessTokenAsync(client,
            new LocalAuthRequestDto(Identifier: credential.Login.Identifier, Password: newPassword));
        using (HttpResponseMessage restored = await SynchronizeAsync(freshBearer))
            await Assert.That(restored.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await AssertCurrentUserAsync(freshBearer);
        using (HttpResponseMessage stillRevoked = await SynchronizeAsync(bearer))
            await Assert.That(stillRevoked.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(await PasswordIsValidAsync(factory, credential, readyPassword)).IsFalse();

        async Task AssertCurrentUserAsync(string token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/user");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using HttpResponseMessage response = await client.SendAsync(request, CancellationToken);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using JsonDocument body = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(CancellationToken), cancellationToken: CancellationToken);
            await Assert.That(body.RootElement.GetProperty("id").GetGuid()).IsEqualTo(credential.Receipt.LocalSubjectId);
        }

        async Task CompletePrivateReplacementAsync(Guid operationId, string password)
        {
            Snapshot current = await ReadAsync(factory, credential);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var authority = new LocalCredentialReplacementAuthority(
                subject: new LocalCredentialReplacementSubject(localSubjectId: credential.Receipt.LocalSubjectId,
                    operationId: operationId, securityStamp: current.SecurityStamp!),
                issuedAtUtc: now, expiresAtUtc: now.AddMinutes(5));
            await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
            await Assert.That(await scope.ServiceProvider.GetRequiredService<ILocalCredentialAdministration>().ReplaceAsync(
                new LocalCredentialReplacementRequest(authority: authority, newPassword: password), CancellationToken))
                .IsEqualTo(LocalCredentialReplacementOutcome.Replaced);
        }

        async Task<HttpResponseMessage> SynchronizeAsync(string token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/User/sync");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await client.SendAsync(request, CancellationToken);
        }
    }

    [Test]
    public async Task StaleVerifiedBearerCannotRestoreRevokedVerificationWhenInstanceEmailDeliveryIsOff()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using HttpClient client = CreateClient(factory);
        LocalAuthRequestDto login = await factory.SeedLocalUserAsync(emailConfirmed: true);
        string oldBearer = await LoginForAccessTokenAsync(client, login);
        Guid userId;
        await using (AsyncServiceScope lookup = factory.Services.CreateAsyncScope())
            userId = (await lookup.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>()
                .FindByEmailAsync(login.Identifier))!.Id;
        using (HttpResponseMessage positive = await SynchronizeAsync(oldBearer))
            await Assert.That(positive.StatusCode).IsEqualTo(HttpStatusCode.OK);

        await using (ExploreDbContext mutation = factory.CreateDatabase())
        {
            await mutation.LocalIdentityUsers.Where(user => user.Id == userId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.EmailConfirmed, false), CancellationToken);
            await mutation.Users.Where(user => user.Id == userId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.EmailVerified, false), CancellationToken);
            SystemSetting? policy = await mutation.SystemSettings.SingleOrDefaultAsync(
                setting => setting.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled, CancellationToken);
            if (policy is null)
                mutation.SystemSettings.Add(new SystemSetting
                {
                    Id = Guid.CreateVersion7(), SettingKey = GovernanceSettingKeys.Email.DeliveryEnabled,
                    Value = "false", ValueType = SettingValueType.Boolean, CreatedAt = DateTime.UtcNow
                });
            else policy.Value = "false";
            await mutation.SaveChangesAsync(CancellationToken);
        }

        using (HttpResponseMessage rejected = await SynchronizeAsync(oldBearer))
        {
            await using ExploreDbContext persisted = factory.CreateDatabase();
            await Assert.That((await persisted.Users.SingleAsync(user => user.Id == userId, CancellationToken)).EmailVerified).IsFalse();
            await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }

        string freshBearer = await LoginForAccessTokenAsync(client, login);
        using (HttpResponseMessage fresh = await SynchronizeAsync(freshBearer))
            await Assert.That(fresh.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await using (ExploreDbContext persisted = factory.CreateDatabase())
            await Assert.That((await persisted.Users.SingleAsync(user => user.Id == userId, CancellationToken)).EmailVerified).IsFalse();

        async Task<HttpResponseMessage> SynchronizeAsync(string token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/User/sync");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await client.SendAsync(request, CancellationToken);
        }
    }

    [Test]
    public async Task ReplacementChallengeCannotAuthenticateOrdinaryUserSynchronization()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using HttpClient client = CreateClient(factory);
        Credential credential = await SeedChangeRequiredAsync(factory);
        string challenge = await IssueChallengeAsync(client, credential);
        Snapshot before = await ReadAsync(factory, credential);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/User/sync");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", challenge);

        using HttpResponseMessage response = await client.SendAsync(request, CancellationToken);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await AssertUnchangedAsync(factory, credential, before);
    }

    [Test]
    public async Task ReplacementReturnsNoSessionAndCannotReplayThroughIdempotencyStorage()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using HttpClient client = CreateClient(factory);
        Credential credential = await SeedChangeRequiredAsync(factory);
        string challenge = await IssueChallengeAsync(client, credential);
        string replacement = NewPassword();
        string replayPassword = NewPassword();
        string key = Guid.CreateVersion7().ToString("N");
        Snapshot before = await ReadAsync(factory, credential);

        using HttpResponseMessage response = await ReplaceAsync(client: client, token: challenge, password: replacement, idempotencyKey: key);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That((await response.Content.ReadAsByteArrayAsync(CancellationToken)).Length).IsEqualTo(0);
        await Assert.That(response.Headers.Contains("Set-Cookie")).IsFalse();
        await Assert.That(response.Headers.CacheControl?.Private).IsEqualTo(true);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsEqualTo(true);
        Snapshot committed = await ReadAsync(factory, credential);
        await Assert.That(committed.Stage).IsEqualTo(LocalCredentialOperationStage.Replaced);
        await Assert.That(string.Equals(committed.PasswordHash, before.PasswordHash, StringComparison.Ordinal)).IsFalse();
        await Assert.That(string.Equals(committed.SecurityStamp, before.SecurityStamp, StringComparison.Ordinal)).IsFalse();
        await Assert.That(JsonSerializer.Deserialize<LocalCredentialStateMetadata>(committed.TokenValue!)!.State)
            .IsEqualTo(LocalCredentialState.Ready);
        string accessToken = await LoginForAccessTokenAsync(client,
            new LocalAuthRequestDto(Identifier: credential.Login.Identifier, Password: replacement));
        await Assert.That(string.IsNullOrEmpty(accessToken)).IsFalse();
        Snapshot beforeReplay = await ReadAsync(factory, credential);

        using HttpResponseMessage replay = await ReplaceAsync(client: client, token: challenge, password: replayPassword, idempotencyKey: key);

        await Assert.That(replay.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await AssertUnchangedAsync(factory, credential, beforeReplay);
        await Assert.That(await PasswordIsValidAsync(factory, credential, replacement)).IsTrue();
        await Assert.That(await PasswordIsValidAsync(factory, credential, replayPassword)).IsFalse();
        await Assert.That(await PasswordIsValidAsync(factory, credential, credential.Login.Password)).IsFalse();
        await using ExploreDbContext stored = factory.CreateDatabase();
        await Assert.That(await stored.Set<IdempotencyRecord>().AnyAsync(record => record.Key == key, CancellationToken)).IsFalse();
    }

    [Test]
    public async Task SamePasswordValidationPreservesChallengeForPrivateReplacement()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using HttpClient client = CreateClient(factory);
        Credential credential = await SeedChangeRequiredAsync(factory);
        string challenge = await IssueChallengeAsync(client, credential);
        Snapshot before = await ReadAsync(factory, credential);

        using HttpResponseMessage invalid = await ReplaceAsync(client: client, token: challenge, password: credential.Login.Password);

        await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await AssertUnchangedAsync(factory, credential, before);
        using HttpResponseMessage corrected = await ReplaceAsync(client: client, token: challenge, password: NewPassword());
        await Assert.That(corrected.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task InvalidPasswordPolicyPreservesChallengeForCorrectedRequest()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using HttpClient client = CreateClient(factory);
        Credential credential = await SeedChangeRequiredAsync(factory);
        string challenge = await IssueChallengeAsync(client, credential);
        Snapshot before = await ReadAsync(factory, credential);

        using HttpResponseMessage invalid = await ReplaceAsync(client: client, token: challenge, password: NewPassword()[..11]);

        await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await AssertUnchangedAsync(factory, credential, before);
        using HttpResponseMessage corrected = await ReplaceAsync(client: client, token: challenge, password: NewPassword());
        await Assert.That(corrected.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
    }

    [Test]
    [Arguments(InvalidChallenge.Expired)]
    [Arguments(InvalidChallenge.TamperedSignature)]
    [Arguments(InvalidChallenge.WrongPurpose)]
    [Arguments(InvalidChallenge.WrongAudience)]
    [Arguments(InvalidChallenge.WrongTokenType)]
    [Arguments(InvalidChallenge.DuplicateOperationClaim)]
    [Arguments(InvalidChallenge.MissingSecurityStamp)]
    [Arguments(InvalidChallenge.MultipleAudiences)]
    [Arguments(InvalidChallenge.LifetimeTooLong)]
    [Arguments(InvalidChallenge.FutureIssuedAt)]
    [Arguments(InvalidChallenge.MalformedIssuedAt)]
    [Arguments(InvalidChallenge.NoncanonicalSubject)]
    [Arguments(InvalidChallenge.EmptyGuidSubject)]
    [Arguments(InvalidChallenge.ForbiddenEmail)]
    [Arguments(InvalidChallenge.ForbiddenRole)]
    [Arguments(InvalidChallenge.DuplicateJsonOperation)]
    public async Task InvalidChallengeCannotMutateNativeCredentialAuthority(InvalidChallenge defect)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using HttpClient client = CreateClient(factory);
        Credential credential = await SeedChangeRequiredAsync(factory);
        string original = await IssueChallengeAsync(client, credential);
        string challenge = await CreateInvalidChallengeAsync(factory: factory, original: original, defect: defect);
        Snapshot before = await ReadAsync(factory, credential);

        using HttpResponseMessage denied = await ReplaceAsync(client: client, token: challenge, password: NewPassword());

        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await AssertUnchangedAsync(factory, credential, before);
    }

    [Test]
    public async Task ChallengeWithIndependentlyRotatedSecurityStampCannotReplaceCredential()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using HttpClient client = CreateClient(factory);
        Credential credential = await SeedChangeRequiredAsync(factory);
        string challenge = await IssueChallengeAsync(client, credential);
        await using (AsyncServiceScope mutation = factory.Services.CreateAsyncScope())
        {
            var manager = mutation.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
            LocalIdentityUser user = (await manager.FindByIdAsync(credential.Receipt.LocalSubjectId.ToString("D")))!;
            IdentityResult rotated = await manager.UpdateSecurityStampAsync(user).WaitAsync(CancellationToken);
            await Assert.That(rotated.Succeeded).IsTrue();
        }
        Snapshot before = await ReadAsync(factory, credential);

        using HttpResponseMessage denied = await ReplaceAsync(client: client, token: challenge, password: NewPassword());

        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await AssertUnchangedAsync(factory, credential, before);
    }

    [Test]
    [Arguments(InvalidReplacementBody.MissingPassword)]
    [Arguments(InvalidReplacementBody.BlankPassword)]
    [Arguments(InvalidReplacementBody.TooLongPassword)]
    [Arguments(InvalidReplacementBody.UnknownAccount)]
    [Arguments(InvalidReplacementBody.UnknownAuthority)]
    public async Task InvalidBodyCannotSupplyAuthorityOrConsumeValidChallenge(InvalidReplacementBody defect)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using HttpClient client = CreateClient(factory);
        Credential credential = await SeedChangeRequiredAsync(factory);
        string challenge = await IssueChallengeAsync(client, credential);
        Snapshot before = await ReadAsync(factory, credential);
        string correctedPassword = NewPassword();
        object body = defect switch
        {
            InvalidReplacementBody.MissingPassword => new { },
            InvalidReplacementBody.BlankPassword => new { newPassword = " " },
            InvalidReplacementBody.TooLongPassword => new { newPassword = $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(64))}"[..129] },
            InvalidReplacementBody.UnknownAccount => new { newPassword = correctedPassword, userId = Guid.CreateVersion7() },
            InvalidReplacementBody.UnknownAuthority => new { newPassword = correctedPassword, authority = new { operationId = Guid.CreateVersion7() } },
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, ReplacementPath) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", challenge);

        using HttpResponseMessage denied = await client.SendAsync(request, CancellationToken);

        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await AssertUnchangedAsync(factory, credential, before);
        using HttpResponseMessage corrected = await ReplaceAsync(client: client, token: challenge, password: correctedPassword);
        await Assert.That(corrected.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
    }

    private static CancellationToken CancellationToken => TestContext.Current!.Execution.CancellationToken;
    private static string NewPassword() => $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(24))}";
    private static HttpClient CreateClient(LocalAdmissionWebApplicationFactory factory) => factory.CreateClient(
        new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    private static async Task<HttpResponseMessage> ReplaceAsync(
        HttpClient client, string token, string password, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ReplacementPath)
        {
            Content = JsonContent.Create(new { newPassword = password })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<string> IssueChallengeAsync(HttpClient client, Credential credential)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/local/login", credential.Login, CancellationToken);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using JsonDocument body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(CancellationToken), cancellationToken: CancellationToken);
        await Assert.That(body.RootElement.GetProperty("success").GetBoolean()).IsFalse();
        await Assert.That(body.RootElement.TryGetProperty("token", out _)).IsFalse();
        string token = body.RootElement.GetProperty("replacementChallenge").GetProperty("token").GetString()!;
        await Assert.That(string.IsNullOrWhiteSpace(token)).IsFalse();
        return token;
    }

    private static async Task<string> LoginForAccessTokenAsync(HttpClient client, LocalAuthRequestDto login)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/local/login", login, CancellationToken);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using JsonDocument body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(CancellationToken), cancellationToken: CancellationToken);
        await Assert.That(body.RootElement.GetProperty("success").GetBoolean()).IsTrue();
        await Assert.That(body.RootElement.TryGetProperty("replacementChallenge", out _)).IsFalse();
        return body.RootElement.GetProperty("token").GetString()!;
    }

    private static async Task<string> CreateInvalidChallengeAsync(
        LocalAdmissionWebApplicationFactory factory, string original, InvalidChallenge defect)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        SecretResolutionResult resolution = await scope.ServiceProvider.GetRequiredService<ISecretResolver>().ResolveAsync(
            SecretDefinitionRegistry.Keys.Authentication.LocalJwtKey, tenantId: null, cancellationToken: CancellationToken);
        await Assert.That(resolution.IsResolved).IsTrue();
        byte[] key = Convert.FromBase64String(resolution.Value!);
        var handler = new JwtSecurityTokenHandler();
        JwtSecurityToken issued = handler.ReadJwtToken(original);
        JwtPayload payload = JwtPayload.Deserialize(issued.Payload.SerializeToJson());
        string tokenType = LocalCredentialChallengeToken.TokenType;
        switch (defect)
        {
            case InvalidChallenge.Expired:
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                payload[JwtRegisteredClaimNames.Iat] = now - 360;
                payload[JwtRegisteredClaimNames.Nbf] = now - 360;
                payload[JwtRegisteredClaimNames.Exp] = now - 60;
                break;
            case InvalidChallenge.TamperedSignature:
                key = RandomNumberGenerator.GetBytes(64);
                break;
            case InvalidChallenge.WrongPurpose:
                payload[LocalCredentialChallengeToken.PurposeClaim] = "ordinary-login";
                break;
            case InvalidChallenge.WrongAudience:
                payload[JwtRegisteredClaimNames.Aud] = LocalIdentityOptions.Audience;
                break;
            case InvalidChallenge.WrongTokenType:
                tokenType = "JWT";
                break;
            case InvalidChallenge.DuplicateOperationClaim:
                string operation = issued.Claims.Single(claim => claim.Type == LocalCredentialChallengeToken.OperationIdClaim).Value;
                payload[LocalCredentialChallengeToken.OperationIdClaim] = new[] { operation, operation };
                break;
            case InvalidChallenge.MissingSecurityStamp:
                payload.Remove(LocalCredentialChallengeToken.SecurityStampClaim);
                break;
            case InvalidChallenge.MultipleAudiences:
                payload[JwtRegisteredClaimNames.Aud] = new[] { LocalCredentialChallengeToken.Audience, LocalIdentityOptions.Audience };
                break;
            case InvalidChallenge.LifetimeTooLong:
                long issuedAt = long.Parse(issued.Claims.Single(claim => claim.Type == JwtRegisteredClaimNames.Iat).Value, CultureInfo.InvariantCulture);
                payload[JwtRegisteredClaimNames.Exp] = issuedAt + 301;
                break;
            case InvalidChallenge.FutureIssuedAt:
                long future = DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds();
                payload[JwtRegisteredClaimNames.Iat] = future;
                payload[JwtRegisteredClaimNames.Nbf] = future;
                payload[JwtRegisteredClaimNames.Exp] = future + 300;
                break;
            case InvalidChallenge.MalformedIssuedAt:
                payload[JwtRegisteredClaimNames.Iat] = "not-an-integer";
                break;
            case InvalidChallenge.NoncanonicalSubject:
                payload[JwtRegisteredClaimNames.Sub] = Guid.Parse(issued.Subject).ToString("N");
                break;
            case InvalidChallenge.EmptyGuidSubject:
                payload[JwtRegisteredClaimNames.Sub] = Guid.Empty.ToString("D");
                break;
            case InvalidChallenge.ForbiddenEmail:
                payload[JwtRegisteredClaimNames.Email] = $"forbidden-{Guid.CreateVersion7():N}@example.test";
                break;
            case InvalidChallenge.ForbiddenRole:
                payload["roles"] = new[] { "Admin" };
                break;
            case InvalidChallenge.DuplicateJsonOperation:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(defect));
        }
        var header = new JwtHeader(new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256))
        {
            ["typ"] = tokenType
        };
        if (defect == InvalidChallenge.DuplicateJsonOperation)
        {
            using JsonDocument originalPayload = JsonDocument.Parse(payload.SerializeToJson());
            using var duplicated = new MemoryStream();
            using (var writer = new Utf8JsonWriter(duplicated))
            {
                writer.WriteStartObject();
                foreach (JsonProperty property in originalPayload.RootElement.EnumerateObject())
                {
                    property.WriteTo(writer);
                    if (property.NameEquals(LocalCredentialChallengeToken.OperationIdClaim))
                        property.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            string signedPayload = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(header.SerializeToJson()))
                + "." + Base64UrlEncoder.Encode(duplicated.ToArray());
            byte[] signature = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(signedPayload));
            return signedPayload + "." + Base64UrlEncoder.Encode(signature);
        }
        return handler.WriteToken(new JwtSecurityToken(header, payload));
    }

    private static async Task<Credential> SeedChangeRequiredAsync(LocalAdmissionWebApplicationFactory factory)
    {
        Guid initiatorId;
        await using (ExploreDbContext stored = factory.CreateDatabase())
            initiatorId = (await stored.InstanceBootstrapStates.SingleAsync(CancellationToken)).CompletedByUserId!.Value;
        string email = $"replacement-{Guid.CreateVersion7():N}@example.test";
        LocalCredentialCreateResult created;
        await using (AsyncServiceScope create = factory.Services.CreateAsyncScope())
            created = await create.ServiceProvider.GetRequiredService<ILocalCredentialAdministration>().CreatePendingAsync(
                new LocalCredentialCreateRequest(operationId: Guid.CreateVersion7(), initiatingApplicationUserId: initiatorId,
                    email: email, firstName: "Private", lastName: "Replacement"), CancellationToken);
        await Assert.That(created.Outcome).IsEqualTo(LocalCredentialCreateOutcome.Created);
        LocalCredentialOperationReceipt receipt = created.Receipt!;
        await using (ExploreDbContext bind = factory.CreateDatabase())
        {
            var user = new User
            {
                Id = receipt.LocalSubjectId, EmailVerified = true, CreatedAt = DateTime.UtcNow,
                Pii = new UserPii { Email = email, FirstName = "Private", LastName = "Replacement" }
            };
            bind.Actors.Add(new Actor
            {
                Id = receipt.PersonalActorId, UserId = user.Id, User = user,
                ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!,
                Pii = new ActorPii { DisplayName = "Private Replacement" }, CreatedAt = DateTime.UtcNow
            });
            bind.UserExternalLogins.Add(new UserExternalLogin
            {
                Id = receipt.ExternalLoginId, UserId = user.Id, User = user,
                AuthenticationProviderId = (int)AuthenticationProviderKind.Local, AuthenticationProvider = null!,
                ProviderKey = user.Id.ToString("D"), CreatedAt = DateTime.UtcNow
            });
            await bind.SaveChangesAsync(CancellationToken);
        }
        await using (AsyncServiceScope activate = factory.Services.CreateAsyncScope())
        {
            var administration = activate.ServiceProvider.GetRequiredService<ILocalCredentialAdministration>();
            LocalCredentialProvisioningSnapshot pending = (await administration.ReadProvisioningAsync(receipt.OperationId, CancellationToken))!;
            LocalCredentialActivationOutcome activated = await administration.ActivateChangeRequiredAsync(
                new LocalCredentialActivationRequest(operationId: receipt.OperationId,
                    expectedOperationConcurrencyStamp: pending.OperationConcurrencyStamp), CancellationToken);
            await Assert.That(activated).IsEqualTo(LocalCredentialActivationOutcome.Activated);
        }
        return new Credential(Login: new LocalAuthRequestDto(Identifier: email, Password: created.TemporaryPassword!), Receipt: receipt);
    }

    private static async Task<bool> PasswordIsValidAsync(LocalAdmissionWebApplicationFactory factory, Credential credential, string password)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
        LocalIdentityUser user = (await manager.FindByIdAsync(credential.Receipt.LocalSubjectId.ToString("D")))!;
        return await manager.CheckPasswordAsync(user, password).WaitAsync(CancellationToken);
    }

    private static async Task<Snapshot> ReadAsync(LocalAdmissionWebApplicationFactory factory, Credential credential)
    {
        await using ExploreDbContext stored = factory.CreateDatabase();
        LocalIdentityUser user = await stored.LocalIdentityUsers.AsNoTracking()
            .SingleAsync(row => row.Id == credential.Receipt.LocalSubjectId, CancellationToken);
        LocalIdentityCredentialOperation operation = await stored.Set<LocalIdentityCredentialOperation>().AsNoTracking()
            .SingleAsync(row => row.Id == credential.Receipt.OperationId, CancellationToken);
        string? token = await stored.Set<IdentityUserToken<Guid>>().AsNoTracking()
            .Where(row => row.UserId == user.Id && row.LoginProvider == LocalCredentialStateMetadata.TokenLoginProvider
                && row.Name == LocalCredentialStateMetadata.TokenName).Select(row => row.Value).SingleAsync(CancellationToken);
        return new Snapshot(PasswordHash: user.PasswordHash, SecurityStamp: user.SecurityStamp, ConcurrencyStamp: user.ConcurrencyStamp,
            TokenValue: token, Stage: operation.Stage, OperationStamp: operation.ConcurrencyStamp,
            OperationUpdatedAt: operation.UpdatedAt, UserUpdatedAt: user.UpdatedAt);
    }

    private static async Task AssertUnchangedAsync(LocalAdmissionWebApplicationFactory factory, Credential credential, Snapshot before)
    {
        Snapshot after = await ReadAsync(factory, credential);
        await Assert.That(string.Equals(after.PasswordHash, before.PasswordHash, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(after.SecurityStamp, before.SecurityStamp, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(after.ConcurrencyStamp, before.ConcurrencyStamp, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(after.TokenValue, before.TokenValue, StringComparison.Ordinal)).IsTrue();
        await Assert.That(after.Stage).IsEqualTo(before.Stage);
        await Assert.That(after.OperationStamp == before.OperationStamp).IsTrue();
        await Assert.That(after.OperationUpdatedAt).IsEqualTo(before.OperationUpdatedAt);
        await Assert.That(after.UserUpdatedAt).IsEqualTo(before.UserUpdatedAt);
    }

    private sealed record Credential(LocalAuthRequestDto Login, LocalCredentialOperationReceipt Receipt)
    {
        public override string ToString() => nameof(Credential);
    }

    private sealed record Snapshot(string? PasswordHash, string? SecurityStamp, string? ConcurrencyStamp,
        string? TokenValue, LocalCredentialOperationStage Stage, Guid OperationStamp, DateTime? OperationUpdatedAt, DateTime? UserUpdatedAt)
    {
        public override string ToString() => nameof(Snapshot);
    }
}
