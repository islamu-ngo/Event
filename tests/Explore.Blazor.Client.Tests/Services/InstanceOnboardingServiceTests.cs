// ABOUTME: Unit tests for InstanceOnboardingService BFF endpoint mapping and command behavior.
// ABOUTME: Covers typed startup status mapping, onboarding/admin settings, auth provider flows, and storage HAL affordances.

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Explore.Blazor.Client.Helpers;
using Microsoft.AspNetCore.Components;
using Refit;

namespace Explore.Blazor.Client.Tests.Services;

public class InstanceOnboardingServiceTests
{
    private const int KeycloakProviderId = 1;

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> _bffHandler = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    private Func<HttpRequestMessage, Task<HttpResponseMessage>> _authHandler = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    private readonly IBffAuthApi _bffAuthApi;
    private readonly ILogger<InstanceOnboardingService> _logger;
    private readonly NavigationManager _navigation;
    private readonly InstanceOnboardingService _service;

    public InstanceOnboardingServiceTests()
    {
        _logger = Substitute.For<ILogger<InstanceOnboardingService>>();
        _navigation = new TestNavigationManager("https://localhost/");
        var client = new HttpClient(new MockHttpMessageHandler(request => _bffHandler(request)))
        {
            BaseAddress = new Uri("https://test.local")
        };
        var authClient = new HttpClient(new MockHttpMessageHandler(request => _authHandler(request)))
        {
            BaseAddress = new Uri("https://test.local")
        };
        _bffAuthApi = RestService.For<IBffAuthApi>(authClient);
        _service = new InstanceOnboardingService(
            new InstanceAuthenticationSettingsClient(client),
            new InstanceAuthorizationSettingsClient(client),
            new InstanceGovernanceSettingsClient(client),
            new InstanceMessagingSettingsClient(client),
            new InstanceOnboardingClient(client),
            new InstancePresentationSettingsClient(client),
            new InstanceStorageSettingsClient(client),
            new SystemClient(client),
            new TenantClient(client),
            _bffAuthApi,
            _logger,
            _navigation);
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager(string baseUri) => Initialize(baseUri, baseUri);
    }

    #region GetStatusAsync

    [Test]
    public async Task GetSystemOnboardingStatusAsync_ReturnsStatus_WhenApiSucceeds()
    {
        // Arrange
        var expected = new SystemOnboardingStatusDto
        {
            RequiresOnboarding = true,
            DeploymentMode = "MultiTenant"
        };
        SetupBffClient(CreateJsonResponse(expected));

        // Act
        var result = await _service.GetSystemOnboardingStatusAsync();

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.RequiresOnboarding).IsTrue();
        await Assert.That(result.DeploymentMode).IsEqualTo("MultiTenant");
    }

    [Test]
    public async Task GetStatusAsync_ReturnsStatus_WhenApiSucceeds()
    {
        // Arrange
        var expected = new HalResourceOfInstanceOnboardingStatusDto
        {
            IsCompleted = true,
            IsAuthenticated = true,
            IsCurrentUserInstanceAdmin = true,
            SelectedDeploymentMode = "SingleTenant",
            _links = new Dictionary<string, HalLink>
            {
                ["manage-authentication"] = new HalLink { Href = "/api/instance/auth-provider" }
            }
        };
        SetupBffClient(CreateJsonResponse(expected));

        // Act
        var result = await _service.GetStatusAsync();

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.IsCompleted).IsTrue();
        await Assert.That(result.SelectedDeploymentMode).IsEqualTo("SingleTenant");
        await Assert.That(result.HasHalLink("manage-authentication")).IsTrue();
    }

    [Test]
    public async Task GetStatusAsync_ReturnsSetupFields_WhenApiSucceeds()
    {
        // Arrange
        var startedAt = new DateTime(2026, 2, 15, 10, 0, 0, DateTimeKind.Utc);
        var expected = new HalResourceOfInstanceOnboardingStatusDto
        {
            IsCompleted = false,
            IsSetupModeActive = true,
            SetupSecretFromEnvironment = true,
            SetupTimedOut = false,
            InstanceStartedAt = startedAt
        };
        SetupBffClient(CreateJsonResponse(expected));

        // Act
        var result = await _service.GetStatusAsync();

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.IsSetupModeActive).IsTrue();
        await Assert.That(result.SetupSecretFromEnvironment).IsTrue();
        await Assert.That(result.SetupTimedOut).IsFalse();
        await Assert.That(result.InstanceStartedAt).IsEqualTo(startedAt);
    }

    [Test]
    public async Task GetStatusAsync_ReturnsNull_WhenApiThrows()
    {
        // Arrange
        SetupBffClient(_ => throw new HttpRequestException("boom"));

        // Act
        var result = await _service.GetStatusAsync();

        // Assert
        await Assert.That(result).IsNull();
    }

    #endregion

    #region GetDeploymentModeAsync

    [Test]
    public async Task GetDeploymentModeAsync_ReturnsMode_WhenApiSucceeds()
    {
        // Arrange
        var expected = new DeploymentModeDto { Mode = DeploymentMode.MultiTenant };
        SetupBffClient(CreateJsonResponse(expected));

        // Act
        var result = await _service.GetDeploymentModeAsync();

        // Assert
        await Assert.That(result.Mode).IsEqualTo(DeploymentMode.MultiTenant);
    }

    [Test]
    public async Task GetDeploymentModeAsync_ReturnsDefault_WhenApiThrows()
    {
        // Arrange
        SetupBffClient(_ => throw new HttpRequestException("boom"));

        // Act
        var result = await _service.GetDeploymentModeAsync();

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Mode).IsNull();
    }

    #endregion

    [Test]
    [Arguments(HttpStatusCode.OK)]
    [Arguments(HttpStatusCode.Unauthorized)]
    [Arguments(HttpStatusCode.ServiceUnavailable)]
    public async Task LocalCompletionClearsTransientCredentialWithoutOrdinarySessionRefresh(HttpStatusCode status)
    {
        string password = $"Aa1!{Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24))}";
        var completion = new CompleteLocalInstanceOnboardingRequestDto
        {
            OperationId = Guid.CreateVersion7(), Username = "operator", TemporaryPassword = password,
            Settings = new CompleteInstanceOnboardingRequest()
        };
        bool ordinaryRefresh = false;
        _authHandler = _ =>
        {
            ordinaryRefresh = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        };
        bool correctWireRequest = false;
        _bffHandler = async request =>
        {
            using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            correctWireRequest = request.Method == HttpMethod.Post
                && request.RequestUri!.AbsolutePath.Equals("/api/instanceonboarding/complete-local", StringComparison.OrdinalIgnoreCase)
                && body.RootElement.GetProperty("operationId").GetGuid() == completion.OperationId
                && string.Equals(body.RootElement.GetProperty("temporaryPassword").GetString(), password, StringComparison.Ordinal);
            return new HttpResponseMessage(status)
            {
                Content = System.Net.Http.Json.JsonContent.Create(status == HttpStatusCode.OK
                    ? (object)new { success = true, id = completion.OperationId }
                    : new { status = (int)status, detail = password })
            };
        };
        var response = await _service.CompleteLocalAsync(completion);
        await Assert.That(correctWireRequest).IsTrue();
        await Assert.That(response.Success).IsEqualTo(status == HttpStatusCode.OK);
        await Assert.That(completion.TemporaryPassword).IsEqualTo(string.Empty);
        await Assert.That(ordinaryRefresh).IsFalse();
        await Assert.That(response.Message?.Contains(password, StringComparison.Ordinal) == true).IsFalse();
    }

    #region CompleteAsync

    [Test]
    public async Task CompleteAsync_ReturnsSuccess_WhenApiSucceeds()
    {
        // Arrange
        var commandResponse = new BaseCommandResponseOfGuid
        {
            Success = true,
            Message = "OK"
        };
        SetupBffClient(CreateJsonResponse(commandResponse));

        // Act
        var result = await _service.CompleteAsync(new CompleteInstanceOnboardingRequest
        {
            DeploymentMode = DeploymentMode.SingleTenant,
            InstanceName = "Test Instance"
        });

        // Assert
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Message).IsEqualTo("OK");
    }

    [Test]
    public async Task CompleteAsync_ReturnsFailure_WhenApiThrows()
    {
        // Arrange
        SetupBffClient(_ => throw new HttpRequestException("network failed"));

        // Act
        var result = await _service.CompleteAsync(new CompleteInstanceOnboardingRequest());

        // Assert
        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Message).IsEqualTo("Request failed.");
        await Assert.That(result.Errors).DoesNotContain("network failed");
    }

    [Test]
    public async Task CompleteAsync_ReturnsSafeFailure_WhenApiReturnsBadRequest()
    {
        // Arrange
        var problemDetails = new
        {
            title = "Validation failed.",
            status = 400,
            errors = new Dictionary<string, string[]>
            {
                ["DeploymentMode"] = new[] { "Invalid deployment mode." }
            }
        };
        SetupBffClient(CreateJsonResponse(problemDetails, HttpStatusCode.BadRequest));

        // Act
        var result = await _service.CompleteAsync(new CompleteInstanceOnboardingRequest());

        // Assert
        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Message).IsEqualTo("Validation failed.");
        await Assert.That(result.Errors).IsNotEmpty();
    }

    #endregion

    #region KeycloakBootstrapAsync

    [Test]
    public async Task BootstrapKeycloakRealmAsync_UsesSetupEndpointAndRefreshesAuthSchemes_WhenApiSucceeds()
    {
        // Arrange
        Uri? requestUri = null;
        HttpMethod? method = null;
        string? requestBody = null;
        var refreshCalled = false;
        var commandResponse = new BaseCommandResponseOfGuid { Success = true, Message = "Bootstrapped" };
        SetupBffClient(async request =>
        {
            requestUri = request.RequestUri;
            method = request.Method;
            requestBody = await request.Content!.ReadAsStringAsync();
            return CreateJsonResponse(commandResponse);
        });
        SetupBffSelfClient(request =>
        {
            refreshCalled = request.RequestUri?.AbsolutePath == "/bff/auth/refresh-schemes";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        // Act
        var result = await _service.BootstrapKeycloakRealmAsync(CreateKeycloakBootstrapRequest());

        // Assert
        await Assert.That(result.Success).IsTrue();
        await Assert.That(requestUri).IsNotNull();
        await Assert.That(requestUri!.AbsolutePath).IsEqualTo("/api/instanceonboarding/auth-provider-configuration/keycloak-bootstrap");
        await Assert.That(method).IsEqualTo(HttpMethod.Post);
        await Assert.That(requestBody).Contains("\"blazorRedirectUris\":[\"https://localhost/*\"]");
        await Assert.That(requestBody).Contains("\"blazorWebOrigins\":[\"\\u002B\"]");
        await Assert.That(refreshCalled).IsTrue();
    }

    [Test]
    public async Task BootstrapKeycloakRealmAsync_DoesNotRefreshAuthSchemes_WhenApiFails()
    {
        // Arrange
        var refreshCalled = false;
        var commandResponse = new BaseCommandResponseOfGuid { Success = false, Message = "Bootstrap failed" };
        SetupBffClient(CreateJsonResponse(commandResponse, HttpStatusCode.BadRequest));
        SetupBffSelfClient(request =>
        {
            refreshCalled = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        // Act
        var result = await _service.BootstrapKeycloakRealmAsync(CreateKeycloakBootstrapRequest());

        // Assert
        await Assert.That(result.Success).IsFalse();
        await Assert.That(refreshCalled).IsFalse();
    }

    #endregion

    #region RefreshAuthSessionAsync

    [Test]
    public async Task RefreshAuthSessionAsync_ReturnsTrue_WhenEndpointSucceeds()
    {
        // Arrange
        SetupBffSelfClient(new HttpResponseMessage(HttpStatusCode.OK));

        // Act
        var result = await _service.RefreshAuthSessionAsync();

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task RefreshAuthSessionAsync_ReturnsFalse_WhenEndpointFails()
    {
        // Arrange
        SetupBffSelfClient(new HttpResponseMessage(HttpStatusCode.Unauthorized));

        // Act
        var result = await _service.RefreshAuthSessionAsync();

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task RefreshAuthSessionAsync_ReturnsFalse_WhenEndpointThrows()
    {
        // Arrange
        SetupBffSelfClient(_ => throw new HttpRequestException("refresh failed"));

        // Act
        var result = await _service.RefreshAuthSessionAsync();

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task RefreshAuthSessionAsync_UsesInternalEndpoint_ForServerSideSelfCall()
    {
        // Arrange
        Uri? requestUri = null;
        SetupBffSelfClient(request =>
        {
            requestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        // Act
        var result = await _service.RefreshAuthSessionAsync();

        // Assert
        await Assert.That(result).IsTrue();
        await Assert.That(requestUri).IsNotNull();
        await Assert.That(requestUri!.AbsolutePath).IsEqualTo("/bff/auth/refresh-session/internal");
    }

    #endregion

    #region AuthProviderConfigurationAsync

    [Test]
    public async Task GetAuthProviderConfigurationAsync_UsesPublicRedactedEndpoint()
    {
        // Arrange
        Uri? requestUri = null;
        var expected = new HalResourceOfAuthProviderConfigurationDto
        {
            PrimaryProviderId = KeycloakProviderId,
            KeycloakAuthority = "https://keycloak.example.com/auth/realms/ISLAMU",
            KeycloakClientId = "islamu-event-blazor",
            KeycloakClientSecret = string.Empty,
            KeycloakDetectedFromEnvironment = true,
            _links = new Dictionary<string, HalLink> { ["verify-email"] = new() { Href = "/api/auth/local/email-verifications" } }
        };
        SetupBffClient(request =>
        {
            requestUri = request.RequestUri;
            return Task.FromResult(CreateJsonResponse(expected));
        });

        // Act
        var result = await _service.GetAuthProviderConfigurationAsync();

        // Assert
        await Assert.That(requestUri).IsNotNull();
        await Assert.That(requestUri!.AbsolutePath).IsEqualTo("/api/instanceonboarding/auth-provider-configuration");
        await Assert.That(result.KeycloakDetectedFromEnvironment).IsTrue();
        await Assert.That(result.KeycloakClientSecret).IsEmpty();
        var links = (JsonElement)result.AdditionalProperties["_links"];
        await Assert.That(links.GetProperty("verify-email").GetProperty("href").GetString()).IsEqualTo("/api/auth/local/email-verifications");
    }

    [Test]
    public async Task GetAuthProviderConfigurationAsAdminAsync_UsesInstanceAdminEndpoint()
    {
        // Arrange
        Uri? requestUri = null;
        var expected = new AuthProviderConfigurationDto
        {
            PrimaryProviderId = KeycloakProviderId,
            KeycloakAuthority = "https://keycloak.example.com/realms/ISLAMU",
            KeycloakClientId = "islamu-event-blazor"
        };
        SetupBffClient(request =>
        {
            requestUri = request.RequestUri;
            return Task.FromResult(CreateJsonResponse(expected));
        });

        // Act
        var result = await _service.GetAuthProviderConfigurationAsAdminAsync();

        // Assert
        await Assert.That(requestUri).IsNotNull();
        await Assert.That(requestUri!.AbsolutePath).IsEqualTo("/api/instance/settings/auth-provider");
        await Assert.That(result.KeycloakClientId).IsEqualTo("islamu-event-blazor");
    }

    [Test]
    public async Task UpdateAuthProviderConfigurationAsAdminAsync_SendsPrimaryAndAtprotoAxes()
    {
        string? requestBody = null;
        _bffHandler = async request =>
        {
            requestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync();
            return CreateJsonResponse(new BaseCommandResponseOfGuid
            {
                Success = true,
                Message = "Updated"
            });
        };

        var result = await _service.UpdateAuthProviderConfigurationAsAdminAsync(
            new AuthProviderConfigurationDto
            {
                PrimaryProviderId = 4,
                LockPrimaryProvider = true,
                AtprotoLoginEnabled = true,
                AtprotoPublicUrl = "https://events.example.test"
            });
        using var document = JsonDocument.Parse(requestBody!);
        JsonElement configuration = document.RootElement
            .GetProperty("configuration")
            .GetProperty("value");

        await Assert.That(result.Success).IsTrue();
        await Assert.That(configuration.GetProperty("primaryProviderId").GetInt32())
            .IsEqualTo(4);
        await Assert.That(configuration.GetProperty("lockPrimaryProvider").GetBoolean())
            .IsTrue();
        await Assert.That(configuration.GetProperty("atprotoLoginEnabled").GetBoolean())
            .IsTrue();
    }

    #endregion

    #region AuthorizationProviderAdminAsync

    [Test]
    public async Task GetAuthorizationProviderConfigurationAsAdminAsync_UsesAdminSettingsEndpoint()
    {
        // Arrange
        Uri? requestUri = null;
        var expected = new AuthorizationProviderConfigurationDto
        {
            Provider = "cerbos",
            CerbosGrpcEndpoint = "cerbosgrpc.local:3593",
            CerbosEndpointVerified = true
        };
        SetupBffClient(request =>
        {
            requestUri = request.RequestUri;
            return Task.FromResult(CreateJsonResponse(expected));
        });

        // Act
        var result = await _service.GetAuthorizationProviderConfigurationAsAdminAsync();

        // Assert
        await Assert.That(requestUri).IsNotNull();
        await Assert.That(requestUri!.AbsolutePath).IsEqualTo("/api/instance/settings/authz-provider");
        await Assert.That(result.Provider).IsEqualTo("cerbos");
        await Assert.That(result.CerbosEndpointVerified).IsTrue();
    }

    [Test]
    [Arguments(401)]
    [Arguments(403)]
    public async Task GetAuthorizationProviderConfigurationAsAdminAsync_WhenIdentityIsNotReady_ReturnsEmptyConfiguration(
        int statusCode)
    {
        SetupBffClient(new HttpResponseMessage((HttpStatusCode)statusCode));

        var result = await _service.GetAuthorizationProviderConfigurationAsAdminAsync();

        await Assert.That(result).IsNotNull();
        await Assert.That(result.Provider).IsNull();
    }

    [Test]
    [Arguments(true, false, "pending", true)]
    [Arguments(true, false, "failed", false)]
    [Arguments(false, true, null, true)]
    [Arguments(false, false, null, false)]
    public async Task ShouldSkipAuthorizationProviderStepAsync_UsesDeploymentOwnershipOrReadiness(
        bool managedByDeployment,
        bool configured,
        string? bootstrapStatus,
        bool expected)
    {
        SetupBffClient(CreateJsonResponse(new ProviderConfigurationStatusDto
        {
            AuthorizationProviderManagedByDeployment = managedByDeployment,
            Configured = configured,
            AuthorizationProviderBootstrapStatus = bootstrapStatus
        }));

        var result = await _service.ShouldSkipAuthorizationProviderStepAsync();

        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task ShouldSkipAuthorizationProviderStepAsync_WhenReadFails_ReturnsFalse()
    {
        SetupBffClient(_ => throw new HttpRequestException("unavailable"));

        var result = await _service.ShouldSkipAuthorizationProviderStepAsync();

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task UpdateAuthorizationProviderConfigurationAsAdminAsync_UsesAdminSettingsEndpoint()
    {
        // Arrange
        Uri? requestUri = null;
        HttpMethod? method = null;
        string? requestBody = null;
        var commandResponse = new BaseCommandResponseOfGuid { Success = true, Message = "Updated" };
        SetupBffClient(async request =>
        {
            requestUri = request.RequestUri;
            method = request.Method;
            requestBody = await request.Content!.ReadAsStringAsync();
            return CreateJsonResponse(commandResponse);
        });

        // Act
        var result = await _service.UpdateAuthorizationProviderConfigurationAsAdminAsync(new AuthorizationProviderConfigurationDto
        {
            Provider = "cerbos",
            CerbosGrpcEndpoint = "cerbosgrpc.local:3593"
        });

        // Assert
        await Assert.That(result.Success).IsTrue();
        await Assert.That(requestUri).IsNotNull();
        await Assert.That(requestUri!.AbsolutePath).IsEqualTo("/api/instance/settings/authz-provider");
        await Assert.That(method).IsEqualTo(HttpMethod.Patch);
        await Assert.That(requestBody).Contains("cerbos", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public async Task SyncAuthorizationPolicyPackageAsync_UsesSetupEndpoint()
    {
        // Arrange
        Uri? requestUri = null;
        HttpMethod? method = null;
        string? requestBody = null;
        var commandResponse = new BaseCommandResponseOfGuid { Success = true, Message = "Synced" };
        SetupBffClient(async request =>
        {
            requestUri = request.RequestUri;
            method = request.Method;
            requestBody = await request.Content!.ReadAsStringAsync();
            return CreateJsonResponse(commandResponse);
        });

        // Act
        var result = await _service.SyncAuthorizationPolicyPackageAsync(new AuthorizationPolicyPackageSyncRequestDto
        {
            AdminUsername = "one-time-admin",
            AdminPassword = "one-time-password"
        });

        // Assert
        await Assert.That(result.Success).IsTrue();
        await Assert.That(requestUri).IsNotNull();
        await Assert.That(requestUri!.AbsolutePath).IsEqualTo("/api/instanceonboarding/authz-provider-configuration/sync");
        await Assert.That(method).IsEqualTo(HttpMethod.Post);
        await Assert.That(requestBody).Contains("one-time-admin");
        await Assert.That(requestBody).Contains("one-time-password");
    }

    [Test]
    public async Task SyncAuthorizationPolicyPackageAsAdminAsync_UsesAdminEndpoint()
    {
        // Arrange
        Uri? requestUri = null;
        HttpMethod? method = null;
        var commandResponse = new BaseCommandResponseOfGuid { Success = true, Message = "Synced" };
        SetupBffClient(request =>
        {
            requestUri = request.RequestUri;
            method = request.Method;
            return Task.FromResult(CreateJsonResponse(commandResponse));
        });

        // Act
        var result = await _service.SyncAuthorizationPolicyPackageAsAdminAsync();

        // Assert
        await Assert.That(result.Success).IsTrue();
        await Assert.That(requestUri).IsNotNull();
        await Assert.That(requestUri!.AbsolutePath).IsEqualTo("/api/instance/settings/authz-provider/sync");
        await Assert.That(method).IsEqualTo(HttpMethod.Post);
    }

    #endregion

    #region ValidateSecretAsync

    [Test]
    public async Task ValidateSecretAsync_ReturnsValid_WhenApiSucceeds()
    {
        // Arrange
        var expected = new SetupSecretValidationResultDto { Valid = true };
        SetupBffClient(CreateJsonResponse(expected));

        // Act
        var result = await _service.ValidateSecretAsync("test-secret");

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Valid).IsTrue();
    }

    [Test]
    public async Task ValidateSecretAsync_ReturnsInvalid_WhenApiReturnsInvalid()
    {
        // Arrange
        var expected = new SetupSecretValidationResultDto { Valid = false };
        SetupBffClient(CreateJsonResponse(expected));

        // Act
        var result = await _service.ValidateSecretAsync("wrong-secret");

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Valid).IsFalse();
    }

    [Test]
    public async Task ValidateSecretAsync_ReturnsInvalid_WhenApiThrows()
    {
        // Arrange
        SetupBffClient(_ => throw new HttpRequestException("connection refused"));

        // Act
        var result = await _service.ValidateSecretAsync("any-secret");

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Valid).IsFalse();
    }

    #endregion

    #region UpdateModuleSettingsAsync

    [Test]
    public async Task UpdateModuleSettingsAsync_ReturnsSuccess_WhenApiSucceeds()
    {
        // Arrange
        var commandResponse = new BaseCommandResponseOfGuid
        {
            Success = true,
            Message = "Updated"
        };
        SetupBffClient(CreateJsonResponse(commandResponse));

        // Act
        var result = await _service.UpdateModuleSettingsAsync(new ModuleSettingsDto());

        // Assert
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Message).IsEqualTo("Updated");
    }

    [Test]
    public async Task UpdateModuleSettingsAsync_ReturnsFailure_WhenApiReturnsError()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("null", Encoding.UTF8, "application/json")
        };
        SetupBffClient(response);

        // Act
        var result = await _service.UpdateModuleSettingsAsync(new ModuleSettingsDto());

        // Assert
        await Assert.That(result.Success).IsFalse();
    }

    [Test]
    public async Task UpdateModuleSettingsAsync_SendsOnlySpecifiedPatchLeaf()
    {
        // Arrange
        string? requestJson = null;
        SetupBffClient(async request =>
        {
            requestJson = await request.Content!.ReadAsStringAsync();
            return CreateJsonResponse(new BaseCommandResponseOfGuid { Success = true });
        });

        // Act
        await _service.UpdateModuleSettingsAsync(new ModuleSettingsDto { EnableIslamicModule = true });

        // Assert
        using JsonDocument document = JsonDocument.Parse(requestJson!);
        JsonElement root = document.RootElement;
        JsonElement update = root.GetProperty("enableIslamicModule");
        await Assert.That(update.GetProperty("hasValue").GetBoolean()).IsTrue();
        await Assert.That(update.GetProperty("value").GetBoolean()).IsTrue();
        await Assert.That(root.GetProperty("enableTechModule").ValueKind).IsEqualTo(JsonValueKind.Null);
    }

    #endregion

    #region UpdateAiAssistantProviderConfigurationAsync

    [Test]
    public async Task UpdateAiAssistantProviderConfigurationAsync_SendsOneCoupledCredentialGroup()
    {
        string? requestJson = null;
        SetupBffClient(async request =>
        {
            requestJson = await request.Content!.ReadAsStringAsync();
            return CreateJsonResponse(new BaseCommandResponseOfGuid { Success = true });
        });

        await _service.UpdateAiAssistantProviderConfigurationAsync(new AiAssistantProviderConfigurationWriteDto
        {
            Provider = "openai-compatible",
            EndpointUrl = "https://ai.example.test/v1",
            ApiKey = "replacement-key",
            ModelId = "model-a",
            AllowedModelIds = ["model-a"]
        });

        using JsonDocument document = JsonDocument.Parse(requestJson!);
        JsonElement root = document.RootElement;
        JsonElement update = root.GetProperty("providerConfiguration");
        await Assert.That(update.GetProperty("hasValue").GetBoolean()).IsTrue();
        await Assert.That(update.GetProperty("value").GetProperty("apiKey").GetString()).IsEqualTo("replacement-key");
        await Assert.That(root.GetProperty("enabled").ValueKind).IsEqualTo(JsonValueKind.Null);
    }

    #endregion

    #region StorageSettingsAsync

    [Test]
    public async Task GetStorageSettingsAsync_MapsHalLinksAndUsage()
    {
        // Arrange
        var payload = new HalResourceOfInstanceStorageSettingsDto
        {
            Provider = StorageProviderOptions.Local,
            DefaultMaxUploadBytes = 10 * 1024 * 1024,
            DefaultTenantQuotaBytes = 1024L * 1024 * 1024,
            InstanceMaxUploadBytes = 100L * 1024 * 1024,
            LockTenantStorage = false,
            Usage = new Usage
            {
                UsedBytes = 4096,
                ReservedBytes = 2048,
                QuarantinedBytes = 0,
                ObjectCount = 3
            },
            ProviderStatus = new ProviderStatus
            {
                Provider = StorageProviderOptions.Local,
                IsAvailable = true,
                Message = "OK"
            },
            _links = new Dictionary<string, HalLink>
            {
                ["edit"] = new() { Href = "/api/instance/settings/storage", Method = "PATCH" },
                ["provider-test"] = new() { Href = "/api/instance/settings/storage/test", Method = "POST" },
                ["recalculate-usage"] = new() { Href = "/api/instance/settings/storage/recalculate-usage", Method = "POST" }
            }
        };
        SetupBffClient(CreateJsonResponse(payload));

        // Act
        var result = await _service.GetStorageSettingsAsync();

        // Assert
        await Assert.That(result.Provider).IsEqualTo(StorageProviderOptions.Local);
        await Assert.That(result.HasLink("edit")).IsTrue();
        await Assert.That(result.HasLink("provider-test")).IsTrue();
        await Assert.That(result.HasLink("recalculate-usage")).IsTrue();
        await Assert.That(result.Usage!.UsedBytes).IsEqualTo(4096);
        await Assert.That(result.ProviderStatus!.IsAvailable).IsTrue();
    }

    [Test]
    public async Task UpdateStorageSettingsAsync_UsesStorageSettingsEndpoint_WhenEditAffordancePresent()
    {
        // Arrange
        Uri? requestUri = null;
        HttpMethod? method = null;
        string? requestBody = null;
        SetupBffClient(async request =>
        {
            requestUri = request.RequestUri;
            method = request.Method;
            requestBody = await request.Content!.ReadAsStringAsync();
            return CreateJsonResponse(new BaseCommandResponseOfGuid { Success = true, Message = "Updated" });
        });

        // Act
        var result = await _service.UpdateStorageSettingsAsync(new HalResourceOfInstanceStorageSettingsDto
        {
            Provider = StorageProviderOptions.Local,
            DefaultMaxUploadBytes = 10 * 1024 * 1024,
            DefaultTenantQuotaBytes = 1024L * 1024 * 1024,
            InstanceMaxUploadBytes = 100L * 1024 * 1024,
            _links = new Dictionary<string, HalLink>
            {
                ["edit"] = new() { Href = "/api/instance/settings/storage", Method = "PATCH" }
            }
        });

        // Assert
        await Assert.That(result.Success).IsTrue();
        await Assert.That(requestUri).IsNotNull();
        await Assert.That(requestUri!.AbsolutePath).IsEqualTo("/api/instance/settings/storage");
        await Assert.That(method).IsEqualTo(HttpMethod.Patch);
        await Assert.That(requestBody).Contains("\"provider\":\"local\"", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public async Task UpdateStorageSettingsAsync_DoesNotCallApi_WhenEditAffordanceMissing()
    {
        // Arrange
        var called = false;
        SetupBffClient(request =>
        {
            called = true;
            return Task.FromResult(CreateJsonResponse(new BaseCommandResponseOfGuid { Success = true }));
        });

        // Act
        var result = await _service.UpdateStorageSettingsAsync(new HalResourceOfInstanceStorageSettingsDto());

        // Assert
        await Assert.That(result.Success).IsFalse();
        await Assert.That(called).IsFalse();
    }

    #endregion

    #region GetStartupStatusAsync

    [Test]
    [Arguments(null)]
    [Arguments("Local")]
    [Arguments("Keycloak")]
    [Arguments("Atproto")]
    public async Task GetStartupStatusAsync_MapsInteractivePending_FromCanonicalStateModeAndGeneration(string? provider)
    {
        SetupBffClient(CreateJsonResponse(CreateStatusResource(
            isCompleted: false,
            state: "InteractivePending",
            mode: "Interactive",
            provider: provider,
            generation: 3)));

        var status = await _service.GetStartupStatusAsync();

        await Assert.That(status.Disposition)
            .IsEqualTo(InstanceOnboardingStartupDisposition.InteractivePending);
        await Assert.That(status.Provider).IsEqualTo(provider);
        await Assert.That(status.Generation).IsEqualTo(3L);
    }

    [Test]
    [Arguments("Keycloak")]
    [Arguments("Atproto")]
    public async Task GetStartupStatusAsync_MapsConfiguredAdministratorPending_ForTheExactConfiguredProvider(
        string provider)
    {
        SetupBffClient(CreateJsonResponse(CreateStatusResource(
            isCompleted: false,
            state: "ConfiguredAdministratorPending",
            mode: "ConfiguredAdministrator",
            provider: provider,
            generation: 11)));

        var status = await _service.GetStartupStatusAsync();

        await Assert.That(status.Disposition)
            .IsEqualTo(InstanceOnboardingStartupDisposition.ConfiguredAdministratorPending);
        await Assert.That(status.Provider).IsEqualTo(provider);
        await Assert.That(status.Generation).IsEqualTo(11L);
    }

    [Test]
    public async Task GetStartupStatusAsync_MapsCompletedInteractive_AndSurfacesServerAuthority()
    {
        SetupBffClient(CreateJsonResponse(CreateStatusResource(
            isCompleted: true,
            state: "Completed",
            mode: "Interactive",
            provider: null,
            generation: 7,
            isAuthenticated: true,
            isCurrentUserInstanceAdmin: true,
            selectedDeploymentMode: "MultiTenant")));

        var status = await _service.GetStartupStatusAsync();

        await Assert.That(status.Disposition).IsEqualTo(InstanceOnboardingStartupDisposition.Completed);
        await Assert.That(status.IsAuthenticated).IsTrue();
        await Assert.That(status.IsCurrentUserInstanceAdmin).IsTrue();
        await Assert.That(status.SelectedDeploymentMode).IsEqualTo("MultiTenant");
        await Assert.That(status.Generation).IsEqualTo(7L);
    }

    [Test]
    public async Task GetStartupStatusAsync_MapsCompletedConfiguredAdministrator_AndKeepsTheProvider()
    {
        SetupBffClient(CreateJsonResponse(CreateStatusResource(
            isCompleted: true,
            state: "Completed",
            mode: "ConfiguredAdministrator",
            provider: "Atproto",
            generation: 4,
            isAuthenticated: true,
            isCurrentUserInstanceAdmin: false,
            selectedDeploymentMode: "SingleTenant")));

        var status = await _service.GetStartupStatusAsync();

        await Assert.That(status.Disposition).IsEqualTo(InstanceOnboardingStartupDisposition.Completed);
        await Assert.That(status.Provider).IsEqualTo("Atproto");
        await Assert.That(status.IsCurrentUserInstanceAdmin).IsFalse();
        await Assert.That(status.SelectedDeploymentMode).IsEqualTo("SingleTenant");
    }

    [Test]
    public async Task GetStartupStatusAsync_MapsCompletedConfiguredAdministrator_WhenProviderIsNull()
    {
        SetupBffClient(CreateJsonResponse(CreateStatusResource(
            isCompleted: true,
            state: "Completed",
            mode: "ConfiguredAdministrator",
            provider: null,
            generation: 5)));

        var status = await _service.GetStartupStatusAsync();

        await Assert.That(status.Disposition).IsEqualTo(InstanceOnboardingStartupDisposition.Completed);
        await Assert.That(status.Provider).IsNull();
        await Assert.That(status.Generation).IsEqualTo(5L);
    }

    [Test]
    [Arguments(false, "InteractivePending", "Interactive", "Unknown")]
    [Arguments(false, "ConfiguredAdministratorPending", "ConfiguredAdministrator", null)]
    [Arguments(false, "ConfiguredAdministratorPending", "ConfiguredAdministrator", "keycloak")]
    [Arguments(false, "ConfiguredAdministratorPending", "ConfiguredAdministrator", "Google")]
    [Arguments(false, "ConfiguredAdministratorPending", "Interactive", "Keycloak")]
    [Arguments(false, "InteractivePending", "ConfiguredAdministrator", null)]
    [Arguments(true, "InteractivePending", "Interactive", null)]
    [Arguments(false, "Completed", "Interactive", null)]
    [Arguments(true, "Bootstrapping", "Interactive", null)]
    [Arguments(true, "Completed", "Headless", null)]
    [Arguments(true, "completed", "Interactive", null)]
    [Arguments(true, null, null, null)]
    public async Task GetStartupStatusAsync_FailsClosed_WhenTheCanonicalContractIsInconsistent(
        bool isCompleted,
        string? state,
        string? mode,
        string? provider)
    {
        SetupBffClient(CreateJsonResponse(CreateStatusResource(
            isCompleted,
            state,
            mode,
            provider,
            generation: 9)));

        var status = await _service.GetStartupStatusAsync();

        await Assert.That(status).IsEqualTo(InstanceOnboardingStartupStatus.Unavailable);
    }

    [Test]
    [Arguments(0L)]
    [Arguments(-1L)]
    public async Task GetStartupStatusAsync_FailsClosed_WhenGenerationIsNotPositive(long generation)
    {
        SetupBffClient(CreateJsonResponse(CreateStatusResource(
            isCompleted: false,
            state: "InteractivePending",
            mode: "Interactive",
            provider: null,
            generation: generation)));

        var status = await _service.GetStartupStatusAsync();

        await Assert.That(status).IsEqualTo(InstanceOnboardingStartupStatus.Unavailable);
    }

    [Test]
    public async Task GetStartupStatusAsync_FailsClosed_WhenGenerationIsAbsent()
    {
        SetupBffClient(CreateJsonResponse(CreateStatusResource(
            isCompleted: true,
            state: "Completed",
            mode: "Interactive",
            provider: null,
            generation: null,
            isAuthenticated: true,
            isCurrentUserInstanceAdmin: true,
            selectedDeploymentMode: "MultiTenant")));

        var status = await _service.GetStartupStatusAsync();

        await Assert.That(status).IsEqualTo(InstanceOnboardingStartupStatus.Unavailable);
    }

    [Test]
    public async Task GetStartupStatusAsync_WithholdsAuthorityAndModeValues_WhenTheContractIsInconsistent()
    {
        SetupBffClient(CreateJsonResponse(CreateStatusResource(
            isCompleted: true,
            state: "ConfiguredAdministratorPending",
            mode: "ConfiguredAdministrator",
            provider: "Keycloak",
            generation: 12,
            isAuthenticated: true,
            isCurrentUserInstanceAdmin: true,
            selectedDeploymentMode: "MultiTenant")));

        var status = await _service.GetStartupStatusAsync();

        await Assert.That(status.Disposition).IsEqualTo(InstanceOnboardingStartupDisposition.Unavailable);
        await Assert.That(status.Provider).IsNull();
        await Assert.That(status.Generation).IsEqualTo(0L);
        await Assert.That(status.IsAuthenticated).IsFalse();
        await Assert.That(status.IsCurrentUserInstanceAdmin).IsFalse();
        await Assert.That(status.SelectedDeploymentMode).IsNull();
    }

    [Test]
    public async Task GetStartupStatusAsync_FailsClosed_WhenOnlyLegacyBooleanAndExtensionFieldsArePresent()
    {
        SetupBffClient(CreateRawJsonResponse(
            """
            {
              "isCompleted": false,
              "requiresOnboarding": true,
              "bootstrapState": "InteractivePending",
              "bootstrapMode": "Interactive",
              "generation": 5,
              "isAuthenticated": true,
              "isCurrentUserInstanceAdmin": true,
              "selectedDeploymentMode": "MultiTenant"
            }
            """));

        var status = await _service.GetStartupStatusAsync();

        await Assert.That(status).IsEqualTo(InstanceOnboardingStartupStatus.Unavailable);
    }

    [Test]
    public async Task GetStartupStatusAsync_PrefersCanonicalFields_OverContradictingExtensionData()
    {
        SetupBffClient(CreateRawJsonResponse(
            """
            {
              "isCompleted": false,
              "state": "InteractivePending",
              "mode": "Interactive",
              "generation": 6,
              "completed": true,
              "requiresOnboarding": false,
              "legacyProvider": "Keycloak"
            }
            """));

        var status = await _service.GetStartupStatusAsync();

        await Assert.That(status.Disposition)
            .IsEqualTo(InstanceOnboardingStartupDisposition.InteractivePending);
        await Assert.That(status.Provider).IsNull();
        await Assert.That(status.Generation).IsEqualTo(6L);
    }

    [Test]
    public async Task GetStartupStatusAsync_FailsClosed_WhenTheStatusEndpointIsUnavailable()
    {
        SetupBffClient(_ => throw new HttpRequestException("status endpoint unreachable"));

        var status = await _service.GetStartupStatusAsync();

        await Assert.That(status).IsEqualTo(InstanceOnboardingStartupStatus.Unavailable);
    }

    [Test]
    public async Task GetStartupStatusAsync_PropagatesCancellation_WithoutReportingAStartupState()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.That(() => _service.GetStartupStatusAsync(cancellation.Token))
            .Throws<OperationCanceledException>();
    }

    #endregion

    private static HalResourceOfInstanceOnboardingStatusDto CreateStatusResource(
        bool isCompleted,
        string? state,
        string? mode,
        string? provider,
        long? generation,
        bool isAuthenticated = false,
        bool isCurrentUserInstanceAdmin = false,
        string? selectedDeploymentMode = null) => new()
        {
            IsCompleted = isCompleted,
            State = state,
            Mode = mode,
            Provider = provider,
            Generation = generation,
            IsAuthenticated = isAuthenticated,
            IsCurrentUserInstanceAdmin = isCurrentUserInstanceAdmin,
            SelectedDeploymentMode = selectedDeploymentMode
        };

    private static HttpResponseMessage CreateRawJsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static HttpResponseMessage CreateJsonResponse<T>(T model, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(model, options);
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static HttpResponseMessage CreateZipResponse(byte[] content, string fileName)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
        {
            FileName = $"\"{fileName}\""
        };
        return response;
    }

    private static KeycloakBootstrapRequestDto CreateKeycloakBootstrapRequest() =>
        new()
        {
            KeycloakBaseUrl = "https://keycloak.example.com",
            Realm = "ISLAMU",
            BlazorClientId = "islamu-event-blazor",
            BlazorClientSecret = "runtime-blazor-secret",
            ApiClientId = "islamu-event-api",
            ApiClientSecret = "runtime-api-secret",
            Mode = 0,
            BootstrapAdminUsername = "keycloak-admin",
            BootstrapAdminPassword = "one-time-admin-password"
        };

    private void SetupBffClient(HttpResponseMessage response)
    {
        SetupBffClient(_ => Task.FromResult(response));
    }

    private void SetupBffClient(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
    {
        _bffHandler = handler;
    }

    private void SetupBffSelfClient(HttpResponseMessage response)
    {
        SetupBffSelfClient(_ => Task.FromResult(response));
    }

    private void SetupBffSelfClient(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
    {
        _authHandler = handler;
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public MockHttpMessageHandler(HttpResponseMessage response)
        {
            _handler = _ => Task.FromResult(response);
        }

        public MockHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request);
    }

}
