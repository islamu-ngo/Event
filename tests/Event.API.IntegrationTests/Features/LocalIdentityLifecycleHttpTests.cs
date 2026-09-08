// ABOUTME: Exercises native Local lifecycle HTTP admission and one-use authority through real SQLite Identity.
// ABOUTME: Verifies private responses and exact Domain mirror synchronization without issuing sessions on consume.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Identity;
using Explore.Application.DTOs.User;
using Explore.Application.Features.Authentication.Local.Handlers.Commands;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Features.Users.Requests.Queries;
using MediatR;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Authentication;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.API.IntegrationTests.Features;

[NotInParallel]
public sealed class LocalIdentityLifecycleHttpTests
{
    private const string VerificationPath = "/api/auth/local/email-verifications";
    private const string RecoveryPath = "/api/auth/local/password-recoveries";
    private const string PasswordPath = "/api/auth/local/password";
    private static CancellationToken CancellationToken => TestContext.Current!.Execution.CancellationToken;

    [Test]
    public async Task MissingRecoveryTargetIsAcceptedWithoutSessionOrAccountCreation()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });
        await using ExploreDbContext before = factory.CreateDatabase();
        int users = await before.Users.CountAsync(CancellationToken);
        using HttpResponseMessage response = await client.PostAsJsonAsync(RecoveryPath,
            new { identifier = $"missing-{Guid.CreateVersion7():N}@example.test" }, CancellationToken);

        await AssertPrivateEmptyAsync(response, HttpStatusCode.Accepted);
        await using ExploreDbContext after = factory.CreateDatabase();
        await Assert.That(await after.Users.CountAsync(CancellationToken)).IsEqualTo(users);
    }

    [Test]
    public async Task VerificationChangesOnlyExactNativeAndDomainBindingWithoutSigningIn()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(verified: false);
        var before = await fixture.ReadMirrorAsync();
        using (HttpResponseMessage blocked = await fixture.Client.PostAsJsonAsync("/api/auth/local/login", fixture.Login, CancellationToken))
            await Assert.That(blocked.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        using (HttpResponseMessage requested = await fixture.Client.PostAsJsonAsync(VerificationPath,
            new LocalEmailVerificationRequestDto { Identifier = fixture.Login.Identifier }, CancellationToken))
            await AssertPrivateEmptyAsync(requested, HttpStatusCode.Accepted);
        LocalIdentityLifecycleTransport handoff = await fixture.DrainOneAsync();
        await Assert.That(handoff.ExpiresAtUtc - fixture.Clock.GetUtcNow()).IsEqualTo(TimeSpan.FromMinutes(30));
        await Assert.That((await fixture.ReadIdentityAsync()).Verified).IsFalse();
        fixture.Client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.CreateVersion7().ToString("D"));

        using (HttpResponseMessage consumed = await fixture.Client.PostAsJsonAsync(VerificationPath + "/consume", EmailBody(handoff), CancellationToken))
            await AssertPrivateEmptyAsync(consumed, HttpStatusCode.NoContent);
        var native = await fixture.ReadIdentityAsync();
        await Assert.That(native.Verified).IsTrue();
        await Assert.That(await fixture.ReadMirrorAsync()).IsEqualTo(before with { Verified = true });
        await AssertSynchronizedAsync(fixture, handoff.Operation);

        using (HttpResponseMessage replay = await fixture.Client.PostAsJsonAsync(VerificationPath + "/consume", EmailBody(handoff), CancellationToken))
            await AssertPrivateEmptyAsync(replay, HttpStatusCode.NoContent);
        await Assert.That(await fixture.ReadIdentityAsync()).IsEqualTo(native);
        using (HttpResponseMessage tampered = await fixture.Client.PostAsJsonAsync(VerificationPath + "/consume",
            EmailBody(handoff) with { Token = Convert.ToBase64String(Guid.NewGuid().ToByteArray()) }, CancellationToken))
            await AssertProblemAsync(tampered, HttpStatusCode.BadRequest);
        await fixture.SignInAsync();
    }

    [Test]
    public async Task ProposedAddressRequiresCurrentLocalSessionAndConsumesItsExactPendingAddress()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        string accessToken = await fixture.SignInAsync();
        string proposed = $"changed-{Guid.CreateVersion7():N}@example.test";
        var before = await fixture.ReadMirrorAsync();
        using (HttpResponseMessage anonymous = await fixture.Client.PostAsJsonAsync(VerificationPath,
            new LocalEmailVerificationRequestDto { ProposedEmail = proposed }, CancellationToken))
            await AssertProblemAsync(anonymous, HttpStatusCode.Unauthorized);
        using (HttpResponseMessage selected = await fixture.PostAuthorizedAsync(VerificationPath,
            new LocalEmailVerificationRequestDto { Identifier = fixture.Login.Identifier }, accessToken))
            await AssertProblemAsync(selected, HttpStatusCode.BadRequest);
        using (HttpResponseMessage accepted = await fixture.PostAuthorizedAsync(VerificationPath,
            new LocalEmailVerificationRequestDto { ProposedEmail = proposed }, accessToken))
            await AssertPrivateEmptyAsync(accepted, HttpStatusCode.Accepted);
        LocalIdentityLifecycleTransport handoff = await fixture.DrainOneAsync();
        await Assert.That(handoff.Operation.Purpose).IsEqualTo(LocalIdentityLifecyclePurpose.EmailChange);
        await Assert.That(handoff.Address).IsEqualTo(proposed);
        await Assert.That(await fixture.ReadMirrorAsync()).IsEqualTo(before);
        await Assert.That((await fixture.ReadIdentityAsync()).Email).IsEqualTo(before.Email);
        using (HttpResponseMessage confused = await fixture.Client.PostAsJsonAsync(VerificationPath + "/consume",
            EmailBody(handoff) with { ExternalLoginId = Guid.CreateVersion7() }, CancellationToken))
            await AssertProblemAsync(confused, HttpStatusCode.BadRequest);
        using (HttpResponseMessage confirmed = await fixture.Client.PostAsJsonAsync(VerificationPath + "/consume", EmailBody(handoff), CancellationToken))
            await AssertPrivateEmptyAsync(confirmed, HttpStatusCode.NoContent);
        await Assert.That(await fixture.ReadMirrorAsync()).IsEqualTo(before with { Email = proposed });
        await Assert.That((await fixture.ReadIdentityAsync()).Email).IsEqualTo(proposed);
        await AssertRevokedAsync(fixture, accessToken);
        string currentToken = await fixture.SignInAsync(new LocalAuthRequestDto(proposed, fixture.Login.Password));
        string latestAddress = $"latest-{Guid.CreateVersion7():N}@example.test";
        using (HttpResponseMessage next = await fixture.PostAuthorizedAsync(VerificationPath,
            new LocalEmailVerificationRequestDto { ProposedEmail = latestAddress }, currentToken))
            await AssertPrivateEmptyAsync(next, HttpStatusCode.Accepted);
        LocalIdentityLifecycleTransport latest = await fixture.DrainOneAsync();
        using (HttpResponseMessage changedAgain = await fixture.Client.PostAsJsonAsync(VerificationPath + "/consume", EmailBody(latest), CancellationToken))
            await AssertPrivateEmptyAsync(changedAgain, HttpStatusCode.NoContent);
        using (HttpResponseMessage stale = await fixture.Client.PostAsJsonAsync(VerificationPath + "/consume", EmailBody(handoff), CancellationToken))
            await AssertProblemAsync(stale, HttpStatusCode.BadRequest);
        await Assert.That(await fixture.ReadMirrorAsync()).IsEqualTo(before with { Email = latestAddress });
    }

    [Test]
    public async Task RecoveryDeduplicatesDeliveryAndReplayCannotReplacePasswordAgain()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        var before = await fixture.ReadMirrorAsync();
        var request = new LocalPasswordRecoveryRequestDto { Identifier = fixture.Login.Identifier };
        for (int attempt = 0; attempt < 2; attempt++)
        {
            using HttpResponseMessage accepted = await fixture.Client.PostAsJsonAsync(RecoveryPath, request, CancellationToken);
            await AssertPrivateEmptyAsync(accepted, HttpStatusCode.Accepted);
        }
        LocalIdentityLifecycleTransport handoff = await fixture.DrainOneAsync();
        await Assert.That(handoff.ExpiresAtUtc - fixture.Clock.GetUtcNow()).IsEqualTo(TimeSpan.FromMinutes(15));
        await using (var scope = fixture.Host.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<LocalIdentityLifecycleDeliveryProcessor>().DrainAsync(CancellationToken);
        await Assert.That(fixture.Smtp.Handoffs.Count).IsEqualTo(1);
        string password = LocalIdentityLifecycleHttpFixture.NewPassword();
        using (HttpResponseMessage consumed = await fixture.Client.PostAsJsonAsync(RecoveryPath + "/consume", RecoveryBody(handoff, password), CancellationToken))
            await AssertPrivateEmptyAsync(consumed, HttpStatusCode.NoContent);
        var consumedState = await fixture.ReadIdentityAsync();
        using (HttpResponseMessage replay = await fixture.Client.PostAsJsonAsync(RecoveryPath + "/consume",
            RecoveryBody(handoff, LocalIdentityLifecycleHttpFixture.NewPassword()), CancellationToken))
            await AssertPrivateEmptyAsync(replay, HttpStatusCode.NoContent);
        await Assert.That(await fixture.ReadIdentityAsync()).IsEqualTo(consumedState);
        await Assert.That(await fixture.ReadMirrorAsync()).IsEqualTo(before);
        using (HttpResponseMessage oldPassword = await fixture.Client.PostAsJsonAsync("/api/auth/local/login", fixture.Login, CancellationToken))
            await Assert.That(oldPassword.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await fixture.SignInAsync(new LocalAuthRequestDto(fixture.Login.Identifier, password));
    }

    [Test]
    public async Task ExternalIdentityMirrorFailureRetriesOnlySynchronizationWithOriginalToken()
    {
        var failure = new FailMirrorSaveOnce();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            topology: IdentityDatabaseTopology.External, interceptor: failure);
        using (HttpResponseMessage requested = await fixture.Client.PostAsJsonAsync(RecoveryPath,
            new LocalPasswordRecoveryRequestDto { Identifier = fixture.Login.Identifier }, CancellationToken))
            await AssertPrivateEmptyAsync(requested, HttpStatusCode.Accepted);
        LocalIdentityLifecycleTransport handoff = await fixture.DrainOneAsync();
        string password = LocalIdentityLifecycleHttpFixture.NewPassword();
        var original = await fixture.ReadIdentityAsync();
        failure.Arm();
        using (HttpResponseMessage failed = await fixture.Client.PostAsJsonAsync(RecoveryPath + "/consume", RecoveryBody(handoff, password), CancellationToken))
            await AssertProblemAsync(failed, HttpStatusCode.Conflict);
        await Assert.That(failure.Triggered).IsTrue();
        var mutated = await fixture.ReadIdentityAsync();
        await Assert.That(mutated.PasswordHash == original.PasswordHash).IsFalse();
        using (HttpResponseMessage pointerOnly = await fixture.Client.PostAsJsonAsync(RecoveryPath + "/consume",
            RecoveryBody(handoff, password) with { Token = Convert.ToBase64String(Guid.NewGuid().ToByteArray()) }, CancellationToken))
            await AssertProblemAsync(pointerOnly, HttpStatusCode.BadRequest);
        using (HttpResponseMessage retried = await fixture.Client.PostAsJsonAsync(RecoveryPath + "/consume",
            RecoveryBody(handoff, LocalIdentityLifecycleHttpFixture.NewPassword()), CancellationToken))
            await AssertPrivateEmptyAsync(retried, HttpStatusCode.NoContent);
        await Assert.That(await fixture.ReadIdentityAsync()).IsEqualTo(mutated);
        await AssertSynchronizedAsync(fixture, handoff.Operation);
        await fixture.SignInAsync(new LocalAuthRequestDto(fixture.Login.Identifier, password));
    }

    [Test]
    public async Task TrustedRepairAfterExpirySynchronizesMirrorAndEvictsCachedProfileWithoutPublicPointerAuthority()
    {
        var failure = new FailMirrorSaveOnce();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(verified: false, interceptor: failure);
        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            UserDto cached = await scope.ServiceProvider.GetRequiredService<ISender>().Send(
                new GetUserRequest { UserId = fixture.Binding.LocalSubjectId }, CancellationToken);
            await Assert.That(cached.EmailVerified).IsFalse();
        }
        using (HttpResponseMessage accepted = await fixture.Client.PostAsJsonAsync(VerificationPath,
            new LocalEmailVerificationRequestDto { Identifier = fixture.Login.Identifier }, CancellationToken))
            await AssertPrivateEmptyAsync(accepted, HttpStatusCode.Accepted);
        LocalIdentityLifecycleTransport handoff = await fixture.DrainOneAsync();
        failure.Arm();
        using (HttpResponseMessage failed = await fixture.Client.PostAsJsonAsync(VerificationPath + "/consume", EmailBody(handoff), CancellationToken))
            await AssertProblemAsync(failed, HttpStatusCode.Conflict);
        await Assert.That(failure.Triggered).IsTrue();
        var identity = await fixture.ReadIdentityAsync();
        await Assert.That(identity.Verified).IsTrue();
        await Assert.That((await fixture.ReadMirrorAsync()).Verified).IsFalse();
        fixture.Clock.Advance(TimeSpan.FromMinutes(31));
        using (HttpResponseMessage expired = await fixture.Client.PostAsJsonAsync(VerificationPath + "/consume", EmailBody(handoff), CancellationToken))
            await AssertProblemAsync(expired, HttpStatusCode.BadRequest);
        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            var repaired = await scope.ServiceProvider.GetRequiredService<ISender>().Send(
                new ReconcileLocalIdentityLifecycleMirrorCommand(handoff.Operation), CancellationToken);
            await Assert.That(repaired.IsSuccess).IsTrue();
            UserDto refreshed = await scope.ServiceProvider.GetRequiredService<ISender>().Send(
                new GetUserRequest { UserId = fixture.Binding.LocalSubjectId }, CancellationToken);
            await Assert.That(refreshed.EmailVerified).IsTrue();
        }
        await Assert.That(await fixture.ReadIdentityAsync()).IsEqualTo(identity);
        await Assert.That((await fixture.ReadMirrorAsync()).Verified).IsTrue();
        await AssertSynchronizedAsync(fixture, handoff.Operation);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExpiredOperationsCannotMutateAuthorityOrMirror(bool recovery)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(verified: recovery);
        string path = recovery ? RecoveryPath : VerificationPath;
        using (HttpResponseMessage accepted = await fixture.Client.PostAsJsonAsync(path,
            new { identifier = fixture.Login.Identifier }, CancellationToken))
            await AssertPrivateEmptyAsync(accepted, HttpStatusCode.Accepted);
        LocalIdentityLifecycleTransport handoff = await fixture.DrainOneAsync();
        var identity = await fixture.ReadIdentityAsync();
        var mirror = await fixture.ReadMirrorAsync();
        fixture.Clock.Advance(TimeSpan.FromMinutes(recovery ? 15 : 30));
        using HttpResponseMessage expired = recovery
            ? await fixture.Client.PostAsJsonAsync(path + "/consume", RecoveryBody(handoff, LocalIdentityLifecycleHttpFixture.NewPassword()), CancellationToken)
            : await fixture.Client.PostAsJsonAsync(path + "/consume", EmailBody(handoff), CancellationToken);
        await AssertProblemAsync(expired, HttpStatusCode.BadRequest);
        await Assert.That(await fixture.ReadIdentityAsync()).IsEqualTo(identity);
        await Assert.That(await fixture.ReadMirrorAsync()).IsEqualTo(mirror);
    }

    [Test]
    public async Task IneligibleRecoveryNeverUsesMatchingDomainEmailAsLocalAuthority()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(verified: false);
        string externalEmail = $"external-{Guid.CreateVersion7():N}@example.test";
        await using (ExploreDbContext seed = fixture.Native.CreateDatabase())
        {
            var external = new User
            {
                Id = Guid.CreateVersion7(), Pii = new UserPii { Email = externalEmail, FirstName = "External", LastName = "Only" },
                EmailVerified = true
            };
            seed.Users.Add(external);
            seed.UserExternalLogins.Add(new UserExternalLogin
            {
                Id = Guid.CreateVersion7(), UserId = external.Id, User = external,
                AuthenticationProviderId = (int)AuthenticationProviderKind.Google,
                AuthenticationProvider = null!, ProviderKey = $"https://external.test|{Guid.CreateVersion7():D}"
            });
            await seed.SaveChangesAsync(CancellationToken);
        }
        var identity = await fixture.ReadIdentityAsync();
        foreach (string identifier in new[] { fixture.Login.Identifier, externalEmail, $"missing-{Guid.CreateVersion7():N}@example.test" })
        {
            using HttpResponseMessage response = await fixture.Client.PostAsJsonAsync(RecoveryPath,
                new LocalPasswordRecoveryRequestDto { Identifier = identifier }, CancellationToken);
            await AssertPrivateEmptyAsync(response, HttpStatusCode.Accepted);
        }
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<LocalIdentityLifecycleDeliveryProcessor>().DrainAsync(CancellationToken);
        await Assert.That(fixture.Smtp.Handoffs.Count).IsEqualTo(0);
        await Assert.That(await fixture.ReadIdentityAsync()).IsEqualTo(identity);
    }

    [Test]
    public async Task OrdinaryPasswordChangeRemainsProtectedAndWorksWithoutSmtp()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(emailEnabled: false);
        var body = new LocalPasswordChangeRequestDto
        {
            CurrentPassword = fixture.Login.Password, NewPassword = LocalIdentityLifecycleHttpFixture.NewPassword()
        };
        using (HttpResponseMessage anonymous = await fixture.Client.PostAsJsonAsync(PasswordPath, body, CancellationToken))
            await Assert.That(anonymous.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        string accessToken = await fixture.SignInAsync();
        var mirror = await fixture.ReadMirrorAsync();
        var original = await fixture.ReadIdentityAsync();
        using (HttpResponseMessage wrong = await fixture.PostAuthorizedAsync(PasswordPath,
            body with { CurrentPassword = LocalIdentityLifecycleHttpFixture.NewPassword() }, accessToken))
            await AssertProblemAsync(wrong, HttpStatusCode.BadRequest);
        using (HttpResponseMessage same = await fixture.PostAuthorizedAsync(PasswordPath,
            body with { NewPassword = fixture.Login.Password }, accessToken))
            await AssertProblemAsync(same, HttpStatusCode.BadRequest);
        await Assert.That(await fixture.ReadIdentityAsync()).IsEqualTo(original);
        using (HttpResponseMessage changed = await fixture.PostAuthorizedAsync(PasswordPath, body, accessToken))
            await AssertPrivateEmptyAsync(changed, HttpStatusCode.NoContent);
        await Assert.That((await fixture.ReadIdentityAsync()).PasswordHash == original.PasswordHash).IsFalse();
        await Assert.That(await fixture.ReadMirrorAsync()).IsEqualTo(mirror);
        await Assert.That(fixture.Smtp.Handoffs.Count).IsEqualTo(0);
        await AssertRevokedAsync(fixture, accessToken);
        await fixture.SignInAsync(new LocalAuthRequestDto(fixture.Login.Identifier, body.NewPassword));
    }

    [Test]
    public async Task DiscoveryUsesNativeCapabilityAndOrdinaryLocalBinding()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        using HttpResponseMessage discovery = await fixture.Client.GetAsync("/api/InstanceOnboarding/auth-provider-configuration", CancellationToken);
        await Assert.That(discovery.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using JsonDocument publicJson = JsonDocument.Parse(await discovery.Content.ReadAsStringAsync(CancellationToken));
        JsonElement publicLinks = publicJson.RootElement.GetProperty("_links");
        await AssertLinkAsync(publicLinks, "verify-email", VerificationPath);
        await AssertLinkAsync(publicLinks, "recover-password", RecoveryPath);
        await Assert.That(publicLinks.TryGetProperty("change-password", out _)).IsFalse();
        await Assert.That(publicJson.RootElement.TryGetProperty("primaryProviderId", out _)).IsTrue();
        string accessToken = await fixture.SignInAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/User");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using HttpResponseMessage current = await fixture.Client.SendAsync(request, CancellationToken);
        await Assert.That(current.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using JsonDocument currentJson = JsonDocument.Parse(await current.Content.ReadAsStringAsync(CancellationToken));
        JsonElement currentLinks = currentJson.RootElement.GetProperty("_links");
        await AssertLinkAsync(currentLinks, "change-password", PasswordPath);
        await AssertLinkAsync(currentLinks, "verify-email", VerificationPath);
    }

    [Test]
    public async Task DisabledEmailHidesEmailDiscoveryButNotCurrentPasswordChange()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(emailEnabled: false);
        using HttpResponseMessage discovery = await fixture.Client.GetAsync("/api/InstanceOnboarding/auth-provider-configuration", CancellationToken);
        using JsonDocument publicJson = JsonDocument.Parse(await discovery.Content.ReadAsStringAsync(CancellationToken));
        await Assert.That(publicJson.RootElement.GetProperty("_links").TryGetProperty("verify-email", out _)).IsFalse();
        await Assert.That(publicJson.RootElement.GetProperty("_links").TryGetProperty("recover-password", out _)).IsFalse();
        string accessToken = await fixture.SignInAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/User");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using HttpResponseMessage current = await fixture.Client.SendAsync(request, CancellationToken);
        using JsonDocument currentJson = JsonDocument.Parse(await current.Content.ReadAsStringAsync(CancellationToken));
        JsonElement links = currentJson.RootElement.GetProperty("_links");
        await AssertLinkAsync(links, "change-password", PasswordPath);
        await Assert.That(links.TryGetProperty("verify-email", out _)).IsFalse();
        await Assert.That(links.TryGetProperty("recover-password", out _)).IsFalse();
    }

    [Test]
    public async Task PublicInputsRejectExtraAuthorityOversizedValuesAndWrongPurpose()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(verified: false);
        using (HttpResponseMessage oversized = await fixture.Client.PostAsJsonAsync(RecoveryPath,
            new { identifier = new string('a', 257) }, CancellationToken))
            await AssertProblemAsync(oversized, HttpStatusCode.BadRequest);
        using (HttpResponseMessage authority = await fixture.Client.PostAsJsonAsync(RecoveryPath,
            new { identifier = fixture.Login.Identifier, userId = fixture.Binding.LocalSubjectId }, CancellationToken))
            await AssertProblemAsync(authority, HttpStatusCode.BadRequest);
        using (HttpResponseMessage requested = await fixture.Client.PostAsJsonAsync(VerificationPath,
            new LocalEmailVerificationRequestDto { Identifier = fixture.Login.Identifier }, CancellationToken))
            await AssertPrivateEmptyAsync(requested, HttpStatusCode.Accepted);
        LocalIdentityLifecycleTransport handoff = await fixture.DrainOneAsync();
        var identity = await fixture.ReadIdentityAsync();
        using (HttpResponseMessage wrongPurpose = await fixture.Client.PostAsJsonAsync(RecoveryPath + "/consume",
            RecoveryBody(handoff, LocalIdentityLifecycleHttpFixture.NewPassword()), CancellationToken))
            await AssertProblemAsync(wrongPurpose, HttpStatusCode.BadRequest);
        using (HttpResponseMessage oversizedToken = await fixture.Client.PostAsJsonAsync(VerificationPath + "/consume",
            EmailBody(handoff) with { Token = new string('a', 8193) }, CancellationToken))
            await AssertProblemAsync(oversizedToken, HttpStatusCode.BadRequest);
        await Assert.That(await fixture.ReadIdentityAsync()).IsEqualTo(identity);
    }

    [Test]
    public async Task PublicRecoveryUsesNativeNamedIpRatePolicy()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(rateLimiting: true);
        for (int attempt = 0; attempt < 10; attempt++)
        {
            using HttpResponseMessage accepted = await fixture.Client.PostAsJsonAsync(RecoveryPath,
                new { identifier = $"missing-{Guid.CreateVersion7():N}@example.test" }, CancellationToken);
            await AssertPrivateEmptyAsync(accepted, HttpStatusCode.Accepted);
        }
        using HttpResponseMessage limited = await fixture.Client.PostAsJsonAsync(RecoveryPath,
            new { identifier = $"missing-{Guid.CreateVersion7():N}@example.test" }, CancellationToken);
        await AssertProblemAsync(limited, HttpStatusCode.TooManyRequests);
    }

    private static LocalEmailConfirmationRequestDto EmailBody(LocalIdentityLifecycleTransport handoff) => new()
    {
        OperationId = handoff.Operation.OperationId, LocalSubjectId = handoff.Operation.LocalSubjectId,
        PersonalActorId = handoff.Operation.PersonalActorId, ExternalLoginId = handoff.Operation.ExternalLoginId,
        Purpose = handoff.Operation.Purpose, Generation = handoff.Operation.Generation, Token = handoff.Token
    };

    private static LocalPasswordRecoveryCompletionRequestDto RecoveryBody(LocalIdentityLifecycleTransport handoff, string password) => new()
    {
        OperationId = handoff.Operation.OperationId, LocalSubjectId = handoff.Operation.LocalSubjectId,
        PersonalActorId = handoff.Operation.PersonalActorId, ExternalLoginId = handoff.Operation.ExternalLoginId,
        Purpose = handoff.Operation.Purpose, Generation = handoff.Operation.Generation, Token = handoff.Token, NewPassword = password
    };

    private static async Task AssertPrivateEmptyAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        await Assert.That(response.StatusCode).IsEqualTo(expected);
        await Assert.That(await response.Content.ReadAsStringAsync(CancellationToken)).IsEqualTo(string.Empty);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(response.Headers.CacheControl?.Private).IsTrue();
        await Assert.That(response.Headers.Contains("Set-Cookie")).IsFalse();
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        await Assert.That(response.StatusCode).IsEqualTo(expected);
        await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("application/problem+json");
        using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        await Assert.That(problem.RootElement.GetProperty("status").GetInt32()).IsEqualTo((int)expected);
        await Assert.That(response.Headers.Contains("Set-Cookie")).IsFalse();
    }

    private static async Task AssertRevokedAsync(LocalIdentityLifecycleHttpFixture fixture, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/User");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage response = await fixture.Client.SendAsync(request, CancellationToken);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    private static async Task AssertSynchronizedAsync(LocalIdentityLifecycleHttpFixture fixture, LocalIdentityLifecyclePointer pointer)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        LocalIdentityLifecycleSynchronization? receipt = await scope.ServiceProvider.GetRequiredService<ILocalIdentityLifecycleStore>()
            .ReadSynchronizationAsync(pointer, CancellationToken);
        await Assert.That(receipt).IsNotNull();
        await Assert.That(receipt!.Synchronized).IsTrue();
        await Assert.That(receipt.Operation).IsEqualTo(pointer);
    }

    private static async Task AssertLinkAsync(JsonElement links, string relation, string path)
    {
        JsonElement link = links.GetProperty(relation);
        string href = link.GetProperty("href").GetString()!;
        await Assert.That(href.EndsWith(path, StringComparison.Ordinal)).IsTrue();
        await Assert.That(link.GetProperty("method").GetString()).IsEqualTo("POST");
    }

    private sealed class FailMirrorSaveOnce : SaveChangesInterceptor
    {
        private bool _armed;
        internal bool Triggered { get; private set; }
        internal void Arm() => _armed = true;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (_armed && eventData.Context!.ChangeTracker.Entries<User>().Any(entry => entry.State == EntityState.Modified))
            {
                _armed = false;
                Triggered = true;
                throw new InvalidOperationException("Controlled Domain mirror write failure.");
            }
            return ValueTask.FromResult(result);
        }
    }
}
