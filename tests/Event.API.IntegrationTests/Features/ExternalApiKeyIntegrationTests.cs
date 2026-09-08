using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Helpers;
using Explore.Application.DTOs.ExternalApiKey;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("SingleTenantAuthenticatedApiFixture")]
[ClassDataSource<SingleTenantAuthenticatedApiTestFixture>(Shared = SharedType.PerAssembly)]
public class ExternalApiKeyIntegrationTests
{
    private readonly SingleTenantAuthenticatedApiTestFixture _fixture;

    public ExternalApiKeyIntegrationTests(SingleTenantAuthenticatedApiTestFixture fixture)
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
        await Assert.That(raw).DoesNotContain(issued.ApiKey!);
        await Assert.That(raw).DoesNotContain(issued.ApiKey!.Split('.', 2)[1]);
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
        request.Content = JsonContent.Create(payload);

        var response = await _fixture.Client.SendAsync(request);

        await AssertExternalApiKeyValidationProblemAsync(
            response,
            "External API key creation failed.",
            "At least one scope is required.");
    }

    [Test]
    public async Task CreateExternalApiKey_WithNameControlCharacter_ShouldReturnValidationProblemDetails()
    {
        var userId = Guid.NewGuid();
        var payload = new CreateExternalApiKeyDto
        {
            Name = "Invalid\nKey",
            Scopes = [ExternalApiKeyScopes.EventsRead],
            ExternalApiKeyOwnerTypeId = (int)ExternalApiKeyOwnerType.User
        };

        using var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Post, "/api/externalapikey", userId);
        request.Content = JsonContent.Create(payload);

        var response = await _fixture.Client.SendAsync(request);

        await AssertExternalApiKeyValidationProblemAsync(
            response,
            "External API key creation failed.",
            "API key name must not contain control characters.");
    }

    [Test]
    public async Task CreateExternalApiKey_WithDescriptionTooLong_ShouldReturnValidationProblemDetails()
    {
        var userId = Guid.NewGuid();
        var payload = new CreateExternalApiKeyDto
        {
            Name = "Description Validation",
            Description = new string('a', 1001),
            Scopes = [ExternalApiKeyScopes.EventsRead],
            ExternalApiKeyOwnerTypeId = (int)ExternalApiKeyOwnerType.User
        };

        using var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Post, "/api/externalapikey", userId);
        request.Content = JsonContent.Create(payload);

        var response = await _fixture.Client.SendAsync(request);

        await AssertExternalApiKeyValidationProblemAsync(
            response,
            "External API key creation failed.",
            "API key description cannot exceed 1000 characters.");
    }

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
        request.Content = JsonContent.Create(payload);

        var response = await _fixture.Client.SendAsync(request);

        await AssertExternalApiKeyValidationProblemAsync(
            response,
            "External API key creation failed.",
            "An API key with the same name already exists for this owner.");
    }

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

    private async Task<CreateExternalApiKeyCommandResponse> CreateIssuedExternalApiKeyAsync(Guid userId, string name, List<string> scopes)
    {
        var payload = new CreateExternalApiKeyDto
        {
            Name = name,
            Scopes = scopes,
            ExternalApiKeyOwnerTypeId = (int)ExternalApiKeyOwnerType.User
        };

        using var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Post, "/api/externalapikey", userId);
        request.Content = JsonContent.Create(payload);

        var response = await _fixture.Client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<CreateExternalApiKeyCommandResponse>();
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.IsSuccess).IsTrue();

        using var scope = _fixture.Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var stored = await dbContext.ExternalApiKeys.SingleAsync(x => x.Id == body.Id);
        await Assert.That(stored.CreatedAt).IsNotEqualTo(default(DateTime));
        await Assert.That(stored.SecretHash).IsNotEqualTo(body.ApiKey);
        await Assert.That(ApiKeyHashing.TryParsePersistedApiKey(body.ApiKey!, out _, out var secret)).IsTrue();
        await Assert.That(stored.SecretHash).IsEqualTo(ApiKeyHashing.ComputeHash(secret));

        return body;
    }
}
