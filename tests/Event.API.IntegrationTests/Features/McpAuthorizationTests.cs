using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Mcp;
using Explore.Application.Constants;
using Explore.Application.Features.AiAssistant.Tools;
using Explore.Application.Services;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using TUnit.Core;

namespace ApiIntegrationTests.Features;

public sealed class McpAuthorizationTests
{
    private static readonly string[] ProjectedProposalToolNames = AiMcpProjectedToolFactory
        .CreateTools(AiToolContractRegistry.CreateDefault())
        .Select(tool => tool.ProtocolTool.Name)
        .ToArray();

    private static readonly string[] ProposalToolNames = ProjectedProposalToolNames
        .Prepend("propose_ai_tool_action")
        .ToArray();

    private static readonly string[] EventManagementReadToolNames =
    [
        "list_my_events",
        "get_event_creation_context",
        "get_event_publish_readiness",
        "get_event_program_management_context",
        "get_event_custom_properties_context",
        "get_event_registrations_context",
        "get_event_team_context",
        "get_event_template_catalog_context",
        "get_event_template_sync_context",
        "get_event_session_template_sync_context"
    ];

    [Test]
    public async Task McpEndpoint_WhenRuntimeGovernanceDisabled_ReturnsNotFound()
    {
        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            DeploymentMode = DeploymentMode.SingleTenant,
            DefaultTenantId = PlatformDefaults.DefaultTenantId,
            McpEnabled = true,
            McpRuntimeEnabled = false
        };
        using var client = factory.CreateClient();
        using var request = CreateMcpRequest();

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task McpEndpoint_WhenStartupDisabled_ReturnsNotFoundEvenIfRuntimeEnabled()
    {
        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            DeploymentMode = DeploymentMode.SingleTenant,
            DefaultTenantId = PlatformDefaults.DefaultTenantId,
            McpEnabled = false,
            McpRuntimeEnabled = true
        };
        using var client = factory.CreateClient();
        using var request = CreateMcpRequest();

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task McpEndpoint_WhenAnonymous_CanListAnonymousSafeContractsOnly()
    {
        await using var factory = CreateMcpEnabledFactory();
        using var client = factory.CreateClient();
        using var request = CreateMcpRequest();

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var toolNames = await ReadToolNamesAsync(response);
        await Assert.That(toolNames).Contains("list_ai_tool_contracts");
        await AssertEventManagementReadsHidden(toolNames);
        await AssertProposalToolsHidden(toolNames);
    }

    [Test]
    public async Task McpEndpoint_WhenStartupDefaultsAreUsed_MapsDefaultPath()
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider()
        };
        using var client = factory.CreateClient();
        using var request = CreateMcpRequest();

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var toolNames = await ReadToolNamesAsync(response);
        await Assert.That(toolNames).Contains("list_ai_tool_contracts");
    }

    [Test]
    public async Task McpEndpoint_WithEmptyApiKeyHeader_TreatsHeaderAsAnonymous()
    {
        await using var factory = CreateMcpEnabledFactory();
        using var client = factory.CreateClient();
        using var request = CreateMcpRequest();
        request.Headers.Add("X-API-Key", string.Empty);

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var toolNames = await ReadToolNamesAsync(response);
        await Assert.That(toolNames).Contains("list_ai_tool_contracts");
        await AssertEventManagementReadsHidden(toolNames);
        await AssertProposalToolsHidden(toolNames);
    }

    [Test]
    public async Task McpEndpoint_WhenAuthenticated_ReachesMcpProtocolBoundary()
    {
        await using var factory = CreateMcpEnabledFactory();
        using var client = factory.CreateClient();
        using var request = CreateMcpRequest();
        request.Headers.Add(
            TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(Guid.CreateVersion7(), "MCP User"));

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.Forbidden);
        var toolNames = await ReadToolNamesAsync(response);
        await AssertEventManagementReadsDiscoverable(toolNames);
        await AssertProposalToolsDiscoverable(toolNames, includeGeneric: false);
    }

    [Test]
    public async Task McpEndpoint_WithInvalidApiKey_FallsBackToAnonymousSafeContracts()
    {
        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            DeploymentMode = DeploymentMode.SingleTenant,
            DefaultTenantId = PlatformDefaults.DefaultTenantId,
            McpEnabled = true
        };
        using var client = factory.CreateClient();
        using var request = CreateMcpRequest();
        request.Headers.Add("X-API-Key", "invalid-test-key");

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var toolNames = await ReadToolNamesAsync(response);
        await Assert.That(toolNames).Contains("list_ai_tool_contracts");
        await AssertEventManagementReadsHidden(toolNames);
        await AssertProposalToolsHidden(toolNames);
    }

    [Test]
    public async Task McpEndpoint_WithRevokedApiKey_FallsBackToAnonymousSafeContracts()
    {
        const string keyId = "mcp-revoked-key";
        const string secret = "mcp-revoked-secret";
        var rawApiKey = ApiKeyHashing.FormatPersistedApiKey(keyId, secret);

        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            DeploymentMode = DeploymentMode.SingleTenant,
            DefaultTenantId = PlatformDefaults.DefaultTenantId,
            McpEnabled = true,
            PersistedApiKeys =
            [
                new ExternalApiPhase0WebApplicationFactory.PersistedApiKeySeed
                {
                    KeyId = keyId,
                    Secret = secret,
                    TenantId = PlatformDefaults.DefaultTenantId,
                    OwnerId = Guid.CreateVersion7(),
                    Status = ExternalApiKeyStatusEnum.Revoked,
                    Scopes = [ExternalApiKeyScopes.McpRead, ExternalApiKeyScopes.McpPropose]
                }
            ]
        };
        using var client = factory.CreateClient();
        using var request = CreateMcpRequest();
        request.Headers.Add("X-API-Key", rawApiKey);

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var toolNames = await ReadToolNamesAsync(response);
        await Assert.That(toolNames).Contains("list_ai_tool_contracts");
        await AssertEventManagementReadsHidden(toolNames);
        await AssertProposalToolsHidden(toolNames);
    }

    [Test]
    public async Task McpEndpoint_WithValidApiKeyAndConflictingTenantSlug_ReturnsNotFound()
    {
        var apiKeyTenantId = Guid.CreateVersion7();
        var hintedTenantId = Guid.CreateVersion7();
        const string keyId = "mcp-tenant-key";
        const string secret = "mcp-tenant-secret";
        var rawApiKey = ApiKeyHashing.FormatPersistedApiKey(keyId, secret);

        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            DeploymentMode = DeploymentMode.MultiTenant,
            McpEnabled = true,
            TenantSlugMappings = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
            {
                ["beta"] = hintedTenantId
            },
            PersistedApiKeys =
            [
                new ExternalApiPhase0WebApplicationFactory.PersistedApiKeySeed
                {
                    KeyId = keyId,
                    Secret = secret,
                    TenantId = apiKeyTenantId,
                    OwnerId = Guid.CreateVersion7(),
                    Scopes = [ExternalApiKeyScopes.McpRead]
                }
            ]
        };
        using var client = factory.CreateClient();
        using var request = CreateMcpRequest();
        request.Headers.Add("X-API-Key", rawApiKey);
        request.Headers.Add(TenantHeaderNames.TenantSlug, "beta");

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task McpEndpoint_WithMcpReadOnlyApiKey_CannotDiscoverPrivateEventReadsOrProposalTools()
    {
        const string keyId = "mcp-read-only-key";
        const string secret = "mcp-read-only-secret";
        var rawApiKey = ApiKeyHashing.FormatPersistedApiKey(keyId, secret);

        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            DeploymentMode = DeploymentMode.SingleTenant,
            DefaultTenantId = PlatformDefaults.DefaultTenantId,
            McpEnabled = true,
            PersistedApiKeys =
            [
                new ExternalApiPhase0WebApplicationFactory.PersistedApiKeySeed
                {
                    KeyId = keyId,
                    Secret = secret,
                    TenantId = PlatformDefaults.DefaultTenantId,
                    OwnerId = Guid.CreateVersion7(),
                    Scopes = [ExternalApiKeyScopes.McpRead]
                }
            ]
        };
        using var client = factory.CreateClient();
        using var listRequest = CreateMcpRequest();
        listRequest.Headers.Add("X-API-Key", rawApiKey);
        using var resourceTemplatesRequest = CreateMcpListRequest("resources/templates/list");
        resourceTemplatesRequest.Headers.Add("X-API-Key", rawApiKey);

        var listResponse = await client.SendAsync(listRequest);
        var resourceTemplatesResponse = await client.SendAsync(resourceTemplatesRequest);

        await Assert.That(listResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var toolNames = await ReadToolNamesAsync(listResponse);
        await Assert.That(toolNames).Contains("list_ai_tool_contracts");
        await AssertEventManagementReadsHidden(toolNames);
        await AssertProposalToolsHidden(toolNames);

        await Assert.That(resourceTemplatesResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var resourceTemplateNames = await ReadResourceTemplateNamesAsync(resourceTemplatesResponse);
        await Assert.That(resourceTemplateNames).DoesNotContain("event_management_context");

        using var eventReadCallRequest = CreateMcpToolCallRequest("list_my_events", new { });
        eventReadCallRequest.Headers.Add("X-API-Key", rawApiKey);

        var eventReadCallResponse = await client.SendAsync(eventReadCallRequest);

        await Assert.That(eventReadCallResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var eventReadErrorDocument = await ReadJsonRpcDocumentAsync(eventReadCallResponse);
        await Assert.That(eventReadErrorDocument.RootElement.TryGetProperty("error", out _)).IsTrue().Because(eventReadErrorDocument.RootElement.GetRawText());
        await Assert.That(eventReadErrorDocument.RootElement.GetRawText()).DoesNotContain(rawApiKey);

        using var callRequest = CreateMcpToolCallRequest("propose_create_event_draft", new
        {
            conversationId = Guid.CreateVersion7(),
            title = "Read-only key should not propose"
        });
        callRequest.Headers.Add("X-API-Key", rawApiKey);

        var callResponse = await client.SendAsync(callRequest);

        await Assert.That(callResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var errorDocument = await ReadJsonRpcDocumentAsync(callResponse);
        await Assert.That(errorDocument.RootElement.TryGetProperty("error", out _)).IsTrue().Because(errorDocument.RootElement.GetRawText());
        await Assert.That(errorDocument.RootElement.GetRawText()).DoesNotContain(rawApiKey);
    }

    [Test]
    public async Task McpEndpoint_WithMcpReadAndEventReadApiKey_CanDiscoverEventManagementReadsButNotProposalTools()
    {
        const string keyId = "mcp-event-read-key";
        const string secret = "mcp-event-read-secret";
        var rawApiKey = ApiKeyHashing.FormatPersistedApiKey(keyId, secret);

        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            DeploymentMode = DeploymentMode.SingleTenant,
            DefaultTenantId = PlatformDefaults.DefaultTenantId,
            McpEnabled = true,
            PersistedApiKeys =
            [
                new ExternalApiPhase0WebApplicationFactory.PersistedApiKeySeed
                {
                    KeyId = keyId,
                    Secret = secret,
                    TenantId = PlatformDefaults.DefaultTenantId,
                    OwnerId = Guid.CreateVersion7(),
                    Scopes = [ExternalApiKeyScopes.McpRead, ExternalApiKeyScopes.EventsRead]
                }
            ]
        };
        using var client = factory.CreateClient();
        using var listRequest = CreateMcpRequest();
        listRequest.Headers.Add("X-API-Key", rawApiKey);
        using var resourceTemplatesRequest = CreateMcpListRequest("resources/templates/list");
        resourceTemplatesRequest.Headers.Add("X-API-Key", rawApiKey);

        var listResponse = await client.SendAsync(listRequest);
        var resourceTemplatesResponse = await client.SendAsync(resourceTemplatesRequest);

        await Assert.That(listResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var toolNames = await ReadToolNamesAsync(listResponse);
        await Assert.That(toolNames).Contains("list_ai_tool_contracts");
        await AssertEventManagementReadsDiscoverable(toolNames);
        await AssertProposalToolsHidden(toolNames);

        await Assert.That(resourceTemplatesResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var resourceTemplateNames = await ReadResourceTemplateNamesAsync(resourceTemplatesResponse);
        await Assert.That(resourceTemplateNames).Contains("event_management_context");
    }

    [Test]
    public async Task McpEndpoint_WithValidApiKeyWithoutMcpScope_CannotDiscoverAuthenticatedEventReads()
    {
        const string keyId = "mcp-unscoped-key";
        const string secret = "mcp-unscoped-secret";
        var rawApiKey = ApiKeyHashing.FormatPersistedApiKey(keyId, secret);

        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            DeploymentMode = DeploymentMode.SingleTenant,
            DefaultTenantId = PlatformDefaults.DefaultTenantId,
            McpEnabled = true,
            PersistedApiKeys =
            [
                new ExternalApiPhase0WebApplicationFactory.PersistedApiKeySeed
                {
                    KeyId = keyId,
                    Secret = secret,
                    TenantId = PlatformDefaults.DefaultTenantId,
                    OwnerId = Guid.CreateVersion7(),
                    Scopes = [ExternalApiKeyScopes.EventsRead]
                }
            ]
        };
        using var client = factory.CreateClient();
        using var request = CreateMcpRequest();
        request.Headers.Add("X-API-Key", rawApiKey);

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var toolNames = await ReadToolNamesAsync(response);
        await Assert.That(toolNames).Contains("list_ai_tool_contracts");
        await AssertEventManagementReadsHidden(toolNames);
        await AssertProposalToolsHidden(toolNames);
    }

    [Test]
    public async Task McpEndpoint_WithInstanceAdminApiKeyWithoutMcpPropose_CannotDiscoverOrCallProposalTools()
    {
        const string keyId = "mcp-instance-admin-no-propose-key";
        const string secret = "mcp-instance-admin-no-propose-secret";
        var rawApiKey = ApiKeyHashing.FormatPersistedApiKey(keyId, secret);

        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            DeploymentMode = DeploymentMode.SingleTenant,
            DefaultTenantId = PlatformDefaults.DefaultTenantId,
            McpEnabled = true,
            PersistedApiKeys =
            [
                new ExternalApiPhase0WebApplicationFactory.PersistedApiKeySeed
                {
                    KeyId = keyId,
                    Secret = secret,
                    OwnerType = ExternalApiKeyOwnerType.InstanceAdmin,
                    OwnerId = Guid.CreateVersion7(),
                    Scopes = [ExternalApiKeyScopes.McpRead, ExternalApiKeyScopes.AdminInstance]
                }
            ]
        };
        using var client = factory.CreateClient();
        using var listRequest = CreateMcpRequest();
        listRequest.Headers.Add("X-API-Key", rawApiKey);

        var listResponse = await client.SendAsync(listRequest);

        await Assert.That(listResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var toolNames = await ReadToolNamesAsync(listResponse);
        await AssertEventManagementReadsDiscoverable(toolNames);
        await AssertProposalToolsHidden(toolNames);

        using var callRequest = CreateMcpToolCallRequest("propose_heavy_moderate_event", new
        {
            conversationId = Guid.CreateVersion7(),
            eventId = Guid.CreateVersion7(),
            expectedConcurrencyStamp = Guid.CreateVersion7(),
            managementContextHasModerateHeavy = true,
            reasonCode = "policy-review",
            destructiveSummary = "Restrict event visibility until platform policy review completes.",
            confirmationPhrase = "HEAVY_MODERATE_EVENT",
            acknowledgedConsequences = true
        });
        callRequest.Headers.Add("X-API-Key", rawApiKey);

        var callResponse = await client.SendAsync(callRequest);

        await Assert.That(callResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var errorDocument = await ReadJsonRpcDocumentAsync(callResponse);
        await Assert.That(errorDocument.RootElement.TryGetProperty("error", out _)).IsTrue().Because(errorDocument.RootElement.GetRawText());
        await Assert.That(errorDocument.RootElement.GetRawText()).DoesNotContain(rawApiKey);
    }

    [Test]
    public async Task McpEndpoint_RateLimiting_PartitionsAnonymousAndApiKeyTraffic()
    {
        const string keyId = "mcp-rate-limited-key";
        const string secret = "mcp-rate-limited-secret";
        var rawApiKey = ApiKeyHashing.FormatPersistedApiKey(keyId, secret);

        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            DeploymentMode = DeploymentMode.SingleTenant,
            DefaultTenantId = PlatformDefaults.DefaultTenantId,
            McpEnabled = true,
            DisableRateLimitingInTesting = false,
            GlobalRateLimitTokenLimit = 1,
            GlobalRateLimitTokensPerPeriod = 1,
            GlobalRateLimitReplenishPeriodSeconds = 60,
            PersistedApiKeys =
            [
                new ExternalApiPhase0WebApplicationFactory.PersistedApiKeySeed
                {
                    KeyId = keyId,
                    Secret = secret,
                    TenantId = PlatformDefaults.DefaultTenantId,
                    OwnerId = Guid.CreateVersion7(),
                    Scopes = [ExternalApiKeyScopes.McpRead]
                }
            ]
        };
        using var client = factory.CreateClient();

        using var firstAnonymousRequest = CreateMcpRequest();
        var firstAnonymousResponse = await client.SendAsync(firstAnonymousRequest);

        using var secondAnonymousRequest = CreateMcpRequest();
        var throttledAnonymousResponse = await client.SendAsync(secondAnonymousRequest);

        using var firstApiKeyRequest = CreateMcpRequest();
        firstApiKeyRequest.Headers.Add("X-API-Key", rawApiKey);
        var firstApiKeyResponse = await client.SendAsync(firstApiKeyRequest);

        using var secondApiKeyRequest = CreateMcpRequest();
        secondApiKeyRequest.Headers.Add("X-API-Key", rawApiKey);
        var throttledApiKeyResponse = await client.SendAsync(secondApiKeyRequest);

        await Assert.That(firstAnonymousResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(throttledAnonymousResponse.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(throttledAnonymousResponse.Headers.Contains("Retry-After")).IsTrue();
        await Assert.That(firstApiKeyResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(throttledApiKeyResponse.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(throttledApiKeyResponse.Headers.Contains("Retry-After")).IsTrue();
    }

    [Test]
    public async Task McpEndpoint_RateLimiting_PartitionsInvalidApiKeyWithAnonymousTrafficByIp()
    {
        const string invalidApiKey = "invalid-mcp-ip-partition-secret";

        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            DeploymentMode = DeploymentMode.SingleTenant,
            DefaultTenantId = PlatformDefaults.DefaultTenantId,
            McpEnabled = true,
            DisableRateLimitingInTesting = false,
            GlobalRateLimitTokenLimit = 1,
            GlobalRateLimitTokensPerPeriod = 1,
            GlobalRateLimitReplenishPeriodSeconds = 60
        };
        using var client = factory.CreateClient();

        using var invalidKeyRequest = CreateMcpRequest();
        invalidKeyRequest.Headers.Add("X-API-Key", invalidApiKey);
        var invalidKeyResponse = await client.SendAsync(invalidKeyRequest);

        using var throttledAnonymousRequest = CreateMcpRequest();
        var throttledAnonymousResponse = await client.SendAsync(throttledAnonymousRequest);
        var throttledBody = await throttledAnonymousResponse.Content.ReadAsStringAsync();

        await Assert.That(invalidKeyResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(throttledAnonymousResponse.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(throttledAnonymousResponse.Headers.Contains("Retry-After")).IsTrue();
        await Assert.That(throttledBody).DoesNotContain(invalidApiKey);
    }

    [Test]
    public async Task McpEndpoint_RateLimiting_PartitionsValidApiKeysByKeyId()
    {
        const string firstKeyId = "mcp-rate-key-one";
        const string firstSecret = "mcp-rate-secret-one";
        const string secondKeyId = "mcp-rate-key-two";
        const string secondSecret = "mcp-rate-secret-two";
        var firstRawApiKey = ApiKeyHashing.FormatPersistedApiKey(firstKeyId, firstSecret);
        var secondRawApiKey = ApiKeyHashing.FormatPersistedApiKey(secondKeyId, secondSecret);

        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            DeploymentMode = DeploymentMode.SingleTenant,
            DefaultTenantId = PlatformDefaults.DefaultTenantId,
            McpEnabled = true,
            DisableRateLimitingInTesting = false,
            GlobalRateLimitTokenLimit = 1,
            GlobalRateLimitTokensPerPeriod = 1,
            GlobalRateLimitReplenishPeriodSeconds = 60,
            PersistedApiKeys =
            [
                new ExternalApiPhase0WebApplicationFactory.PersistedApiKeySeed
                {
                    KeyId = firstKeyId,
                    Secret = firstSecret,
                    TenantId = PlatformDefaults.DefaultTenantId,
                    OwnerId = Guid.CreateVersion7(),
                    Scopes = [ExternalApiKeyScopes.McpRead]
                },
                new ExternalApiPhase0WebApplicationFactory.PersistedApiKeySeed
                {
                    KeyId = secondKeyId,
                    Secret = secondSecret,
                    TenantId = PlatformDefaults.DefaultTenantId,
                    OwnerId = Guid.CreateVersion7(),
                    Scopes = [ExternalApiKeyScopes.McpRead]
                }
            ]
        };
        using var client = factory.CreateClient();

        using var firstKeyRequest = CreateMcpRequest();
        firstKeyRequest.Headers.Add("X-API-Key", firstRawApiKey);
        var firstKeyResponse = await client.SendAsync(firstKeyRequest);

        using var secondKeyRequest = CreateMcpRequest();
        secondKeyRequest.Headers.Add("X-API-Key", secondRawApiKey);
        var secondKeyResponse = await client.SendAsync(secondKeyRequest);

        using var throttledFirstKeyRequest = CreateMcpRequest();
        throttledFirstKeyRequest.Headers.Add("X-API-Key", firstRawApiKey);
        var throttledFirstKeyResponse = await client.SendAsync(throttledFirstKeyRequest);
        var throttledBody = await throttledFirstKeyResponse.Content.ReadAsStringAsync();

        await Assert.That(firstKeyResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(secondKeyResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(throttledFirstKeyResponse.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(throttledFirstKeyResponse.Headers.Contains("Retry-After")).IsTrue();
        await Assert.That(throttledBody).DoesNotContain(firstRawApiKey);
        await Assert.That(throttledBody).DoesNotContain(secondRawApiKey);
    }

    [Test]
    public async Task McpEndpoint_WithInvalidApiKey_IsRateLimitedWithoutEchoingCredential()
    {
        const string invalidApiKey = "invalid-mcp-rate-limit-secret";

        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            DeploymentMode = DeploymentMode.SingleTenant,
            DefaultTenantId = PlatformDefaults.DefaultTenantId,
            McpEnabled = true,
            DisableRateLimitingInTesting = false,
            GlobalRateLimitTokenLimit = 1,
            GlobalRateLimitTokensPerPeriod = 1,
            GlobalRateLimitReplenishPeriodSeconds = 60
        };
        using var client = factory.CreateClient();

        using var firstRequest = CreateMcpRequest();
        firstRequest.Headers.Add("X-API-Key", invalidApiKey);
        var firstResponse = await client.SendAsync(firstRequest);

        using var throttledRequest = CreateMcpRequest();
        throttledRequest.Headers.Add("X-API-Key", invalidApiKey);
        var throttledResponse = await client.SendAsync(throttledRequest);
        var throttledBody = await throttledResponse.Content.ReadAsStringAsync();

        await Assert.That(firstResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(throttledResponse.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(throttledBody).DoesNotContain(invalidApiKey);
    }

    [Test]
    public async Task McpEndpoint_WithValidScopedApiKey_CanDiscoverAuthorizedProposalTools()
    {
        var ownerUserId = Guid.NewGuid();
        const string keyId = "mcp-user-key";
        const string secret = "mcp-user-secret";
        var rawApiKey = ApiKeyHashing.FormatPersistedApiKey(keyId, secret);

        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            DeploymentMode = DeploymentMode.SingleTenant,
            DefaultTenantId = PlatformDefaults.DefaultTenantId,
            McpEnabled = true,
            PersistedApiKeys =
            [
                new ExternalApiPhase0WebApplicationFactory.PersistedApiKeySeed
                {
                    KeyId = keyId,
                    Secret = secret,
                    TenantId = PlatformDefaults.DefaultTenantId,
                    OwnerId = ownerUserId,
                    OwnerType = ExternalApiKeyOwnerType.User,
                    Scopes = [ExternalApiKeyScopes.McpRead, ExternalApiKeyScopes.EventsRead, ExternalApiKeyScopes.McpPropose]
                }
            ]
        };
        using var client = factory.CreateClient();
        using var request = CreateMcpRequest();
        request.Headers.Add("X-API-Key", rawApiKey);

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var toolNames = await ReadToolNamesAsync(response);
        await Assert.That(toolNames).Contains("list_ai_tool_contracts");
        await AssertEventManagementReadsDiscoverable(toolNames);
        await AssertProposalToolsDiscoverable(toolNames);
    }

    [Test]
    public async Task McpEndpoint_WithBearerAndApiKey_ReturnsBadRequest()
    {
        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            DeploymentMode = DeploymentMode.SingleTenant,
            DefaultTenantId = PlatformDefaults.DefaultTenantId,
            McpEnabled = true
        };
        using var client = factory.CreateClient();
        using var request = CreateMcpRequest();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateJwt(Guid.NewGuid()));
        request.Headers.Add("X-API-Key", "conflicting-test-key");

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    private static AuthenticatedWebApplicationFactory CreateMcpEnabledFactory()
    {
        var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider()
        };
        factory.AdditionalConfiguration["Mcp:Enabled"] = "true";
        factory.AdditionalConfiguration["Mcp:EndpointPath"] = "/mcp";
        factory.AdditionalConfiguration["Mcp:Stateless"] = "true";
        factory.AdditionalConfiguration["Mcp:EnableLegacySse"] = "false";
        return factory;
    }

    private static HttpRequestMessage CreateMcpRequest()
        => CreateMcpListRequest("tools/list");

    private static async Task AssertProposalToolsHidden(IReadOnlyList<string> toolNames)
    {
        foreach (var toolName in ProposalToolNames)
        {
            await Assert.That(toolNames).DoesNotContain(toolName);
        }
    }

    private static async Task AssertEventManagementReadsHidden(IReadOnlyList<string> toolNames)
    {
        foreach (var toolName in EventManagementReadToolNames)
        {
            await Assert.That(toolNames).DoesNotContain(toolName);
        }
    }

    private static async Task AssertEventManagementReadsDiscoverable(IReadOnlyList<string> toolNames)
    {
        foreach (var toolName in EventManagementReadToolNames)
        {
            await Assert.That(toolNames).Contains(toolName);
        }
    }

    private static async Task AssertProposalToolsDiscoverable(
        IReadOnlyList<string> toolNames,
        bool includeGeneric = true)
    {
        var expectedNames = includeGeneric
            ? ProposalToolNames
            : ProjectedProposalToolNames;

        foreach (var toolName in expectedNames)
        {
            await Assert.That(toolNames).Contains(toolName);
        }
    }

    private static HttpRequestMessage CreateMcpListRequest(string method)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/mcp")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method
                }),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Add("ProtocolVersion", "2025-06-18");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        return request;
    }

    private static HttpRequestMessage CreateMcpToolCallRequest(string toolName, object arguments)
    {
        var requestBody = new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "tools/call",
            @params = new
            {
                name = toolName,
                arguments
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Add("ProtocolVersion", "2025-06-18");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        return request;
    }

    private static async Task<IReadOnlyList<string>> ReadToolNamesAsync(HttpResponseMessage response)
    {
        using var document = await ReadJsonRpcDocumentAsync(response);
        var result = document.RootElement.GetProperty("result");

        return result.GetProperty("tools")
            .EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToArray();
    }

    private static async Task<IReadOnlyList<string>> ReadResourceTemplateNamesAsync(HttpResponseMessage response)
    {
        using var document = await ReadJsonRpcDocumentAsync(response);
        var result = document.RootElement.GetProperty("result");

        return result.GetProperty("resourceTemplates")
            .EnumerateArray()
            .Select(template => template.GetProperty("name").GetString())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToArray();
    }

    private static async Task<JsonDocument> ReadJsonRpcDocumentAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        var trimmed = body.TrimStart();
        if (trimmed.StartsWith('{'))
        {
            return JsonDocument.Parse(trimmed);
        }

        foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var payload = line[5..].Trim();
            if (payload.StartsWith('{'))
            {
                return JsonDocument.Parse(payload);
            }
        }

        throw new InvalidOperationException("The MCP response did not contain a JSON-RPC message.");
    }
}
