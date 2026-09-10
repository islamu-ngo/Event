using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Helpers;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.PrivacyErasure;
using Explore.Application.DTOs.User;
using Explore.Application.Features.Users.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Constants;
using Explore.Persistence;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
[ClassDataSource<ApiTestFixture>(Shared = SharedType.PerClass)]
public class UserControllerTests
{
    private readonly ApiTestFixture _fixture;
    private const string BaseUrl = "/api/user";

    public UserControllerTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    #region GET Endpoints

    [Test]
    public async Task GetCurrentUser_WithoutAuth_ShouldReturnUnauthorized()
    {
        // Act
        var response = await _fixture.Client.GetAsync(BaseUrl);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task GetUserOrganizations_WithoutAuth_ShouldReturnUnauthorized()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act
        var response = await _fixture.Client.GetAsync($"{BaseUrl}/{userId}/organizations");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task GetUserOrganizations_WhenRequestedUserDiffersFromCurrentUser_ShouldReturnForbiddenProblemDetails()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        var currentUserId = Guid.NewGuid();
        var requestedUserId = Guid.NewGuid();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/{requestedUserId}/organizations");
        request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(currentUserId));

        var response = await client.SendAsync(request);

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(response, HttpStatusCode.Forbidden, "Forbidden");
    }

    #endregion

    #region POST Endpoints

    [Test]
    public async Task SyncUser_WithoutAuth_ShouldReturnUnauthorized()
    {
        // Act
        var response = await _fixture.Client.PostAsync($"{BaseUrl}/sync", null);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    [Category(TestCategories.Fast)]
    public async Task SyncUser_WithOidcIssuerNormalization_PersistsAuthorityQualifiedProviderKey()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        string subject = Guid.NewGuid().ToString("D");
        ProviderAccountKey expected = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
            "https://auth.example.test/realms/ISLAMU",
            subject);

        using var request = CreateOidcSyncRequest(
            subject,
            "HTTPS://AUTH.EXAMPLE.TEST/realms/ISLAMU/",
            "normalized@example.test");
        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        UserExternalLogin login = await dbContext.UserExternalLogins.SingleAsync(x =>
            x.AuthenticationProviderId == (int)AuthenticationProviderKind.Keycloak);
        await Assert.That(login.ProviderKey).IsEqualTo(expected.Value);
    }

    [Test]
    [Category(TestCategories.Fast)]
    public async Task SyncUser_WhenOnlyRawSubjectLoginExists_DoesNotUseLegacyFallback()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        Guid legacyUserId = Guid.CreateVersion7();
        string subject = Guid.NewGuid().ToString("D");
        await SeedUserAndLoginAsync(factory, legacyUserId, "keycloak", subject, "legacy@example.test");

        using var request = CreateOidcSyncRequest(
            subject,
            "https://auth.example.test/realms/ISLAMU",
            "canonical@example.test");
        using HttpResponseMessage response = await client.SendAsync(request);
        BaseCommandResponse<Guid>? body = await response.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.Id).IsNotEqualTo(legacyUserId);
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await Assert.That(await dbContext.UserExternalLogins.CountAsync(x =>
            x.AuthenticationProviderId == (int)AuthenticationProviderKind.Keycloak)).IsEqualTo(2);
        await Assert.That(await dbContext.UserExternalLogins.AnyAsync(x =>
            x.ProviderKey == PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
                "https://auth.example.test/realms/ISLAMU",
                subject).Value
            && x.UserId == body.Id)).IsTrue();
    }

    [Test]
    [Category(TestCategories.Fast)]
    public async Task SyncUser_GuidSubjectFromDifferentIssuer_DoesNotSelectExistingInternalUser()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        Guid existingUserId = Guid.CreateVersion7();
        string subject = existingUserId.ToString("D");
        ProviderAccountKey issuerA = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
            "https://issuer-a.example.test",
            subject);
        await SeedUserAndLoginAsync(
            factory,
            existingUserId,
            "keycloak",
            issuerA.Value,
            "issuer-a@example.test");

        using var request = CreateOidcSyncRequest(
            subject,
            "https://issuer-b.example.test",
            "issuer-b@example.test");
        using HttpResponseMessage response = await client.SendAsync(request);
        BaseCommandResponse<Guid>? body = await response.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.Id).IsNotEqualTo(existingUserId);
        await Assert.That(body.Id.Version).IsEqualTo(7);
    }

    [Test]
    [Category(TestCategories.Fast)]
    public async Task SyncUser_WithAmbientAtprotoClaimsWithoutEmailVerification_ShouldReturnUnauthorized()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        var authUserId = Guid.NewGuid();
        var did = $"did:plc:{Guid.NewGuid():N}";
        await SeedLinkedAtprotoUserAsync(factory, authUserId, did);

        using var syncRequest = CreateAtprotoSyncRequest(authUserId, did, "atproto-user@example.test");
        var syncResponse = await client.SendAsync(syncRequest);

        await Assert.That(syncResponse.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    [Category(TestCategories.Fast)]
    public async Task SyncUser_WithAmbientAtprotoClaimsAndEmailVerification_ShouldReturnUnauthorized()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        var authUserId = Guid.NewGuid();
        var did = $"did:plc:{Guid.NewGuid():N}";
        await SeedLinkedAtprotoUserAsync(factory, authUserId, did);

        using var syncRequest = CreateAtprotoSyncRequest(authUserId, did, "verified-atproto-user@example.test", emailVerified: true);
        var syncResponse = await client.SendAsync(syncRequest);

        await Assert.That(syncResponse.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region PATCH Endpoints

    [Test]
    public async Task UpdateUserPatch_WithoutAuth_ShouldReturnUnauthorized()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var updateDto = new UpdateUserDto
        {
            Names = new UpdateUserNamesDto
            {
                FirstName = "Updated",
                LastName = "User"
            }
        };

        // Act
        var response = await _fixture.Client.PatchAsJsonAsync($"{BaseUrl}/{userId}", updateDto);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task UpdateUserPut_WhenUsingOldBodyIdRoute_ShouldReturnMethodNotAllowed()
    {
        // Arrange
        var oldBody = new
        {
            id = Guid.NewGuid(),
            names = new
            {
                firstName = "Updated",
                lastName = "User"
            }
        };

        // Act
        var response = await _fixture.Client.PutAsJsonAsync(BaseUrl, oldBody);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MethodNotAllowed);
    }

    [Test]
    public async Task UpdateUserPatch_WhenAuthenticatedWithoutIfMatch_ShouldReturnBadRequest()
    {
        // Arrange
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        var userId = Guid.NewGuid();
        var updateDto = new UpdateUserDto
        {
            Names = new UpdateUserNamesDto
            {
                FirstName = "Updated",
                LastName = "User"
            }
        };
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"{BaseUrl}/{userId}")
        {
            Content = JsonContent.Create(updateDto)
        };
        request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(userId));

        // Act
        var response = await client.SendAsync(request);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    #endregion

    #region DELETE Endpoints

    [Test]
    public async Task DeleteUser_WithoutAuth_ShouldReturnUnauthorized()
    {
        // Act
        var response = await _fixture.Client.DeleteAsync(BaseUrl);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task DeleteUser_WithUuidV7Idempotency_ReturnsAcceptedReceiptContract()
    {
        Guid userId = Guid.CreateVersion7();
        Guid intentId = Guid.CreateVersion7();
        var expected = new PrivacyErasureStartDto(
            "completed",
            "once-revealed-receipt",
            DateTime.UtcNow.AddDays(7));
        IMediator mediator = Substitute.For<IMediator>();
        var resourceAssembler = Substitute.For<IResourceAssembler<UserDto, UserDto>>();
        mediator.Send(
                Arg.Is<DeleteUserCommand>(command =>
                    command.UserId == userId && command.IntentId == intentId),
                Arg.Any<CancellationToken>())
            .Returns(expected);
        var controller = new UserController(mediator, resourceAssembler)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim("internal_user_id", userId.ToString("D"))],
                        "test"))
                }
            }
        };

        ActionResult<PrivacyErasureStartDto> result = await controller.DeleteUser(
            intentId.ToString("D"),
            CancellationToken.None);

        var accepted = result.Result as AcceptedAtRouteResult;
        await Assert.That(accepted).IsNotNull();
        await Assert.That(accepted!.StatusCode).IsEqualTo(StatusCodes.Status202Accepted);
        await Assert.That(accepted.RouteName).IsEqualTo(RouteNames.GetPrivacyErasureStatus);
        await Assert.That(accepted.Value).IsSameReferenceAs(expected);
        await Assert.That(controller.Response.Headers.RetryAfter.ToString()).IsEqualTo("5");
        await Assert.That(controller.Response.Headers.CacheControl.ToString()).Contains("no-store");
    }

    #endregion

    private static async Task SeedLinkedAtprotoUserAsync(
        AuthenticatedWebApplicationFactory factory,
        Guid userId,
        string did) =>
        await SeedUserAndLoginAsync(
            factory,
            userId,
            "atproto",
            did,
            $"{userId:N}@integration.test");

    private static async Task SeedUserAndLoginAsync(
        AuthenticatedWebApplicationFactory factory,
        Guid userId,
        string provider,
        string providerKey,
        string email)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();

        if (!await dbContext.Users.AnyAsync(x => x.Id == userId))
        {
            dbContext.Users.Add(new User
            {
                Id = userId,
                EmailVerified = true,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = userId,
                Pii = new UserPii
                {
                    UserId = userId,
                    Email = email,
                    FirstName = "Integration",
                    LastName = "User"
                }
            });
        }

        if (!await dbContext.UserExternalLogins.AnyAsync(x =>
                x.UserId == userId
                && x.AuthenticationProviderId == (int)provider.ParseAuthenticationProviderKind()
                && x.ProviderKey == providerKey))
        {
            dbContext.UserExternalLogins.Add(new UserExternalLogin
            {
                Id = Guid.CreateVersion7(),
                UserId = userId,
                User = null!,
                AuthenticationProviderId = (int)provider.ParseAuthenticationProviderKind(),
                AuthenticationProvider = null!,
                ProviderKey = providerKey,
                ProviderDisplayName = provider,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = userId
            });
        }

        await dbContext.SaveChangesAsync();
    }

    private static HttpRequestMessage CreateOidcSyncRequest(
        string subject,
        string issuer,
        string email)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/sync");
        request.Headers.Add(
            TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(
                Guid.Parse(subject),
                "OIDC User",
                ("iss", issuer),
                ("idp", "keycloak"),
                ("email", email),
                ("given_name", "OIDC"),
                ("family_name", "User")));
        return request;
    }

    private static HttpRequestMessage CreateAtprotoSyncRequest(
        Guid authUserId,
        string did,
        string email,
        bool? emailVerified = null)
    {
        return CreateAtprotoRequest(HttpMethod.Post, $"{BaseUrl}/sync", authUserId, did, email, emailVerified);
    }

    private static HttpRequestMessage CreateAtprotoRequest(
        HttpMethod method,
        string url,
        Guid authUserId,
        string did,
        string email,
        bool? emailVerified = null)
    {
        var additionalClaims = new List<(string Type, string Value)>
        {
            ("idp", "atproto"),
            ("did", did),
            ("email", email),
            ("given_name", "ATProto"),
            ("family_name", "User")
        };

        if (emailVerified.HasValue)
        {
            additionalClaims.Add(("email_verified", emailVerified.Value.ToString().ToLowerInvariant()));
        }

        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(
            TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(authUserId, "ATProto User", additionalClaims.ToArray()));
        return request;
    }
}
