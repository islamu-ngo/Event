using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Helpers;
using Event.Api.IntegrationTests.Builders;
using Explore.Application.DTOs.ExternalApiKey;
using Explore.Application.Models;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain.Constants;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TUnit.Core.Interfaces;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("SingleTenantAuthenticatedApiFixture")]
[ClassDataSource<ExternalApiKeyIntegrationFixture>(Shared = SharedType.PerAssembly)]
public class ExternalApiKeyIntegrationTests
{
    private readonly ExternalApiKeyIntegrationFixture _fixture;

    /// <summary>Uses the shared authenticated HTTP host and durable privacy authority for issuance-dependent API operations.</summary>
    public ExternalApiKeyIntegrationTests(ExternalApiKeyIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Test]
    public async Task UpdateExternalApiKeyPolicy_WithOwnerRequest_ShouldUpdateEditableFields()
    {
        var userId = Guid.NewGuid();
        var apiKeyId = await CreateExternalApiKeyAsync(userId, "Build Bot", ["events:read"]);
        var expiresAt = DateTime.UtcNow.AddDays(30);

        var payload = new UpdateExternalApiKeyPolicyDto
        {
            Metadata = new ExternalApiKeyMetadataUpdateDto { Name = "Deploy Bot" },
            AccessPolicy = new ExternalApiKeyAccessPolicyUpdateDto
            {
                Scopes = ["events:write", "events:read", "events:write"],
                ExpiresAt = expiresAt
            }
        };

        using var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Patch, $"/api/externalapikey/{apiKeyId}", userId);
        request.Content = JsonContent.Create(payload);

        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>();
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.IsSuccess).IsTrue();
        await Assert.That(body.Id).IsEqualTo(apiKeyId);

        using var scope = _fixture.Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var stored = await dbContext.ExternalApiKeys.SingleAsync(x => x.Id == apiKeyId);

        await Assert.That(stored.Name).IsEqualTo("Deploy Bot");
        await Assert.That(stored.Scopes).IsEqualTo("events:read events:write");
        await Assert.That(stored.ExpiresAt).IsEqualTo(expiresAt);
        await Assert.That(stored.OwnerId).IsEqualTo(userId);
        await Assert.That(stored.ExternalApiKeyStatusId).IsEqualTo((int)ExternalApiKeyStatusEnum.Active);
        await Assert.That(stored.UpdatedAt).IsNotNull();
    }

    /// <summary>Checks owner-visible metadata while rejecting raw credential and hash disclosure after issuance.</summary>
    [Test]
    public async Task GetExternalApiKeyDetails_WithOwnerRequest_ShouldReturnVisibleMetadata()
    {
        var userId = Guid.NewGuid();
        var issued = await CreateIssuedExternalApiKeyAsync(userId, "Reader Bot", ["events:read", "events:write"]);
        var apiKeyId = issued.Id;

        using var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Get, $"/api/externalapikey/{apiKeyId}", userId);
        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var raw = await response.Content.ReadAsStringAsync();
        var body = JsonSerializer.Deserialize<ExternalApiKeyListDto>(raw, TestJsonOptions.Default);
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.Id).IsEqualTo(apiKeyId);
        await Assert.That(body.Name).IsEqualTo("Reader Bot");
        await Assert.That(body.ExternalApiKeyOwnerTypeId).IsEqualTo((int)ExternalApiKeyOwnerType.User);
        await Assert.That(body.ExternalApiKeyOwnerTypeCode).IsEqualTo("USER");
        await Assert.That(body.OwnerId).IsEqualTo(userId);
        await Assert.That(body.Scopes).IsEquivalentTo(["events:read", "events:write"]);
        await Assert.That(body.KeyId.Length).IsEqualTo(16);
        await Assert.That(raw).DoesNotContain("\"apiKey\"");
        await Assert.That(raw).DoesNotContain("\"secretHash\"");
        await Assert.That(raw.Contains(issued.ApiKey!, StringComparison.Ordinal)).IsFalse();
        await Assert.That(raw.Contains(issued.ApiKey!.Split('.', 2)[1], StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task GetExternalApiKeyDetails_WithDifferentUser_ShouldReturnNotFound()
    {
        var ownerUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var apiKeyId = await CreateExternalApiKeyAsync(ownerUserId, "Private Bot", ["events:read"]);

        using var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Get, $"/api/externalapikey/{apiKeyId}", otherUserId);
        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    /// <summary>Rejects empty scopes through the HTTP validation contract rather than a success-shaped acknowledgement.</summary>
    [Test]
    public async Task CreateExternalApiKey_WithValidationFailure_ShouldReturnValidationProblemDetails()
    {
        var userId = Guid.NewGuid();
        var payload = new CreateExternalApiKeyDto
        {
            Name = "Invalid Key",
            Scopes = [],
            ExternalApiKeyOwnerTypeId = (int)ExternalApiKeyOwnerType.User
        };

        using var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Post, "/api/externalapikey", userId);
        request.Headers.Add("Idempotency-Key", Guid.CreateVersion7().ToString("N"));
        request.Content = JsonContent.Create(payload);

        var response = await _fixture.Client.SendAsync(request);

        await AssertExternalApiKeyValidationProblemAsync(
            response,
            "External API key creation failed.",
            "At least one scope is required.");
    }

    /// <summary>Seeds legitimate owner authority before isolating the name control-character validation failure.</summary>
    [Test]
    public async Task CreateExternalApiKey_WithNameControlCharacter_ShouldReturnValidationProblemDetails()
    {
        var userId = Guid.NewGuid();
        await SeedIssuanceOwnerAsync(userId);
        var payload = new CreateExternalApiKeyDto
        {
            Name = "Invalid\nKey",
            Scopes = [ExternalApiKeyScopes.EventsRead],
            ExternalApiKeyOwnerTypeId = (int)ExternalApiKeyOwnerType.User
        };

        using var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Post, "/api/externalapikey", userId);
        request.Headers.Add("Idempotency-Key", Guid.CreateVersion7().ToString("N"));
        request.Content = JsonContent.Create(payload);

        var response = await _fixture.Client.SendAsync(request);

        await AssertExternalApiKeyValidationProblemAsync(
            response,
            "External API key creation failed.",
            "API key name must not contain control characters.");
    }

    /// <summary>Rejects excessive policy text for an otherwise authorized issuer through ValidationProblemDetails.</summary>
    [Test]
    public async Task CreateExternalApiKey_WithDescriptionTooLong_ShouldReturnValidationProblemDetails()
    {
        var userId = Guid.NewGuid();
        await SeedIssuanceOwnerAsync(userId);
        var payload = new CreateExternalApiKeyDto
        {
            Name = "Description Validation",
            Description = new string('a', 1001),
            Scopes = [ExternalApiKeyScopes.EventsRead],
            ExternalApiKeyOwnerTypeId = (int)ExternalApiKeyOwnerType.User
        };

        using var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Post, "/api/externalapikey", userId);
        request.Headers.Add("Idempotency-Key", Guid.CreateVersion7().ToString("N"));
        request.Content = JsonContent.Create(payload);

        var response = await _fixture.Client.SendAsync(request);

        await AssertExternalApiKeyValidationProblemAsync(
            response,
            "External API key creation failed.",
            "API key description cannot exceed 1000 characters.");
    }

    /// <summary>Checks owner-local name uniqueness after whitespace normalization using a genuinely issued key.</summary>
    [Test]
    public async Task CreateExternalApiKey_WithPaddedDuplicateName_ShouldReturnValidationProblemDetails()
    {
        var userId = Guid.NewGuid();
        await CreateExternalApiKeyAsync(userId, "Normalized Bot", [ExternalApiKeyScopes.EventsRead]);
        var payload = new CreateExternalApiKeyDto
        {
            Name = " Normalized Bot ",
            Scopes = [ExternalApiKeyScopes.EventsRead],
            ExternalApiKeyOwnerTypeId = (int)ExternalApiKeyOwnerType.User
        };

        using var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Post, "/api/externalapikey", userId);
        request.Headers.Add("Idempotency-Key", Guid.CreateVersion7().ToString("N"));
        request.Content = JsonContent.Create(payload);

        var response = await _fixture.Client.SendAsync(request);

        await AssertExternalApiKeyValidationProblemAsync(
            response,
            "External API key creation failed.",
            "An API key with the same name already exists for this owner.");
    }

    /// <summary>Requires tenant-admin authority before persistence and verifies denial leaves no credential row.</summary>
    [Test]
    public async Task CreateExternalApiKey_WithTenantOwnerAndNoTenantAdminAuthority_ShouldReturnForbiddenWithoutCreatingKey()
    {
        var userId = Guid.NewGuid();
        var keyName = $"Tenant Key {Guid.NewGuid():N}";
        var payload = new CreateExternalApiKeyDto
        {
            Name = keyName,
            Scopes = [ExternalApiKeyScopes.AdminTenant],
            ExternalApiKeyOwnerTypeId = (int)ExternalApiKeyOwnerType.Tenant
        };

        using var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Post, "/api/externalapikey", userId);
        request.Headers.Add("Idempotency-Key", Guid.CreateVersion7().ToString("N"));
        request.Content = JsonContent.Create(payload);

        var response = await _fixture.Client.SendAsync(request);

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(response, HttpStatusCode.Forbidden, "Forbidden");

        using var scope = _fixture.Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var exists = await dbContext.ExternalApiKeys.AnyAsync(x => x.Name == keyName);
        await Assert.That(exists).IsFalse();
    }

    [Test]
    public async Task UpdateExternalApiKeyPolicy_ContractExcludesBodyIdentityAndKeyMaterial()
    {
        var payload = new UpdateExternalApiKeyPolicyDto
        {
            Metadata = new ExternalApiKeyMetadataUpdateDto { Name = "Policy name" }
        };
        using var content = JsonContent.Create(payload);

        var body = await content.ReadAsStringAsync();

        await Assert.That(body).DoesNotContain("\"id\"");
        await Assert.That(body).DoesNotContain("\"keyId\"");
        await Assert.That(body).DoesNotContain("\"secret\"");
        await Assert.That(body).DoesNotContain("\"ownerId\"");
        await Assert.That(body).DoesNotContain("\"tenantId\"");
    }

    [Test]
    public async Task UpdateExternalApiKeyPolicy_WithValidationFailure_ShouldReturnValidationProblemDetails()
    {
        var userId = Guid.NewGuid();
        var apiKeyId = await CreateExternalApiKeyAsync(userId, "Validation Source", ["events:read"]);
        var payload = new UpdateExternalApiKeyPolicyDto
        {
            AccessPolicy = new ExternalApiKeyAccessPolicyUpdateDto
            {
                Scopes = [],
                ExpiresAt = DateTime.UtcNow.AddDays(14)
            }
        };

        using var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Patch, $"/api/externalapikey/{apiKeyId}", userId);
        request.Content = JsonContent.Create(payload);

        var response = await _fixture.Client.SendAsync(request);

        await AssertExternalApiKeyValidationProblemAsync(
            response,
            "External API key update failed.",
            "At least one scope is required.");
    }

    [Test]
    public async Task UpdateExternalApiKeyPolicy_WithNameControlCharacter_ShouldReturnValidationProblemDetailsAndLeaveKeyUnchanged()
    {
        var userId = Guid.NewGuid();
        var apiKeyId = await CreateExternalApiKeyAsync(userId, "Control Source", [ExternalApiKeyScopes.EventsRead]);
        var payload = new UpdateExternalApiKeyPolicyDto
        {
            Metadata = new ExternalApiKeyMetadataUpdateDto { Name = "Control\nTarget" }
        };

        using var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Patch, $"/api/externalapikey/{apiKeyId}", userId);
        request.Content = JsonContent.Create(payload);

        var response = await _fixture.Client.SendAsync(request);

        await AssertExternalApiKeyValidationProblemAsync(
            response,
            "External API key update failed.",
            "API key name must not contain control characters.");

        using var scope = _fixture.Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var stored = await dbContext.ExternalApiKeys.SingleAsync(x => x.Id == apiKeyId);

        await Assert.That(stored.Name).IsEqualTo("Control Source");
        await Assert.That(stored.Scopes).IsEqualTo(ExternalApiKeyScopes.EventsRead);
        await Assert.That(stored.ExpiresAt).IsNull();
    }

    [Test]
    public async Task UpdateExternalApiKeyPolicy_WithDifferentUser_ShouldReturnNotFoundAndLeaveKeyUnchanged()
    {
        var ownerUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var apiKeyId = await CreateExternalApiKeyAsync(ownerUserId, "Owner Key", ["events:read"]);

        var payload = new UpdateExternalApiKeyPolicyDto
        {
            Metadata = new ExternalApiKeyMetadataUpdateDto { Name = "Hijacked Key" },
            AccessPolicy = new ExternalApiKeyAccessPolicyUpdateDto
            {
                Scopes = ["events:write"],
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            }
        };

        using var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Patch, $"/api/externalapikey/{apiKeyId}", otherUserId);
        request.Content = JsonContent.Create(payload);

        var response = await _fixture.Client.SendAsync(request);

        await AssertExternalApiKeyNotFoundProblemAsync(response);

        using var scope = _fixture.Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var stored = await dbContext.ExternalApiKeys.SingleAsync(x => x.Id == apiKeyId);

        await Assert.That(stored.Name).IsEqualTo("Owner Key");
        await Assert.That(stored.Scopes).IsEqualTo("events:read");
        await Assert.That(stored.ExpiresAt).IsNull();
        await Assert.That(stored.OwnerId).IsEqualTo(ownerUserId);
    }

    [Test]
    public async Task GetUsageReport_WithAuthenticatedNonAdmin_ShouldReturnForbidden()
    {
        var userId = Guid.NewGuid();

        using var request = _fixture.CreateAuthenticatedRequest(
            HttpMethod.Get,
            "/api/externalapikey/usage-report?from=2026-01-01&to=2026-01-31",
            userId);

        var response = await _fixture.Client.SendAsync(request);

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(response, HttpStatusCode.Forbidden, "Forbidden");
    }

    [Test]
    public async Task GetUsageReport_WithInvalidDateRange_ShouldReturnValidationProblemBeforeAdminAuthorization()
    {
        var userId = Guid.NewGuid();

        using var request = _fixture.CreateAuthenticatedRequest(
            HttpMethod.Get,
            "/api/externalapikey/usage-report?from=2026-02-01&to=2026-01-31",
            userId);

        var response = await _fixture.Client.SendAsync(request);

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(response, HttpStatusCode.BadRequest, "Validation failed");

        using var document = await ProblemDetailsAssertions.ReadAsJsonAsync(response);
        var root = document.RootElement;
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo("validation_failed");
        await Assert.That(root.GetProperty("errors").TryGetProperty("from", out _)).IsTrue();
        await Assert.That(root.GetProperty("errors").TryGetProperty("to", out _)).IsTrue();
    }

    private static async Task AssertExternalApiKeyValidationProblemAsync(
        HttpResponseMessage response,
        string expectedDetail,
        string expectedError)
    {
        await ProblemDetailsAssertions.AssertProblemDetailsAsync(
            response,
            HttpStatusCode.BadRequest,
            "External API key validation failed");

        using var document = await ProblemDetailsAssertions.ReadAsJsonAsync(response);
        var root = document.RootElement;

        await Assert.That(root.GetProperty("detail").GetString()).IsEqualTo(expectedDetail);
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo("validation_failed");

        var errors = root.GetProperty("errors").GetProperty("externalApiKey");
        await Assert.That(errors.GetArrayLength()).IsEqualTo(1);
        await Assert.That(errors[0].GetString()).IsEqualTo(expectedError);
    }

    private static async Task AssertExternalApiKeyNotFoundProblemAsync(HttpResponseMessage response)
    {
        await ProblemDetailsAssertions.AssertProblemDetailsAsync(
            response,
            HttpStatusCode.NotFound,
            "External API key not found");

        using var document = await ProblemDetailsAssertions.ReadAsJsonAsync(response);
        var root = document.RootElement;

        await Assert.That(root.GetProperty("detail").GetString()).IsEqualTo("External API key not found.");
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo("resource_not_found");
    }

    [Test]
    public async Task DeleteExternalApiKey_WithOwnerRequest_ShouldRevokeKeyAndPopulateAuditFields()
    {
        var userId = Guid.NewGuid();
        var apiKeyId = await CreateExternalApiKeyAsync(userId, "Ops Bot", ["events:read"]);

        using var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Delete, $"/api/externalapikey/{apiKeyId}", userId);
        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

        using var scope = _fixture.Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var stored = await dbContext.ExternalApiKeys.SingleAsync(x => x.Id == apiKeyId);

        await Assert.That(stored.ExternalApiKeyStatusId).IsEqualTo((int)ExternalApiKeyStatusEnum.Revoked);
        await Assert.That(stored.UpdatedAt).IsNotNull();
    }

    [Test]
    public async Task DeleteExternalApiKey_WithDifferentUser_ShouldReturnNotFoundAndLeaveKeyActive()
    {
        var ownerUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var apiKeyId = await CreateExternalApiKeyAsync(ownerUserId, "Private Ops Bot", ["events:read"]);

        using var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Delete, $"/api/externalapikey/{apiKeyId}", otherUserId);
        var response = await _fixture.Client.SendAsync(request);

        await AssertExternalApiKeyNotFoundProblemAsync(response);

        using var scope = _fixture.Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var stored = await dbContext.ExternalApiKeys.SingleAsync(x => x.Id == apiKeyId);

        await Assert.That(stored.ExternalApiKeyStatusId).IsEqualTo((int)ExternalApiKeyStatusEnum.Active);
        await Assert.That(stored.UpdatedAt).IsNull();
        await Assert.That(stored.OwnerId).IsEqualTo(ownerUserId);
    }

    private async Task<Guid> CreateExternalApiKeyAsync(Guid userId, string name, List<string> scopes)
    {
        var body = await CreateIssuedExternalApiKeyAsync(userId, name, scopes);
        return body.Id;
    }

    /// <summary>Persists the platform user and active tenant membership required by fresh issuance authority checks.</summary>
    private async Task SeedIssuanceOwnerAsync(Guid userId)
    {
        await using (var seedScope = _fixture.Factory.Services.CreateAsyncScope())
        {
            var database = seedScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            if (!await database.Users.AnyAsync(user => user.Id == userId))
            {
                var user = new UserBuilder().WithId(userId).Build();
                database.Users.Add(user);
                database.TenantUsers.Add(new TenantUser
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = PlatformDefaults.DefaultTenantId,
                    Tenant = null!,
                    UserId = userId,
                    User = user,
                    StatusId = (int)TenantUserStatusEnum.Active,
                    JoinedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                });
                await database.SaveChangesAsync();
            }
        }
    }

    /// <summary>Issues through the real HTTP success contract and verifies durable hash storage without redisclosing it.</summary>
    private async Task<ExternalApiKeyIssuanceDto> CreateIssuedExternalApiKeyAsync(Guid userId, string name, List<string> scopes)
    {
        await SeedIssuanceOwnerAsync(userId);
        var payload = new CreateExternalApiKeyDto
        {
            Name = name,
            Scopes = scopes,
            ExternalApiKeyOwnerTypeId = (int)ExternalApiKeyOwnerType.User
        };

        using var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Post, "/api/externalapikey", userId);
        request.Headers.Add("Idempotency-Key", Guid.CreateVersion7().ToString("N"));
        request.Content = JsonContent.Create(payload);

        var response = await _fixture.Client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<ExternalApiKeyIssuanceDto>();
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.DisclosureStatus).IsEqualTo(ExternalApiKeyDisclosureStatus.Issued);

        using var scope = _fixture.Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var stored = await dbContext.ExternalApiKeys.SingleAsync(x => x.Id == body.Id);
        await Assert.That(stored.CreatedAt).IsNotEqualTo(default(DateTime));
        await Assert.That(stored.SecretHash == body.ApiKey).IsFalse();
        await Assert.That(ApiKeyHashing.TryParsePersistedApiKey(body.ApiKey!, out _, out var secret)).IsTrue();
        await Assert.That(stored.SecretHash).IsEqualTo(ApiKeyHashing.ComputeHash(secret));

        return body;
    }
}

public sealed class ExternalApiKeyIntegrationFixture : IAsyncInitializer, IAsyncDisposable
{
    private readonly DirectoryInfo _authorityDirectory =
        Directory.CreateTempSubdirectory("external-api-key-authority-");
    private string AuthorityPath => Path.Join(_authorityDirectory.FullName, "authority.db");

    public SingleTenantAuthenticatedWebApplicationFactory Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;

    /// <summary>Starts the authenticated host with active tenancy and migrates its isolated retained privacy authority.</summary>
    public async Task InitializeAsync()
    {
        Factory = new IssuanceFactory(AuthorityPath) { SeedActiveDefaultTenant = true };
        Factory.AdditionalConfiguration["PrivacyErasure:Authority:Topology"] = "EmbeddedSqlite";
        Factory.AdditionalConfiguration["PrivacyErasureAuthorityEmbedded:Path"] = AuthorityPath;
        Factory.AdditionalConfiguration["PrivacyErasureAuthorityEmbedded:WriterReplicaCount"] = "1";
        Client = Factory.CreateClient();
        await using var scope = Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<EmbeddedPrivacyErasureAuthorityStorage>()
            .EnsureReadyAsync();
        var authority = scope.ServiceProvider.GetRequiredService<EmbeddedPrivacyErasureAuthorityDbContext>();
        await authority.Database.MigrateAsync();
    }

    /// <summary>Binds the test principal through the host authentication handler rather than direct request-service substitution.</summary>
    public HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string url, Guid userId)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(userId));
        return request;
    }

    /// <summary>Stops the HTTP host before removing its temporary privacy-authority database.</summary>
    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        if (Factory is not null)
            await Factory.DisposeAsync();
        if (_authorityDirectory.Exists)
            _authorityDirectory.Delete(recursive: true);
    }

    /// <summary>Supplies a real test-owned privacy authority after production secret-authority projection.</summary>
    private sealed class IssuanceFactory(string authorityPath) : SingleTenantAuthenticatedWebApplicationFactory
    {
        /// <summary>Overrides only native embedded storage options; authentication, owner fences and PostgreSQL remain real.</summary>
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                var embedded = new EmbeddedPrivacyErasureAuthorityOptions { Path = authorityPath };
                services.RemoveAll<EmbeddedPrivacyErasureAuthorityOptions>();
                services.AddSingleton(embedded);
                services.ConfigureDbContext<EmbeddedPrivacyErasureAuthorityDbContext>(options =>
                    EmbeddedPrivacyErasureAuthorityDbContextFactory.Configure(options, embedded));
            });
        }
    }
}
