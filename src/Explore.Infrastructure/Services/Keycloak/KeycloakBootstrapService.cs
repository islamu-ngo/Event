using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Onboarding;
using Explore.Application.Services;
using Explore.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Refit;

namespace Explore.Infrastructure.Services.Keycloak;

public sealed class KeycloakBootstrapService : IKeycloakBootstrapService
{
    public const string HttpClientName = "KeycloakBootstrapClient";

    private const string AdminCliClientId = "admin-cli";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IKeycloakRealmDesiredStateBuilder _desiredStateBuilder;
    private readonly ILogger<KeycloakBootstrapService> _logger;
    private readonly KeycloakBootstrapOptions _options;

    public KeycloakBootstrapService(
        IHttpClientFactory httpClientFactory,
        ILogger<KeycloakBootstrapService> logger)
        : this(httpClientFactory, logger, Options.Create(new KeycloakBootstrapOptions()), KeycloakRealmDesiredStateBuilder.CreateDefault())
    {
    }

    public KeycloakBootstrapService(
        IHttpClientFactory httpClientFactory,
        ILogger<KeycloakBootstrapService> logger,
        IOptions<KeycloakBootstrapOptions> options)
        : this(httpClientFactory, logger, options, KeycloakRealmDesiredStateBuilder.CreateDefault())
    {
    }

    public KeycloakBootstrapService(
        IHttpClientFactory httpClientFactory,
        ILogger<KeycloakBootstrapService> logger,
        IOptions<KeycloakBootstrapOptions> options,
        IKeycloakRealmDesiredStateBuilder desiredStateBuilder)
    {
        _httpClientFactory = httpClientFactory;
        _desiredStateBuilder = desiredStateBuilder;
        _logger = logger;
        _options = options.Value;
    }

    private IKeycloakAdminApi CreateApi(Uri baseUri)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        client.BaseAddress = new Uri(baseUri.ToString().TrimEnd('/'), UriKind.Absolute);
        return RestService.For<IKeycloakAdminApi>(
            client,
            new RefitSettings
            {
                ContentSerializer = new SystemTextJsonContentSerializer(JsonOptions),
                ExceptionFactory = _ => ValueTask.FromResult<Exception?>(null)
            });
    }

    private static string AuthorizationHeader(string accessToken) => $"Bearer {accessToken}";

    private static async Task<T?> ReadJsonResponseAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode || response.Content is null)
            return default;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private static void ThrowIfRequestFailed(IApiResponse response, CancellationToken cancellationToken)
    {
        if (!response.HasRequestError(out var requestError))
            return;

        cancellationToken.ThrowIfCancellationRequested();
        if (requestError.InnerException is OperationCanceledException)
            throw new OperationCanceledException("Keycloak request timed out before a response was received.", requestError);

        throw new HttpRequestException("Keycloak request failed before a response was received.", requestError);
    }

    public async Task<KeycloakBootstrapResultDto> BootstrapAsync(
        KeycloakBootstrapRequestDto request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryNormalizeBaseUri(request.KeycloakBaseUrl, _options.AllowLocalUrls, out var baseUri, out var failureCode))
        {
            return Failure(request, failureCode, "Keycloak base URL is not safe for bootstrap.");
        }

        try
        {
            var api = CreateApi(baseUri!);
            var accessToken = await RequestAdminTokenAsync(api, request, cancellationToken);
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return Failure(request, KeycloakFailureCodes.AuthFailed, "Keycloak admin authentication failed.");
            }

            var realmStatus = await EnsureRealmAsync(api, request, accessToken, cancellationToken);
            if (!realmStatus.Success)
                return realmStatus;

            var blazorUpdated = await EnsureClientSecretAsync(
                api,
                request.Realm,
                request.BlazorClientId,
                request.BlazorClientSecret,
                request.BlazorRedirectUris,
                request.BlazorWebOrigins,
                request.ApiClientId,
                accessToken,
                bearerOnly: false,
                cancellationToken);

            if (!blazorUpdated.Success)
                return Failure(request, blazorUpdated.FailureCode, blazorUpdated.Message, realmStatus.RealmCreated);

            var apiUpdated = false;
            if (!string.IsNullOrWhiteSpace(request.ApiClientId) && !string.IsNullOrWhiteSpace(request.ApiClientSecret))
            {
                var apiUpdate = await EnsureClientSecretAsync(
                    api,
                    request.Realm,
                    request.ApiClientId,
                    request.ApiClientSecret,
                    [],
                    [],
                    null,
                    accessToken,
                    bearerOnly: true,
                    cancellationToken);

                if (!apiUpdate.Success)
                    return Failure(request, apiUpdate.FailureCode, apiUpdate.Message, realmStatus.RealmCreated, blazorClientUpdated: true);

                apiUpdated = true;
            }

            _logger.LogInformation(
                "Keycloak bootstrap Admin API calls completed. Realm: {Realm}, BlazorClientId: {BlazorClientId}, ApiClientId: {ApiClientId}, Mode: {Mode}, RealmCreated: {RealmCreated}, ApiClientUpdated: {ApiClientUpdated}",
                request.Realm,
                request.BlazorClientId,
                request.ApiClientId,
                request.Mode,
                realmStatus.RealmCreated,
                apiUpdated);

            return new KeycloakBootstrapResultDto
            {
                Success = true,
                Message = "Keycloak bootstrap completed successfully.",
                Mode = request.Mode,
                Realm = request.Realm,
                BlazorClientId = request.BlazorClientId,
                ApiClientId = request.ApiClientId,
                RealmCreated = realmStatus.RealmCreated,
                BlazorClientUpdated = true,
                ApiClientUpdated = apiUpdated
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is OperationCanceledException or ApiRequestException { InnerException: OperationCanceledException })
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Failure(request, KeycloakFailureCodes.Timeout, "Keycloak bootstrap timed out before completion.");
        }
        catch (Exception ex) when (ex is HttpRequestException or ApiRequestException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Failure(request, KeycloakFailureCodes.Unreachable, "Keycloak Admin API was unreachable during bootstrap.");
        }
        catch (JsonException)
        {
            return Failure(request, KeycloakFailureCodes.InvalidResponse, "Keycloak Admin API returned an invalid response.");
        }
    }

    public async Task<KeycloakRealmDoctorResultDto> DiagnoseRealmAsync(
        AuthProviderConfigurationDto configuration,
        KeycloakRealmDoctorRequestDto request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var checks = new List<KeycloakRealmDoctorCheckDto>();
        var result = new KeycloakRealmDoctorResultDto
        {
            Authority = configuration.KeycloakAuthority,
            ClientId = configuration.KeycloakClientId,
            ApiClientId = request.ApiClientId
        };

        if (configuration.PrimaryProviderId != (int)AuthenticationProviderKind.Keycloak)
        {
            checks.Add(DoctorCheck(
                "keycloak_disabled",
                "Keycloak enabled",
                "blocked",
                "Keycloak sign-in is not enabled for this instance.",
                "Enable Keycloak before running realm diagnostics."));
            return CompleteDoctorResult(result, checks);
        }

        if (string.IsNullOrWhiteSpace(configuration.KeycloakAuthority)
            || string.IsNullOrWhiteSpace(configuration.KeycloakClientId))
        {
            checks.Add(DoctorCheck(
                "keycloak_configuration_missing",
                "Keycloak configuration",
                "blocked",
                "Keycloak authority and client ID must both be configured.",
                "Save the Keycloak authority URL and Blazor client ID, then rerun diagnostics."));
            return CompleteDoctorResult(result, checks);
        }

        if (!TryParseAuthority(configuration.KeycloakAuthority, _options.AllowLocalUrls, out var baseUri, out var realm, out var failureCode))
        {
            checks.Add(DoctorCheck(
                failureCode,
                "Authority URL",
                "blocked",
                "Keycloak authority URL is not valid or is not safe for diagnostics.",
                "Use a Keycloak realm authority such as https://keycloak.example.com/realms/islamu."));
            return CompleteDoctorResult(result, checks);
        }

        result.Realm = realm;
        checks.Add(DoctorCheck(
            "keycloak_configuration_present",
            "Keycloak configuration",
            "healthy",
            "Keycloak provider configuration is present."));

        var api = CreateApi(baseUri!);
        try
        {
            await AddDiscoveryCheckAsync(api, realm, checks, cancellationToken);
            if (HasBlockingCheck(checks))
                return CompleteDoctorResult(result, checks);

            if (!request.UseTemporaryAdminCredentials)
            {
                checks.Add(DoctorCheck(
                    "keycloak_admin_credentials_required",
                    "Deep realm inspection",
                    "warning",
                    "OIDC discovery is reachable. Temporary admin credentials are required to inspect clients, scopes, and offline_access mappings.",
                    "Run the doctor with temporary Keycloak admin credentials when you need drift details. The credentials are not stored."));
                return CompleteDoctorResult(result, checks);
            }

            if (string.IsNullOrWhiteSpace(request.BootstrapAdminUsername)
                || string.IsNullOrWhiteSpace(request.BootstrapAdminPassword))
            {
                checks.Add(DoctorCheck(
                    "keycloak_admin_credentials_missing",
                    "Temporary admin credentials",
                    "blocked",
                    "Temporary admin username and password are required for deep realm inspection.",
                    "Enter temporary credentials for a Keycloak realm administrator and rerun diagnostics."));
                return CompleteDoctorResult(result, checks);
            }

            var tokenRequest = new KeycloakBootstrapRequestDto
            {
                KeycloakBaseUrl = baseUri!.ToString(),
                Realm = realm,
                BlazorClientId = configuration.KeycloakClientId,
                BootstrapAdminUsername = request.BootstrapAdminUsername,
                BootstrapAdminPassword = request.BootstrapAdminPassword
            };

            var accessToken = await RequestAdminTokenAsync(api, tokenRequest, cancellationToken);
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                checks.Add(DoctorCheck(
                    "keycloak_admin_auth_failed",
                    "Temporary admin authentication",
                    "blocked",
                    "Keycloak admin authentication failed.",
                    "Verify the temporary admin username, password, and Keycloak master realm configuration."));
                return CompleteDoctorResult(result, checks);
            }

            await AddAdminRealmChecksAsync(api, realm, configuration.KeycloakClientId, request.ApiClientId, accessToken, checks, cancellationToken);
            return CompleteDoctorResult(result, checks);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is OperationCanceledException or ApiRequestException { InnerException: OperationCanceledException })
        {
            cancellationToken.ThrowIfCancellationRequested();
            checks.Add(DoctorCheck(
                KeycloakFailureCodes.Timeout,
                "Keycloak reachability",
                "blocked",
                "Keycloak diagnostics timed out before completion.",
                "Check network connectivity and retry."));
            return CompleteDoctorResult(result, checks);
        }
        catch (Exception ex) when (ex is HttpRequestException or ApiRequestException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            checks.Add(DoctorCheck(
                KeycloakFailureCodes.Unreachable,
                "Keycloak reachability",
                "blocked",
                "Keycloak was unreachable during diagnostics.",
                "Check the Keycloak authority URL, DNS, TLS, and firewall configuration."));
            return CompleteDoctorResult(result, checks);
        }
        catch (JsonException)
        {
            checks.Add(DoctorCheck(
                KeycloakFailureCodes.InvalidResponse,
                "Keycloak response format",
                "blocked",
                "Keycloak returned a response that could not be parsed safely.",
                "Verify the authority points to a Keycloak realm and retry."));
            return CompleteDoctorResult(result, checks);
        }
    }

    public async Task<KeycloakRealmSyncPlanDto> PreviewRealmSyncAsync(
        AuthProviderConfigurationDto configuration,
        KeycloakRealmSyncPreviewRequestDto request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var diagnostics = new List<KeycloakRealmDoctorCheckDto>();
        var operations = new List<KeycloakRealmSyncOperationDto>();
        var plan = new KeycloakRealmSyncPlanDto
        {
            Authority = configuration.KeycloakAuthority,
            ClientId = configuration.KeycloakClientId,
            ApiClientId = request.ApiClientId,
            DestructiveOperationsSupported = false
        };

        if (configuration.PrimaryProviderId != (int)AuthenticationProviderKind.Keycloak)
        {
            diagnostics.Add(DoctorCheck(
                "keycloak_disabled",
                "Keycloak enabled",
                "blocked",
                "Keycloak sign-in is not enabled for this instance.",
                "Enable Keycloak before generating a realm sync preview."));
            operations.Add(SyncOperation(
                "keycloak-disabled",
                "configuration",
                "auth-provider",
                "Keycloak",
                "none",
                "blocked",
                "Keycloak is disabled.",
                "A sync preview requires an enabled Keycloak provider."));
            return CompleteSyncPlan(plan, operations, diagnostics);
        }

        if (string.IsNullOrWhiteSpace(configuration.KeycloakAuthority)
            || string.IsNullOrWhiteSpace(configuration.KeycloakClientId))
        {
            diagnostics.Add(DoctorCheck(
                "keycloak_configuration_missing",
                "Keycloak configuration",
                "blocked",
                "Keycloak authority and client ID must both be configured.",
                "Save the Keycloak authority URL and Blazor client ID, then rerun the preview."));
            operations.Add(SyncOperation(
                "keycloak-configuration-missing",
                "configuration",
                "auth-provider",
                "Keycloak",
                "none",
                "blocked",
                "Keycloak provider configuration is incomplete.",
                "Authority and client ID are required before drift can be computed."));
            return CompleteSyncPlan(plan, operations, diagnostics);
        }

        if (!TryParseAuthority(configuration.KeycloakAuthority, _options.AllowLocalUrls, out var baseUri, out var realm, out var failureCode))
        {
            diagnostics.Add(DoctorCheck(
                failureCode,
                "Authority URL",
                "blocked",
                "Keycloak authority URL is not valid or is not safe for sync preview.",
                "Use a Keycloak realm authority such as https://keycloak.example.com/realms/islamu."));
            operations.Add(SyncOperation(
                "keycloak-authority-invalid",
                "configuration",
                "authority",
                "Keycloak authority",
                "none",
                "blocked",
                "Keycloak authority cannot be inspected safely.",
                "The sync preview blocks unsafe or malformed authority URLs."));
            return CompleteSyncPlan(plan, operations, diagnostics);
        }

        plan.Realm = realm;
        plan.DesiredState = BuildDesiredState(realm, configuration.KeycloakClientId, request);
        diagnostics.Add(DoctorCheck(
            "keycloak_configuration_present",
            "Keycloak configuration",
            "healthy",
            "Keycloak provider configuration is present."));

        var api = CreateApi(baseUri!);
        try
        {
            await AddDiscoveryCheckAsync(api, realm, diagnostics, cancellationToken);
            if (HasBlockingCheck(diagnostics))
                return CompleteSyncPlan(plan, operations, diagnostics);

            if (!request.UseTemporaryAdminCredentials)
            {
                operations.Add(SyncOperation(
                    "keycloak-admin-credentials-required",
                    "inspection",
                    "realm",
                    realm,
                    "none",
                    "blocked",
                    "Temporary admin credentials are required to compute a drift-aware sync plan.",
                    "The basic preview can show desired state, but Keycloak Admin API reads are required to compare current realm state."));
                return CompleteSyncPlan(plan, operations, diagnostics);
            }

            if (string.IsNullOrWhiteSpace(request.BootstrapAdminUsername)
                || string.IsNullOrWhiteSpace(request.BootstrapAdminPassword))
            {
                diagnostics.Add(DoctorCheck(
                    "keycloak_admin_credentials_missing",
                    "Temporary admin credentials",
                    "blocked",
                    "Temporary admin username and password are required for sync preview.",
                    "Enter temporary credentials for a Keycloak realm administrator and rerun the preview."));
                return CompleteSyncPlan(plan, operations, diagnostics);
            }

            var tokenRequest = new KeycloakBootstrapRequestDto
            {
                KeycloakBaseUrl = baseUri!.ToString(),
                Realm = realm,
                BlazorClientId = configuration.KeycloakClientId,
                BootstrapAdminUsername = request.BootstrapAdminUsername,
                BootstrapAdminPassword = request.BootstrapAdminPassword
            };

            var accessToken = await RequestAdminTokenAsync(api, tokenRequest, cancellationToken);
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                diagnostics.Add(DoctorCheck(
                    "keycloak_admin_auth_failed",
                    "Temporary admin authentication",
                    "blocked",
                    "Keycloak admin authentication failed.",
                    "Verify the temporary admin username, password, and Keycloak master realm configuration."));
                return CompleteSyncPlan(plan, operations, diagnostics);
            }

            await AddSyncPreviewOperationsAsync(api, realm, configuration.KeycloakClientId, request, accessToken, operations, diagnostics, cancellationToken);
            return CompleteSyncPlan(plan, operations, diagnostics);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is OperationCanceledException or ApiRequestException { InnerException: OperationCanceledException })
        {
            cancellationToken.ThrowIfCancellationRequested();
            diagnostics.Add(DoctorCheck(
                KeycloakFailureCodes.Timeout,
                "Keycloak reachability",
                "blocked",
                "Keycloak sync preview timed out before completion.",
                "Check network connectivity and retry."));
            return CompleteSyncPlan(plan, operations, diagnostics);
        }
        catch (Exception ex) when (ex is HttpRequestException or ApiRequestException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            diagnostics.Add(DoctorCheck(
                KeycloakFailureCodes.Unreachable,
                "Keycloak reachability",
                "blocked",
                "Keycloak was unreachable during sync preview.",
                "Check the Keycloak authority URL, DNS, TLS, and firewall configuration."));
            return CompleteSyncPlan(plan, operations, diagnostics);
        }
        catch (JsonException)
        {
            diagnostics.Add(DoctorCheck(
                KeycloakFailureCodes.InvalidResponse,
                "Keycloak response format",
                "blocked",
                "Keycloak returned a response that could not be parsed safely.",
                "Verify the authority points to a Keycloak realm and retry."));
            return CompleteSyncPlan(plan, operations, diagnostics);
        }
    }

    public async Task<KeycloakRealmSyncPlanDto> ApplyRealmSyncAsync(
        AuthProviderConfigurationDto configuration,
        KeycloakRealmSyncApplyRequestDto request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var previewRequest = new KeycloakRealmSyncPreviewRequestDto
        {
            UseTemporaryAdminCredentials = true,
            BootstrapAdminUsername = request.BootstrapAdminUsername,
            BootstrapAdminPassword = request.BootstrapAdminPassword,
            ApiClientId = request.ApiClientId,
            BlazorRedirectUris = request.BlazorRedirectUris,
            BlazorWebOrigins = request.BlazorWebOrigins
        };
        var diagnostics = new List<KeycloakRealmDoctorCheckDto>();
        var operations = new List<KeycloakRealmSyncOperationDto>();
        var plan = new KeycloakRealmSyncPlanDto
        {
            Authority = configuration.KeycloakAuthority,
            ClientId = configuration.KeycloakClientId,
            ApiClientId = request.ApiClientId,
            DestructiveOperationsSupported = false
        };

        if (!request.BackupConfirmed)
        {
            operations.Add(SyncOperation(
                "keycloak-backup-confirmation-required",
                "safety",
                "realm",
                "Keycloak backup",
                "none",
                "blocked",
                "Keycloak backup confirmation is required before applying realm repairs.",
                "Create or verify a Keycloak database backup, then confirm before applying additive changes."));
            return CompleteSyncPlan(plan, operations, diagnostics);
        }

        if (configuration.PrimaryProviderId != (int)AuthenticationProviderKind.Keycloak
            || string.IsNullOrWhiteSpace(configuration.KeycloakAuthority)
            || string.IsNullOrWhiteSpace(configuration.KeycloakClientId))
        {
            operations.Add(SyncOperation(
                "keycloak-configuration-missing",
                "configuration",
                "auth-provider",
                "Keycloak",
                "none",
                "blocked",
                "Keycloak provider configuration is incomplete.",
                "Enable Keycloak and save authority/client settings before applying realm repairs."));
            return CompleteSyncPlan(plan, operations, diagnostics);
        }

        if (!TryParseAuthority(configuration.KeycloakAuthority, _options.AllowLocalUrls, out var baseUri, out var realm, out var failureCode))
        {
            diagnostics.Add(DoctorCheck(
                failureCode,
                "Authority URL",
                "blocked",
                "Keycloak authority URL is not valid or is not safe for sync apply.",
                "Use a Keycloak realm authority such as https://keycloak.example.com/realms/islamu."));
            return CompleteSyncPlan(plan, operations, diagnostics);
        }

        plan.Realm = realm;
        plan.DesiredState = BuildDesiredState(realm, configuration.KeycloakClientId, previewRequest);

        if (string.IsNullOrWhiteSpace(request.BootstrapAdminUsername)
            || string.IsNullOrWhiteSpace(request.BootstrapAdminPassword))
        {
            diagnostics.Add(DoctorCheck(
                "keycloak_admin_credentials_missing",
                "Temporary admin credentials",
                "blocked",
                "Temporary admin username and password are required to apply Keycloak repairs.",
                "Enter temporary credentials for a Keycloak realm administrator and retry."));
            return CompleteSyncPlan(plan, operations, diagnostics);
        }

        var api = CreateApi(baseUri!);
        try
        {
            await AddDiscoveryCheckAsync(api, realm, diagnostics, cancellationToken);
            if (HasBlockingCheck(diagnostics))
                return CompleteSyncPlan(plan, operations, diagnostics);

            var tokenRequest = new KeycloakBootstrapRequestDto
            {
                KeycloakBaseUrl = baseUri!.ToString(),
                Realm = realm,
                BlazorClientId = configuration.KeycloakClientId,
                BootstrapAdminUsername = request.BootstrapAdminUsername,
                BootstrapAdminPassword = request.BootstrapAdminPassword
            };

            var accessToken = await RequestAdminTokenAsync(api, tokenRequest, cancellationToken);
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                diagnostics.Add(DoctorCheck(
                    "keycloak_admin_auth_failed",
                    "Temporary admin authentication",
                    "blocked",
                    "Keycloak admin authentication failed.",
                    "Verify the temporary admin username, password, and Keycloak master realm configuration."));
                return CompleteSyncPlan(plan, operations, diagnostics);
            }

            await AddSyncApplyOperationsAsync(
                api,
                realm,
                configuration,
                previewRequest,
                accessToken,
                operations,
                diagnostics,
                cancellationToken);
            return CompleteSyncPlan(plan, operations, diagnostics);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is OperationCanceledException or ApiRequestException { InnerException: OperationCanceledException })
        {
            cancellationToken.ThrowIfCancellationRequested();
            diagnostics.Add(DoctorCheck(
                KeycloakFailureCodes.Timeout,
                "Keycloak reachability",
                "blocked",
                "Keycloak sync apply timed out before completion.",
                "Check network connectivity and retry."));
            return CompleteSyncPlan(plan, operations, diagnostics);
        }
        catch (Exception ex) when (ex is HttpRequestException or ApiRequestException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            diagnostics.Add(DoctorCheck(
                KeycloakFailureCodes.Unreachable,
                "Keycloak reachability",
                "blocked",
                "Keycloak was unreachable during sync apply.",
                "Check the Keycloak authority URL, DNS, TLS, and firewall configuration."));
            return CompleteSyncPlan(plan, operations, diagnostics);
        }
        catch (JsonException)
        {
            diagnostics.Add(DoctorCheck(
                KeycloakFailureCodes.InvalidResponse,
                "Keycloak response format",
                "blocked",
                "Keycloak returned a response that could not be parsed safely.",
                "Verify the authority points to a Keycloak realm and retry."));
            return CompleteSyncPlan(plan, operations, diagnostics);
        }
    }

    public async Task<KeycloakClientSecretRotationResultDto> RotateClientSecretAsync(
        AuthProviderConfigurationDto configuration,
        KeycloakClientSecretRotationRequestDto request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var clientId = string.IsNullOrWhiteSpace(request.ClientId)
            ? configuration.KeycloakClientId
            : request.ClientId.Trim();
        var result = new KeycloakClientSecretRotationResultDto
        {
            ClientId = clientId,
            SecretOwnershipMode = "application-managed"
        };

        if (configuration.PrimaryProviderId != (int)AuthenticationProviderKind.Keycloak
            || string.IsNullOrWhiteSpace(configuration.KeycloakAuthority)
            || string.IsNullOrWhiteSpace(configuration.KeycloakClientId))
        {
            return CompleteRotationResult(
                result,
                "blocked",
                "Keycloak provider configuration is incomplete.",
                SyncOperation(
                    "keycloak-client-secret-configuration-missing",
                    "configuration",
                    "auth-provider",
                    "Keycloak",
                    "none",
                    "blocked",
                    "Keycloak provider configuration is incomplete.",
                    "Enable Keycloak and save authority/client settings before rotating the client secret."));
        }

        if (!clientId.Equals(configuration.KeycloakClientId, StringComparison.Ordinal))
        {
            return CompleteRotationResult(
                result,
                "blocked",
                "Only the configured Keycloak Blazor client secret can be rotated by this workflow.",
                SyncOperation(
                    "keycloak-client-secret-target-unsupported",
                    "client-secret",
                    "client",
                    clientId,
                    "none",
                    "blocked",
                    "Requested client is outside the managed rotation boundary.",
                    "Use the configured Keycloak client ID until the multi-project identity contract registry is available."));
        }

        if (string.IsNullOrWhiteSpace(request.NewClientSecret)
            || string.IsNullOrWhiteSpace(request.BootstrapAdminUsername)
            || string.IsNullOrWhiteSpace(request.BootstrapAdminPassword))
        {
            return CompleteRotationResult(
                result,
                "blocked",
                "New client secret and temporary admin credentials are required.",
                SyncOperation(
                    "keycloak-client-secret-rotation-inputs-missing",
                    "client-secret",
                    "client",
                    clientId,
                    "none",
                    "blocked",
                    "Rotation request is missing required application-managed inputs.",
                    "Enter the replacement client secret and temporary Keycloak admin credentials."));
        }

        if (!TryParseAuthority(configuration.KeycloakAuthority, _options.AllowLocalUrls, out var baseUri, out var realm, out var failureCode))
        {
            return CompleteRotationResult(
                result,
                "blocked",
                "Keycloak authority URL is not valid or is not safe for client-secret rotation.",
                SyncOperation(
                    failureCode,
                    "configuration",
                    "auth-provider",
                    "Keycloak authority",
                    "none",
                    "blocked",
                    "Keycloak authority URL is invalid or unsafe.",
                    "Use a Keycloak realm authority such as https://keycloak.example.com/realms/islamu."));
        }

        var api = CreateApi(baseUri!);
        try
        {
            var diagnostics = new List<KeycloakRealmDoctorCheckDto>();
            await AddDiscoveryCheckAsync(api, realm, diagnostics, cancellationToken);
            if (HasBlockingCheck(diagnostics))
            {
                return CompleteRotationResult(
                    result,
                    "blocked",
                    "Keycloak discovery is unreachable, so the client secret was not rotated.",
                    SyncOperation(
                        "keycloak-client-secret-discovery-blocked",
                        "client-secret",
                        "client",
                        clientId,
                        "none",
                        "blocked",
                        "Keycloak discovery failed before rotation.",
                        "Resolve Keycloak reachability before retrying."));
            }

            var tokenRequest = new KeycloakBootstrapRequestDto
            {
                KeycloakBaseUrl = baseUri!.ToString(),
                Realm = realm,
                BlazorClientId = configuration.KeycloakClientId,
                BootstrapAdminUsername = request.BootstrapAdminUsername,
                BootstrapAdminPassword = request.BootstrapAdminPassword
            };

            var accessToken = await RequestAdminTokenAsync(api, tokenRequest, cancellationToken);
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return CompleteRotationResult(
                    result,
                    "blocked",
                    "Keycloak admin authentication failed.",
                    SyncOperation(
                        "keycloak-client-secret-admin-auth-failed",
                        "client-secret",
                        "client",
                        clientId,
                        "none",
                        "blocked",
                        "Temporary admin authentication failed.",
                        "Verify the temporary admin username, password, and Keycloak master realm configuration."));
            }

            var lookup = await FindClientAsync(api, realm, clientId, accessToken, cancellationToken);
            if (!lookup.Success)
            {
                return CompleteRotationResult(
                    result,
                    "blocked",
                    "Keycloak client lookup failed.",
                    SyncOperation(
                        "keycloak-client-secret-client-lookup-failed",
                        "client-secret",
                        "client",
                        clientId,
                        "none",
                        "blocked",
                        "Keycloak client lookup failed before rotation.",
                        "Verify temporary admin permissions and retry."));
            }

            if (string.IsNullOrWhiteSpace(lookup.ClientUuid))
            {
                return CompleteRotationResult(
                    result,
                    "blocked",
                    "Configured Keycloak client does not exist.",
                    SyncOperation(
                        "keycloak-client-secret-client-missing",
                        "client-secret",
                        "client",
                        clientId,
                        "none",
                        "blocked",
                        "Configured Keycloak client was not found.",
                        "Run the Keycloak sync preview/apply workflow to create the managed client before rotating its secret."));
            }

            var representation = await GetClientRepresentationAsync(api, realm, lookup.ClientUuid, accessToken, cancellationToken);
            if (representation is null)
            {
                return CompleteRotationResult(
                    result,
                    "blocked",
                    "Keycloak client representation could not be read.",
                    SyncOperation(
                        "keycloak-client-secret-client-unreadable",
                        "client-secret",
                        "client",
                        clientId,
                        "none",
                        "blocked",
                        "Keycloak client representation could not be read before rotation.",
                        "Verify temporary admin permissions and retry."));
            }

            representation["secret"] = request.NewClientSecret;
            var updated = await UpdateClientRepresentationAsync(api, realm, lookup.ClientUuid, accessToken, representation, cancellationToken);
            if (!updated)
            {
                return CompleteRotationResult(
                    result,
                    "blocked",
                    "Keycloak rejected the client-secret rotation request.",
                    SyncOperation(
                        "keycloak-client-secret-update-failed",
                        "client-secret",
                        "client",
                        clientId,
                        "update",
                        "blocked",
                        "Keycloak client secret could not be updated.",
                        "Verify temporary admin permissions and retry."));
            }

            return CompleteRotationResult(
                result,
                "rotated",
                "Keycloak client secret was rotated and saved as an application-managed secret.",
                SyncOperation(
                    "keycloak-client-secret-rotated",
                    "client-secret",
                    "client",
                    clientId,
                    "update",
                    "applied",
                    "Keycloak client secret was rotated.",
                    "The new secret was sent to Keycloak and will be persisted by the Application handler. Secret values are not returned."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is OperationCanceledException or ApiRequestException { InnerException: OperationCanceledException })
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CompleteRotationResult(
                result,
                "blocked",
                "Keycloak client-secret rotation timed out.",
                SyncOperation(
                    "keycloak-client-secret-timeout",
                    "client-secret",
                    "client",
                    clientId,
                    "update",
                    "blocked",
                    "Keycloak rotation request timed out.",
                    "Check network connectivity and retry."));
        }
        catch (Exception ex) when (ex is HttpRequestException or ApiRequestException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CompleteRotationResult(
                result,
                "blocked",
                "Keycloak was unreachable during client-secret rotation.",
                SyncOperation(
                    "keycloak-client-secret-unreachable",
                    "client-secret",
                    "client",
                    clientId,
                    "update",
                    "blocked",
                    "Keycloak was unreachable during rotation.",
                    "Check the Keycloak authority URL, DNS, TLS, and firewall configuration."));
        }
        catch (JsonException)
        {
            return CompleteRotationResult(
                result,
                "blocked",
                "Keycloak returned a response that could not be parsed safely.",
                SyncOperation(
                    "keycloak-client-secret-invalid-response",
                    "client-secret",
                    "client",
                    clientId,
                    "update",
                    "blocked",
                    "Keycloak response format was invalid during rotation.",
                    "Verify the authority points to a Keycloak realm and retry."));
        }
    }

    private static bool TryNormalizeBaseUri(string keycloakBaseUrl, bool allowLocalUrls, out Uri? baseUri, out string failureCode)
    {
        baseUri = null;
        failureCode = KeycloakFailureCodes.InvalidUrl;

        if (!Uri.TryCreate(keycloakBaseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            return false;
        }

        if (!allowLocalUrls && IsBlockedHost(uri.Host))
        {
            failureCode = KeycloakFailureCodes.UnsafeHost;
            return false;
        }

        baseUri = new Uri(uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/");
        return true;
    }

    private static bool TryParseAuthority(
        string authority,
        bool allowLocalUrls,
        out Uri? baseUri,
        out string realm,
        out string failureCode)
    {
        baseUri = null;
        realm = string.Empty;
        failureCode = KeycloakFailureCodes.InvalidAuthority;

        if (!Uri.TryCreate(authority, UriKind.Absolute, out var authorityUri))
            return false;

        var segments = authorityUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var realmsIndex = Array.FindIndex(segments, segment => string.Equals(segment, "realms", StringComparison.OrdinalIgnoreCase));
        if (realmsIndex < 0 || realmsIndex + 1 >= segments.Length)
            return false;

        realm = Uri.UnescapeDataString(segments[realmsIndex + 1]);
        var basePath = string.Join('/', segments.Take(realmsIndex));
        var builder = new UriBuilder(authorityUri)
        {
            Path = basePath,
            Query = string.Empty,
            Fragment = string.Empty
        };

        return TryNormalizeBaseUri(builder.Uri.ToString(), allowLocalUrls, out baseUri, out failureCode);
    }

    private static bool IsBlockedHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(host, out var address))
            return false;

        if (IPAddress.IsLoopback(address))
            return true;

        if (address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.None)
            || address.Equals(IPAddress.IPv6None))
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        return address.AddressFamily switch
        {
            System.Net.Sockets.AddressFamily.InterNetwork =>
                bytes[0] == 169 && bytes[1] == 254 || bytes[0] >= 224,
            System.Net.Sockets.AddressFamily.InterNetworkV6 =>
                address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal,
            _ => true
        };
    }

    private static async Task<string?> RequestAdminTokenAsync(
        IKeycloakAdminApi api,
        KeycloakBootstrapRequestDto request,
        CancellationToken cancellationToken)
    {
        using var tokenResponse = await api.RequestAdminTokenAsync(
            new Dictionary<string, object>
            {
                ["grant_type"] = "password",
                ["client_id"] = AdminCliClientId,
                ["username"] = request.BootstrapAdminUsername,
                ["password"] = request.BootstrapAdminPassword
            },
            cancellationToken);

        if (!tokenResponse.IsSuccessStatusCode || tokenResponse.Content is null)
            return null;

        await using var stream = await tokenResponse.Content.ReadAsStreamAsync(cancellationToken);
        var token = await JsonSerializer.DeserializeAsync<KeycloakTokenResponse>(stream, JsonOptions, cancellationToken);
        return token?.AccessToken;
    }

    private async Task<KeycloakBootstrapResultDto> EnsureRealmAsync(
        IKeycloakAdminApi api,
        KeycloakBootstrapRequestDto request,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var checkResponse = await api.GetRealmAsync(request.Realm, AuthorizationHeader(accessToken), cancellationToken);
        ThrowIfRequestFailed(checkResponse, cancellationToken);
        if (checkResponse.IsSuccessStatusCode)
            return Success(request, realmCreated: false);

        if (checkResponse.StatusCode != HttpStatusCode.NotFound)
        {
            _logger.LogWarning(
                "Keycloak realm check failed. Realm: {Realm}, Mode: {Mode}, StatusCode: {StatusCode}",
                request.Realm,
                request.Mode,
                (int?)checkResponse.StatusCode);

            return Failure(request, KeycloakFailureCodes.RealmCheckFailed, "Keycloak realm status could not be verified.");
        }

        if (request.Mode != KeycloakBootstrapMode.CreateRealm)
            return Failure(request, KeycloakFailureCodes.RealmNotFound, "Keycloak realm was not found and create mode was not requested.");

        var createResponse = await api.CreateRealmAsync(
            AuthorizationHeader(accessToken),
            new KeycloakRealmRepresentation(request.Realm, Enabled: true),
            cancellationToken);
        ThrowIfRequestFailed(createResponse, cancellationToken);
        if (createResponse.IsSuccessStatusCode || createResponse.StatusCode == HttpStatusCode.Conflict)
            return Success(request, realmCreated: createResponse.StatusCode != HttpStatusCode.Conflict);

        _logger.LogWarning(
            "Keycloak realm create failed. Realm: {Realm}, StatusCode: {StatusCode}",
            request.Realm,
            (int?)createResponse.StatusCode);

        return Failure(request, KeycloakFailureCodes.RealmCreateFailed, "Keycloak realm could not be created.");
    }

    private async Task<ClientSecretUpdateResult> EnsureClientSecretAsync(
        IKeycloakAdminApi api,
        string realm,
        string clientId,
        string secret,
        IReadOnlyList<string>? redirectUris,
        IReadOnlyList<string>? webOrigins,
        string? apiClientId,
        string accessToken,
        bool bearerOnly,
        CancellationToken cancellationToken)
    {
        var lookup = await FindClientAsync(api, realm, clientId, accessToken, cancellationToken);
        if (!lookup.Success)
            return ClientSecretUpdateResult.Failure(KeycloakFailureCodes.ClientLookupFailed, "Keycloak client lookup failed.");

        var clientUuid = lookup.ClientUuid;
        if (clientUuid is null)
        {
            var createResult = await CreateClientAsync(api, realm, clientId, accessToken, bearerOnly, cancellationToken);
            if (!createResult.Success)
                return createResult;

            lookup = await FindClientAsync(api, realm, clientId, accessToken, cancellationToken);
            if (!lookup.Success)
                return ClientSecretUpdateResult.Failure(KeycloakFailureCodes.ClientLookupFailed, "Keycloak client lookup failed.");

            clientUuid = lookup.ClientUuid;
            if (clientUuid is null)
                return ClientSecretUpdateResult.Failure(KeycloakFailureCodes.ClientNotFound, "Keycloak client could not be located after creation.");
        }

        var clientRepresentation = await GetClientRepresentationAsync(api, realm, clientUuid, accessToken, cancellationToken);
        if (clientRepresentation is null)
            return ClientSecretUpdateResult.Failure(KeycloakFailureCodes.ClientLookupFailed, "Keycloak client representation could not be loaded.");

        var representationSecretMatches = string.Equals(
            clientRepresentation["secret"]?.GetValue<string>(),
            secret,
            StringComparison.Ordinal);

        if (!bearerOnly)
        {
            var roleResult = await EnsureOfflineAccessRealmRoleAsync(api, realm, accessToken, cancellationToken);
            if (!roleResult.Success)
                return roleResult;

            var scopeMappingResult = await EnsureOfflineAccessClientScopeMappingAsync(
                api,
                realm,
                accessToken,
                cancellationToken);
            if (!scopeMappingResult.Success)
                return scopeMappingResult;
        }

        if (representationSecretMatches
            && HasRequiredClientSettings(clientRepresentation, bearerOnly, redirectUris, webOrigins, apiClientId))
        {
            return ClientSecretUpdateResult.Succeeded();
        }

        var currentSecret = representationSecretMatches
            ? secret
            : await GetCurrentClientSecretAsync(api, realm, clientUuid, accessToken, cancellationToken);
        var secretMatches = string.Equals(currentSecret, secret, StringComparison.Ordinal);

        if (secretMatches
            && HasRequiredClientSettings(clientRepresentation, bearerOnly, redirectUris, webOrigins, apiClientId))
        {
            return ClientSecretUpdateResult.Succeeded();
        }

        if (!bearerOnly && !HasRefreshTokenSettings(clientRepresentation))
        {
            var scopeResult = await EnsureOfflineAccessScopeAsync(
                api,
                realm,
                clientUuid,
                accessToken,
                cancellationToken);
            if (!scopeResult.Success)
                return scopeResult;
        }

        if (!secretMatches)
            clientRepresentation["secret"] = secret;

        if (!bearerOnly)
            EnsureBlazorClientRuntimeSettings(clientRepresentation, redirectUris, webOrigins, apiClientId);

        var secretResponse = await api.UpdateClientRepresentationAsync(
            realm,
            clientUuid,
            AuthorizationHeader(accessToken),
            clientRepresentation,
            cancellationToken);
        ThrowIfRequestFailed(secretResponse, cancellationToken);
        if (secretResponse.IsSuccessStatusCode)
            return ClientSecretUpdateResult.Succeeded();

        _logger.LogWarning(
            "Keycloak client secret update failed. Realm: {Realm}, ClientId: {ClientId}, StatusCode: {StatusCode}",
            realm,
            clientId,
            (int?)secretResponse.StatusCode);

        return ClientSecretUpdateResult.Failure(
            KeycloakFailureCodes.ClientSecretUpdateFailed,
            "Keycloak client secret could not be updated.");
    }

    private static async Task<ClientLookupResult> FindClientAsync(
        IKeycloakAdminApi api,
        string realm,
        string clientId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var response = await api.FindClientsAsync(realm, clientId, AuthorizationHeader(accessToken), cancellationToken);
        var clients = await ReadJsonResponseAsync<List<KeycloakClientLookupResult>>(response, cancellationToken);
        if (clients is null)
            return ClientLookupResult.Failure();

        var clientUuid = clients.FirstOrDefault(x => string.Equals(x.ClientId, clientId, StringComparison.Ordinal))?.Id;
        return ClientLookupResult.Succeeded(clientUuid);
    }

    private static async Task<JsonObject?> GetClientRepresentationAsync(
        IKeycloakAdminApi api,
        string realm,
        string clientUuid,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var response = await api.GetClientRepresentationAsync(realm, clientUuid, AuthorizationHeader(accessToken), cancellationToken);
        return await ReadJsonResponseAsync<JsonObject>(response, cancellationToken);
    }

    private static async Task<string?> GetCurrentClientSecretAsync(
        IKeycloakAdminApi api,
        string realm,
        string clientUuid,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var response = await api.GetCurrentClientSecretAsync(realm, clientUuid, AuthorizationHeader(accessToken), cancellationToken);
        var representation = await ReadJsonResponseAsync<JsonObject>(response, cancellationToken);
        return representation?["value"]?.GetValue<string>();
    }

    private static bool HasRefreshTokenSettings(JsonObject clientRepresentation)
    {
        var hasOfflineAccess = ContainsScope(clientRepresentation["optionalClientScopes"], "offline_access")
            || ContainsScope(clientRepresentation["defaultClientScopes"], "offline_access");

        var attributes = clientRepresentation["attributes"] as JsonObject;
        var usesRefreshTokens = string.Equals(
            attributes?["use.refresh.tokens"]?.GetValue<string>(),
            "true",
            StringComparison.OrdinalIgnoreCase);

        return hasOfflineAccess && usesRefreshTokens;
    }

    private static bool HasRequiredClientSettings(
        JsonObject clientRepresentation,
        bool bearerOnly,
        IReadOnlyList<string>? redirectUris,
        IReadOnlyList<string>? webOrigins,
        string? apiClientId)
    {
        if (bearerOnly)
            return true;

        return clientRepresentation["enabled"]?.GetValue<bool>() == true
               && clientRepresentation["standardFlowEnabled"]?.GetValue<bool>() == true
               && HasRefreshTokenSettings(clientRepresentation)
               && ContainsAllStringValues(clientRepresentation["redirectUris"], redirectUris)
               && ContainsAllStringValues(clientRepresentation["webOrigins"], webOrigins)
               && HasPostLogoutRedirectSettings(clientRepresentation, redirectUris)
               && HasAudienceMapper(clientRepresentation, apiClientId);
    }

    private static bool ContainsAllStringValues(JsonNode? currentValuesNode, IReadOnlyList<string>? desiredValues)
    {
        var desired = (desiredValues ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        if (desired.Length == 0)
            return true;

        var current = GetStringArray(currentValuesNode);
        return desired.All(value => current.Contains(value, StringComparer.Ordinal));
    }

    private static bool HasPostLogoutRedirectSettings(
        JsonObject clientRepresentation,
        IReadOnlyList<string>? redirectUris)
    {
        if ((redirectUris ?? []).All(string.IsNullOrWhiteSpace))
            return true;

        var attributes = clientRepresentation["attributes"] as JsonObject;
        return !string.IsNullOrWhiteSpace(attributes?["post.logout.redirect.uris"]?.GetValue<string>());
    }

    private static bool HasAudienceMapper(JsonObject clientRepresentation, string? apiClientId)
    {
        if (string.IsNullOrWhiteSpace(apiClientId))
            return true;

        return clientRepresentation["protocolMappers"] is JsonArray mappers
               && mappers.OfType<JsonObject>().Any(mapper =>
               {
                   var config = mapper["config"] as JsonObject;
                   return string.Equals(mapper["protocolMapper"]?.GetValue<string>(), "oidc-audience-mapper", StringComparison.Ordinal)
                          && string.Equals(config?["included.client.audience"]?.GetValue<string>(), apiClientId, StringComparison.Ordinal)
                          && string.Equals(config?["access.token.claim"]?.GetValue<string>(), "true", StringComparison.OrdinalIgnoreCase);
               });
    }

    private static bool ContainsScope(JsonNode? scopesNode, string expectedScope)
    {
        return scopesNode is JsonArray scopes
            && scopes.Any(scope => string.Equals(scope?.GetValue<string>(), expectedScope, StringComparison.Ordinal));
    }

    private static async Task<ClientSecretUpdateResult> EnsureOfflineAccessRealmRoleAsync(
        IKeycloakAdminApi api,
        string realm,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var offlineRole = await GetRealmRoleAsync(api, realm, "offline_access", accessToken, cancellationToken);
        if (offlineRole is null)
        {
            return ClientSecretUpdateResult.Failure(
                KeycloakFailureCodes.OfflineAccessRoleNotFound,
                "Keycloak offline access realm role could not be located.");
        }

        var defaultRoleName = $"default-roles-{realm.ToLowerInvariant()}";
        var defaultRole = await GetRealmRoleAsync(api, realm, defaultRoleName, accessToken, cancellationToken);
        var defaultRoleId = defaultRole?["id"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(defaultRoleId))
        {
            return ClientSecretUpdateResult.Failure(
                KeycloakFailureCodes.DefaultRoleNotFound,
                "Keycloak default realm role could not be located.");
        }

        var response = await api.AddRealmRoleCompositesAsync(
            realm,
            defaultRoleId,
            AuthorizationHeader(accessToken),
            new JsonArray(offlineRole.DeepClone()),
            cancellationToken);
        ThrowIfRequestFailed(response, cancellationToken);
        return response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Conflict
            ? ClientSecretUpdateResult.Succeeded()
            : ClientSecretUpdateResult.Failure(
                KeycloakFailureCodes.OfflineAccessRoleUpdateFailed,
                "Keycloak offline access realm role could not be assigned to the default role.");
    }

    private static async Task<JsonObject?> GetRealmRoleAsync(
        IKeycloakAdminApi api,
        string realm,
        string roleName,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var response = await api.GetRealmRoleAsync(realm, roleName, AuthorizationHeader(accessToken), cancellationToken);
        return await ReadJsonResponseAsync<JsonObject>(response, cancellationToken);
    }

    private static async Task<ClientSecretUpdateResult> EnsureOfflineAccessScopeAsync(
        IKeycloakAdminApi api,
        string realm,
        string clientUuid,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var scopeId = await FindClientScopeIdAsync(api, realm, "offline_access", accessToken, cancellationToken);
        if (string.IsNullOrWhiteSpace(scopeId))
        {
            return ClientSecretUpdateResult.Failure(
                KeycloakFailureCodes.ClientScopeNotFound,
                "Keycloak offline access client scope could not be located.");
        }

        var response = await api.AssignOptionalClientScopeAsync(
            realm,
            clientUuid,
            scopeId,
            AuthorizationHeader(accessToken),
            cancellationToken);
        ThrowIfRequestFailed(response, cancellationToken);
        return response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Conflict
            ? ClientSecretUpdateResult.Succeeded()
            : ClientSecretUpdateResult.Failure(
                KeycloakFailureCodes.ClientScopeUpdateFailed,
                "Keycloak offline access client scope could not be assigned.");
    }

    private static async Task<ClientSecretUpdateResult> EnsureOfflineAccessClientScopeMappingAsync(
        IKeycloakAdminApi api,
        string realm,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var scopeId = await FindClientScopeIdAsync(api, realm, "offline_access", accessToken, cancellationToken);
        if (string.IsNullOrWhiteSpace(scopeId))
        {
            return ClientSecretUpdateResult.Failure(
                KeycloakFailureCodes.ClientScopeNotFound,
                "Keycloak offline access client scope could not be located.");
        }

        return await EnsureOfflineAccessScopeMappingAsync(
            api,
            realm,
            scopeId,
            accessToken,
            cancellationToken);
    }

    private static async Task<ClientSecretUpdateResult> EnsureOfflineAccessScopeMappingAsync(
        IKeycloakAdminApi api,
        string realm,
        string scopeId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var offlineRole = await GetRealmRoleAsync(api, realm, "offline_access", accessToken, cancellationToken);
        if (offlineRole is null)
        {
            return ClientSecretUpdateResult.Failure(
                KeycloakFailureCodes.OfflineAccessRoleNotFound,
                "Keycloak offline access realm role could not be located.");
        }

        var response = await api.AddClientScopeRealmMappingAsync(
            realm,
            scopeId,
            AuthorizationHeader(accessToken),
            new JsonArray(offlineRole.DeepClone()),
            cancellationToken);
        ThrowIfRequestFailed(response, cancellationToken);
        return response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Conflict
            ? ClientSecretUpdateResult.Succeeded()
            : ClientSecretUpdateResult.Failure(
                KeycloakFailureCodes.OfflineAccessScopeMappingFailed,
                "Keycloak offline access role could not be assigned to the offline access client scope.");
    }

    private static async Task<string?> FindClientScopeIdAsync(
        IKeycloakAdminApi api,
        string realm,
        string scopeName,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var response = await api.FindClientScopesAsync(realm, scopeName, AuthorizationHeader(accessToken), cancellationToken);
        var scopes = await ReadJsonResponseAsync<List<KeycloakClientScopeLookupResult>>(response, cancellationToken);
        return scopes?.FirstOrDefault(scope => string.Equals(scope.Name, scopeName, StringComparison.Ordinal))?.Id;
    }


    private static void PreserveRefreshTokenSettings(JsonObject clientRepresentation)
    {
        var optionalScopes = clientRepresentation["optionalClientScopes"] as JsonArray;
        if (optionalScopes is null)
        {
            optionalScopes = [];
            clientRepresentation["optionalClientScopes"] = optionalScopes;
        }

        if (!optionalScopes.Any(scope => string.Equals(scope?.GetValue<string>(), "offline_access", StringComparison.Ordinal)))
            optionalScopes.Add("offline_access");

        var attributes = clientRepresentation["attributes"] as JsonObject;
        if (attributes is null)
        {
            attributes = [];
            clientRepresentation["attributes"] = attributes;
        }

        attributes["use.refresh.tokens"] = "true";
    }

    private static void EnsureBlazorClientRuntimeSettings(
        JsonObject clientRepresentation,
        IReadOnlyList<string>? redirectUris,
        IReadOnlyList<string>? webOrigins,
        string? apiClientId)
    {
        clientRepresentation["enabled"] = true;
        clientRepresentation["protocol"] = "openid-connect";
        clientRepresentation["publicClient"] = false;
        clientRepresentation["bearerOnly"] = false;
        clientRepresentation["standardFlowEnabled"] = true;

        PreserveRefreshTokenSettings(clientRepresentation);
        AddMissingStringValues(clientRepresentation, "redirectUris", redirectUris ?? []);
        AddMissingStringValues(clientRepresentation, "webOrigins", webOrigins ?? []);
        EnsurePostLogoutRedirectSettings(clientRepresentation, redirectUris);
        EnsureAudienceMapper(clientRepresentation, apiClientId);
    }

    private static void EnsurePostLogoutRedirectSettings(
        JsonObject clientRepresentation,
        IReadOnlyList<string>? redirectUris)
    {
        if ((redirectUris ?? []).All(string.IsNullOrWhiteSpace))
            return;

        var attributes = clientRepresentation["attributes"] as JsonObject;
        if (attributes is null)
        {
            attributes = [];
            clientRepresentation["attributes"] = attributes;
        }

        if (string.IsNullOrWhiteSpace(attributes["post.logout.redirect.uris"]?.GetValue<string>()))
            attributes["post.logout.redirect.uris"] = "+";
    }

    private static void EnsureAudienceMapper(JsonObject clientRepresentation, string? apiClientId)
    {
        if (string.IsNullOrWhiteSpace(apiClientId))
            return;

        var protocolMappers = clientRepresentation["protocolMappers"] as JsonArray;
        if (protocolMappers is null)
        {
            protocolMappers = [];
            clientRepresentation["protocolMappers"] = protocolMappers;
        }

        var mapperName = $"{apiClientId}-audience";
        var mapper = protocolMappers
            .OfType<JsonObject>()
            .FirstOrDefault(candidate =>
            {
                var config = candidate["config"] as JsonObject;
                return string.Equals(candidate["name"]?.GetValue<string>(), mapperName, StringComparison.Ordinal)
                       || (string.Equals(candidate["protocolMapper"]?.GetValue<string>(), "oidc-audience-mapper", StringComparison.Ordinal)
                           && string.Equals(config?["included.client.audience"]?.GetValue<string>(), apiClientId, StringComparison.Ordinal));
            });

        if (mapper is null)
        {
            protocolMappers.Add(new JsonObject
            {
                ["name"] = mapperName,
                ["protocol"] = "openid-connect",
                ["protocolMapper"] = "oidc-audience-mapper",
                ["consentRequired"] = false,
                ["config"] = CreateAudienceMapperConfig(apiClientId)
            });
            return;
        }

        mapper["name"] = mapperName;
        mapper["protocol"] = "openid-connect";
        mapper["protocolMapper"] = "oidc-audience-mapper";
        mapper["consentRequired"] = false;
        mapper["config"] = CreateAudienceMapperConfig(apiClientId);
    }

    private static JsonObject CreateAudienceMapperConfig(string apiClientId) => new()
    {
        ["included.client.audience"] = apiClientId,
        ["id.token.claim"] = "false",
        ["access.token.claim"] = "true"
    };

    private async Task<ClientSecretUpdateResult> CreateClientAsync(
        IKeycloakAdminApi api,
        string realm,
        string clientId,
        string accessToken,
        bool bearerOnly,
        CancellationToken cancellationToken)
    {
        var representation = bearerOnly
            ? KeycloakClientRepresentation.CreateBearerOnly(clientId)
            : KeycloakClientRepresentation.CreateConfidentialOidc(clientId);
        var response = await api.CreateClientAsync(
            realm,
            AuthorizationHeader(accessToken),
            representation,
            cancellationToken);
        ThrowIfRequestFailed(response, cancellationToken);
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Conflict)
            return ClientSecretUpdateResult.Succeeded();

        _logger.LogWarning(
            "Keycloak client create failed. Realm: {Realm}, ClientId: {ClientId}, StatusCode: {StatusCode}",
            realm,
            clientId,
            (int?)response.StatusCode);

        return ClientSecretUpdateResult.Failure(KeycloakFailureCodes.ClientCreateFailed, "Keycloak client could not be created.");
    }

    private static async Task AddDiscoveryCheckAsync(
        IKeycloakAdminApi api,
        string realm,
        List<KeycloakRealmDoctorCheckDto> checks,
        CancellationToken cancellationToken)
    {
        var response = await api.GetDiscoveryAsync(realm, cancellationToken);
        ThrowIfRequestFailed(response, cancellationToken);
        checks.Add(response.IsSuccessStatusCode
            ? DoctorCheck(
                "keycloak_discovery_reachable",
                "OIDC discovery",
                "healthy",
                "Keycloak OIDC discovery is reachable for the configured realm.")
            : DoctorCheck(
                "keycloak_discovery_unreachable",
                "OIDC discovery",
                "blocked",
                "Keycloak OIDC discovery is not reachable for the configured realm.",
                "Verify the authority URL, realm name, reverse proxy path, and TLS configuration."));
    }

    private static async Task AddAdminRealmChecksAsync(
        IKeycloakAdminApi api,
        string realm,
        string blazorClientId,
        string? apiClientId,
        string accessToken,
        List<KeycloakRealmDoctorCheckDto> checks,
        CancellationToken cancellationToken)
    {
        var realmResponse = await api.GetRealmAsync(realm, AuthorizationHeader(accessToken), cancellationToken);
        ThrowIfRequestFailed(realmResponse, cancellationToken);
        checks.Add(realmResponse.IsSuccessStatusCode
            ? DoctorCheck("keycloak_realm_exists", "Realm", "healthy", "Keycloak realm exists and is readable.")
            : DoctorCheck(KeycloakFailureCodes.RealmNotFound, "Realm", "blocked", "Keycloak realm could not be read through the Admin API.", "Verify the realm exists and the temporary admin has read access."));

        if (!realmResponse.IsSuccessStatusCode)
            return;

        var blazorLookup = await FindClientAsync(api, realm, blazorClientId, accessToken, cancellationToken);
        if (!blazorLookup.Success || string.IsNullOrWhiteSpace(blazorLookup.ClientUuid))
        {
            checks.Add(DoctorCheck(
                "keycloak_blazor_client_missing",
                "Blazor OIDC client",
                "needs-repair",
                "The configured Blazor Keycloak client was not found.",
                "Run setup bootstrap or create the confidential OIDC client additively in Keycloak."));
            return;
        }

        checks.Add(DoctorCheck("keycloak_blazor_client_exists", "Blazor OIDC client", "healthy", "The configured Blazor Keycloak client exists."));
        var clientRepresentation = await GetClientRepresentationAsync(
            api,
            realm,
            blazorLookup.ClientUuid,
            accessToken,
            cancellationToken);

        if (clientRepresentation is null)
        {
            checks.Add(DoctorCheck(
                "keycloak_blazor_client_unreadable",
                "Blazor client representation",
                "blocked",
                "The Blazor client exists but its representation could not be read.",
                "Verify the temporary admin has permission to view clients."));
            return;
        }

        AddClientRepresentationChecks(clientRepresentation, checks);
        await AddOfflineAccessChecksAsync(api, realm, blazorLookup.ClientUuid, accessToken, checks, cancellationToken);
        await AddApiClientCheckAsync(api, realm, apiClientId, accessToken, checks, cancellationToken);
    }

    private static void AddClientRepresentationChecks(JsonObject clientRepresentation, List<KeycloakRealmDoctorCheckDto> checks)
    {
        var standardFlowEnabled = clientRepresentation["standardFlowEnabled"]?.GetValue<bool>() == true;
        checks.Add(standardFlowEnabled
            ? DoctorCheck("keycloak_standard_flow_enabled", "Authorization code flow", "healthy", "The Blazor client has standard authorization code flow enabled.")
            : DoctorCheck("keycloak_standard_flow_disabled", "Authorization code flow", "needs-repair", "The Blazor client does not have standard authorization code flow enabled.", "Enable Standard flow on the Blazor OIDC client."));

        var refreshTokensEnabled = HasRefreshTokenSettings(clientRepresentation);
        checks.Add(refreshTokensEnabled
            ? DoctorCheck("keycloak_refresh_tokens_enabled", "Refresh token settings", "healthy", "The Blazor client includes offline_access scope and refresh-token settings.")
            : DoctorCheck("keycloak_refresh_tokens_missing", "Refresh token settings", "needs-repair", "The Blazor client is missing offline_access scope or refresh-token settings.", "Assign offline_access as a client scope and set use.refresh.tokens=true."));
    }

    private static async Task AddOfflineAccessChecksAsync(
        IKeycloakAdminApi api,
        string realm,
        string clientUuid,
        string accessToken,
        List<KeycloakRealmDoctorCheckDto> checks,
        CancellationToken cancellationToken)
    {
        var offlineRole = await GetRealmRoleAsync(api, realm, "offline_access", accessToken, cancellationToken);
        checks.Add(offlineRole is not null
            ? DoctorCheck("keycloak_offline_access_role_exists", "offline_access realm role", "healthy", "The offline_access realm role exists.")
            : DoctorCheck("keycloak_offline_access_role_missing", "offline_access realm role", "needs-repair", "The offline_access realm role was not found.", "Restore or create the offline_access realm role before enabling offline sessions."));

        var defaultRoleName = $"default-roles-{realm.ToLowerInvariant()}";
        var defaultRole = await GetRealmRoleAsync(api, realm, defaultRoleName, accessToken, cancellationToken);
        checks.Add(defaultRole is not null
            ? DoctorCheck("keycloak_default_role_exists", "Default realm role", "healthy", "The default realm role exists.")
            : DoctorCheck("keycloak_default_role_missing", "Default realm role", "needs-repair", "The default realm role was not found.", "Recreate the default realm role or repair realm defaults."));

        if (offlineRole is not null && defaultRole?["id"]?.GetValue<string>() is { Length: > 0 } defaultRoleId)
        {
            var defaultComposites = await GetRealmRoleCompositesAsync(api, realm, defaultRoleId, accessToken, cancellationToken);
            var hasOfflineComposite = defaultComposites.Any(role => string.Equals(role.Name, "offline_access", StringComparison.Ordinal));
            checks.Add(hasOfflineComposite
                ? DoctorCheck("keycloak_default_role_offline_access", "Default role offline_access composite", "healthy", "The default realm role includes offline_access as a composite.")
                : DoctorCheck("keycloak_default_role_offline_access_missing", "Default role offline_access composite", "needs-repair", "The default realm role does not include offline_access as a composite.", "Add offline_access to the default realm role composites."));
        }

        var scopeId = await FindClientScopeIdAsync(api, realm, "offline_access", accessToken, cancellationToken);
        checks.Add(!string.IsNullOrWhiteSpace(scopeId)
            ? DoctorCheck("keycloak_offline_access_scope_exists", "offline_access client scope", "healthy", "The offline_access client scope exists.")
            : DoctorCheck("keycloak_offline_access_scope_missing", "offline_access client scope", "needs-repair", "The offline_access client scope was not found.", "Restore or create the offline_access client scope."));

        if (!string.IsNullOrWhiteSpace(scopeId))
        {
            var optionalScopes = await GetClientScopesAsync(api, realm, clientUuid, "optional-client-scopes", accessToken, cancellationToken);
            var defaultScopes = await GetClientScopesAsync(api, realm, clientUuid, "default-client-scopes", accessToken, cancellationToken);
            var clientHasOfflineScope = optionalScopes.Concat(defaultScopes).Any(scope => string.Equals(scope.Name, "offline_access", StringComparison.Ordinal));
            checks.Add(clientHasOfflineScope
                ? DoctorCheck("keycloak_client_offline_access_scope", "Blazor client offline_access scope", "healthy", "The Blazor client has offline_access assigned as a client scope.")
                : DoctorCheck("keycloak_client_offline_access_scope_missing", "Blazor client offline_access scope", "needs-repair", "The Blazor client does not have offline_access assigned as a client scope.", "Assign offline_access to the Blazor client as an optional or default client scope."));

            var scopeMappings = await GetClientScopeRealmMappingsAsync(api, realm, scopeId, accessToken, cancellationToken);
            var scopeMapsOfflineRole = scopeMappings.Any(role => string.Equals(role.Name, "offline_access", StringComparison.Ordinal));
            checks.Add(scopeMapsOfflineRole
                ? DoctorCheck("keycloak_scope_maps_offline_access", "offline_access scope mapping", "healthy", "The offline_access client scope maps the offline_access realm role.")
                : DoctorCheck("keycloak_scope_maps_offline_access_missing", "offline_access scope mapping", "needs-repair", "The offline_access client scope does not map the offline_access realm role.", "Add offline_access to the realm role mappings for the offline_access client scope."));
        }
    }

    private static async Task AddApiClientCheckAsync(
        IKeycloakAdminApi api,
        string realm,
        string? apiClientId,
        string accessToken,
        List<KeycloakRealmDoctorCheckDto> checks,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiClientId))
            return;

        var lookup = await FindClientAsync(api, realm, apiClientId, accessToken, cancellationToken);
        checks.Add(lookup.Success && !string.IsNullOrWhiteSpace(lookup.ClientUuid)
            ? DoctorCheck("keycloak_api_client_exists", "API audience client", "healthy", "The configured API client exists.")
            : DoctorCheck("keycloak_api_client_missing", "API audience client", "warning", "The configured API client was not found.", "Create the API audience client if this instance validates API audience through Keycloak."));
    }

    private static async Task<IReadOnlyList<KeycloakRoleLookupResult>> GetRealmRoleCompositesAsync(
        IKeycloakAdminApi api,
        string realm,
        string roleId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var response = await api.GetRealmRoleCompositesAsync(realm, roleId, AuthorizationHeader(accessToken), cancellationToken);
        return await ReadJsonResponseAsync<List<KeycloakRoleLookupResult>>(response, cancellationToken) ?? [];
    }

    private static async Task<IReadOnlyList<KeycloakClientScopeLookupResult>> GetClientScopesAsync(
        IKeycloakAdminApi api,
        string realm,
        string clientUuid,
        string scopeEndpoint,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var response = await api.GetClientScopesAsync(realm, clientUuid, scopeEndpoint, AuthorizationHeader(accessToken), cancellationToken);
        return await ReadJsonResponseAsync<List<KeycloakClientScopeLookupResult>>(response, cancellationToken) ?? [];
    }

    private static async Task<IReadOnlyList<KeycloakRoleLookupResult>> GetClientScopeRealmMappingsAsync(
        IKeycloakAdminApi api,
        string realm,
        string scopeId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var response = await api.GetClientScopeRealmMappingsAsync(realm, scopeId, AuthorizationHeader(accessToken), cancellationToken);
        return await ReadJsonResponseAsync<List<KeycloakRoleLookupResult>>(response, cancellationToken) ?? [];
    }

    private static KeycloakRealmDoctorCheckDto DoctorCheck(
        string code,
        string name,
        string status,
        string message,
        string? remediation = null) => new()
        {
            Code = code,
            Name = name,
            Status = status,
            Message = message,
            Remediation = remediation
        };

    private static KeycloakRealmDoctorResultDto CompleteDoctorResult(
        KeycloakRealmDoctorResultDto result,
        IReadOnlyList<KeycloakRealmDoctorCheckDto> checks)
    {
        result.Checks = checks;
        result.OverallStatus = HasBlockingCheck(checks)
            ? "blocked"
            : checks.Any(check => string.Equals(check.Status, "needs-repair", StringComparison.OrdinalIgnoreCase))
                ? "needs-repair"
                : checks.Any(check => string.Equals(check.Status, "warning", StringComparison.OrdinalIgnoreCase))
                    ? "needs-repair"
                    : "healthy";
        result.Message = result.OverallStatus switch
        {
            "healthy" => "Keycloak realm diagnostics did not find drift.",
            "needs-repair" => "Keycloak realm diagnostics found repairable drift or incomplete inspection.",
            _ => "Keycloak realm diagnostics are blocked. Resolve the blocking checks and retry."
        };

        return result;
    }

    private static bool HasBlockingCheck(IEnumerable<KeycloakRealmDoctorCheckDto> checks) =>
        checks.Any(check => string.Equals(check.Status, "blocked", StringComparison.OrdinalIgnoreCase));

    private KeycloakRealmDesiredStateDto BuildDesiredState(
        string realm,
        string blazorClientId,
        KeycloakRealmSyncPreviewRequestDto request) =>
        _desiredStateBuilder.Build(new KeycloakRealmDesiredStateBuildRequestDto
        {
            Realm = realm,
            BlazorClientId = blazorClientId,
            ApiClientId = request.ApiClientId,
            BlazorRedirectUris = request.BlazorRedirectUris,
            BlazorWebOrigins = request.BlazorWebOrigins
        });

    private static async Task AddSyncPreviewOperationsAsync(
        IKeycloakAdminApi api,
        string realm,
        string blazorClientId,
        KeycloakRealmSyncPreviewRequestDto request,
        string accessToken,
        List<KeycloakRealmSyncOperationDto> operations,
        List<KeycloakRealmDoctorCheckDto> diagnostics,
        CancellationToken cancellationToken)
    {
        var realmResponse = await api.GetRealmAsync(realm, AuthorizationHeader(accessToken), cancellationToken);
        ThrowIfRequestFailed(realmResponse, cancellationToken);
        diagnostics.Add(realmResponse.IsSuccessStatusCode
            ? DoctorCheck("keycloak_realm_exists", "Realm", "healthy", "Keycloak realm exists and is readable.")
            : DoctorCheck(KeycloakFailureCodes.RealmNotFound, "Realm", "blocked", "Keycloak realm could not be read through the Admin API.", "Verify the realm exists and the temporary admin has read access."));

        if (!realmResponse.IsSuccessStatusCode)
        {
            operations.Add(SyncOperation(
                "keycloak-realm-unreadable",
                "realm",
                "realm",
                realm,
                "none",
                "blocked",
                "Realm cannot be inspected.",
                "The preview does not create or reimport realms in post-onboarding repair mode."));
            return;
        }

        await AddBlazorClientSyncOperationsAsync(api, realm, blazorClientId, request, accessToken, operations, cancellationToken);
        await AddOfflineAccessSyncOperationsAsync(api, realm, blazorClientId, accessToken, operations, cancellationToken);
        await AddApiClientSyncOperationsAsync(api, realm, request.ApiClientId, accessToken, operations, cancellationToken);
    }

    private static async Task AddBlazorClientSyncOperationsAsync(
        IKeycloakAdminApi api,
        string realm,
        string blazorClientId,
        KeycloakRealmSyncPreviewRequestDto request,
        string accessToken,
        List<KeycloakRealmSyncOperationDto> operations,
        CancellationToken cancellationToken)
    {
        var blazorLookup = await FindClientAsync(api, realm, blazorClientId, accessToken, cancellationToken);
        if (!blazorLookup.Success)
        {
            operations.Add(SyncOperation(
                "keycloak-blazor-client-lookup-failed",
                "client",
                "client",
                blazorClientId,
                "none",
                "blocked",
                "Blazor client lookup failed.",
                "Keycloak did not allow the temporary admin to list clients."));
            return;
        }

        if (string.IsNullOrWhiteSpace(blazorLookup.ClientUuid))
        {
            operations.Add(SyncOperation(
                "keycloak-blazor-client-add",
                "client",
                "client",
                blazorClientId,
                "add",
                "planned",
                "Create the confidential Blazor OIDC client.",
                "The configured Blazor client is missing from the realm.",
                ["Create confidential OIDC client", "Enable standard authorization-code flow", "Assign offline_access as an optional scope"],
                requiresBackupBeforeApply: true));
            return;
        }

        var representation = await GetClientRepresentationAsync(
            api,
            realm,
            blazorLookup.ClientUuid,
            accessToken,
            cancellationToken);
        if (representation is null)
        {
            operations.Add(SyncOperation(
                "keycloak-blazor-client-unreadable",
                "client",
                "client",
                blazorClientId,
                "none",
                "blocked",
                "Blazor client representation cannot be read.",
                "The temporary admin needs permission to view client details."));
            return;
        }

        if (representation["standardFlowEnabled"]?.GetValue<bool>() != true)
        {
            operations.Add(SyncOperation(
                "keycloak-blazor-standard-flow-enable",
                "client",
                "client",
                blazorClientId,
                "update",
                "planned",
                "Enable standard authorization-code flow on the Blazor client.",
                "The Blazor client must support browser OIDC sign-in.",
                ["Set standardFlowEnabled=true"],
                requiresBackupBeforeApply: true));
        }

        if (!HasRefreshTokenSettings(representation))
        {
            operations.Add(SyncOperation(
                "keycloak-blazor-refresh-token-settings",
                "client",
                "client",
                blazorClientId,
                "update",
                "planned",
                "Add refresh-token/offline_access settings to the Blazor client.",
                "Offline sessions require offline_access and refresh token settings.",
                ["Assign offline_access client scope", "Set use.refresh.tokens=true"],
                requiresBackupBeforeApply: true));
        }

        AddMissingStringSetOperation(
            operations,
            "keycloak-blazor-redirect-uris",
            blazorClientId,
            "redirectUris",
            GetStringArray(representation["redirectUris"]),
            request.BlazorRedirectUris);
        AddMissingStringSetOperation(
            operations,
            "keycloak-blazor-web-origins",
            blazorClientId,
            "webOrigins",
            GetStringArray(representation["webOrigins"]),
            request.BlazorWebOrigins);
    }

    private static async Task AddOfflineAccessSyncOperationsAsync(
        IKeycloakAdminApi api,
        string realm,
        string blazorClientId,
        string accessToken,
        List<KeycloakRealmSyncOperationDto> operations,
        CancellationToken cancellationToken)
    {
        var offlineRole = await GetRealmRoleAsync(api, realm, "offline_access", accessToken, cancellationToken);
        if (offlineRole is null)
        {
            operations.Add(SyncOperation(
                "keycloak-offline-access-role-add",
                "role",
                "realm-role",
                "offline_access",
                "add",
                "planned",
                "Create the offline_access realm role.",
                "The realm is missing the offline_access role required for offline sessions.",
                ["Add realm role offline_access"],
                requiresBackupBeforeApply: true));
        }

        var defaultRoleName = $"default-roles-{realm.ToLowerInvariant()}";
        var defaultRole = await GetRealmRoleAsync(api, realm, defaultRoleName, accessToken, cancellationToken);
        if (defaultRole is null)
        {
            operations.Add(SyncOperation(
                "keycloak-default-role-missing",
                "role",
                "realm-role",
                defaultRoleName,
                "none",
                "blocked",
                "Default realm role is missing.",
                "The preview does not recreate Keycloak-managed default roles."));
        }
        else if (offlineRole is not null && defaultRole["id"]?.GetValue<string>() is { Length: > 0 } defaultRoleId)
        {
            var composites = await GetRealmRoleCompositesAsync(api, realm, defaultRoleId, accessToken, cancellationToken);
            if (!composites.Any(role => string.Equals(role.Name, "offline_access", StringComparison.Ordinal)))
            {
                operations.Add(SyncOperation(
                    "keycloak-default-role-offline-access-add",
                    "role-composite",
                    "realm-role",
                    defaultRoleName,
                    "update",
                    "planned",
                    "Add offline_access to the default realm role composites.",
                    "New users need the offline_access composite for offline sessions.",
                    ["Add offline_access composite"],
                    requiresBackupBeforeApply: true));
            }
        }

        var scopeId = await FindClientScopeIdAsync(api, realm, "offline_access", accessToken, cancellationToken);
        if (string.IsNullOrWhiteSpace(scopeId))
        {
            operations.Add(SyncOperation(
                "keycloak-offline-access-scope-add",
                "client-scope",
                "client-scope",
                "offline_access",
                "add",
                "planned",
                "Create the offline_access client scope.",
                "The realm is missing the offline_access client scope required by the Blazor client.",
                ["Add client scope offline_access", "Map offline_access realm role"],
                requiresBackupBeforeApply: true));
            return;
        }

        var blazorLookup = await FindClientAsync(api, realm, blazorClientId, accessToken, cancellationToken);
        if (blazorLookup.Success && !string.IsNullOrWhiteSpace(blazorLookup.ClientUuid))
        {
            var optionalScopes = await GetClientScopesAsync(api, realm, blazorLookup.ClientUuid, "optional-client-scopes", accessToken, cancellationToken);
            var defaultScopes = await GetClientScopesAsync(api, realm, blazorLookup.ClientUuid, "default-client-scopes", accessToken, cancellationToken);
            var hasOfflineScope = optionalScopes.Concat(defaultScopes).Any(scope => string.Equals(scope.Name, "offline_access", StringComparison.Ordinal));
            if (!hasOfflineScope)
            {
                operations.Add(SyncOperation(
                    "keycloak-blazor-offline-access-scope-add",
                    "client-scope",
                    "client",
                    blazorClientId,
                    "update",
                    "planned",
                    "Assign offline_access to the Blazor client.",
                    "The Blazor client is missing the offline_access client scope.",
                    ["Add offline_access as an optional client scope"],
                    requiresBackupBeforeApply: true));
            }
        }

        var mappings = await GetClientScopeRealmMappingsAsync(api, realm, scopeId, accessToken, cancellationToken);
        if (!mappings.Any(role => string.Equals(role.Name, "offline_access", StringComparison.Ordinal)))
        {
            operations.Add(SyncOperation(
                "keycloak-offline-access-scope-mapping-add",
                "scope-mapping",
                "client-scope",
                "offline_access",
                "update",
                "planned",
                "Map the offline_access realm role to the offline_access client scope.",
                "The client scope does not currently issue the offline_access role.",
                ["Add offline_access realm-role mapping"],
                requiresBackupBeforeApply: true));
        }
    }

    private static async Task AddApiClientSyncOperationsAsync(
        IKeycloakAdminApi api,
        string realm,
        string? apiClientId,
        string accessToken,
        List<KeycloakRealmSyncOperationDto> operations,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiClientId))
            return;

        var lookup = await FindClientAsync(api, realm, apiClientId, accessToken, cancellationToken);
        if (!lookup.Success)
        {
            operations.Add(SyncOperation(
                "keycloak-api-client-lookup-failed",
                "client",
                "client",
                apiClientId,
                "none",
                "blocked",
                "API client lookup failed.",
                "Keycloak did not allow the temporary admin to list clients."));
            return;
        }

        if (string.IsNullOrWhiteSpace(lookup.ClientUuid))
        {
            operations.Add(SyncOperation(
                "keycloak-api-client-add",
                "client",
                "client",
                apiClientId,
                "add",
                "planned",
                "Create the API audience client.",
                "The configured API client is missing from the realm.",
                ["Create bearer-only API client", "Reserve audience mapper contract for access tokens"],
                requiresBackupBeforeApply: true));
        }
    }

    private async Task AddSyncApplyOperationsAsync(
        IKeycloakAdminApi api,
        string realm,
        AuthProviderConfigurationDto configuration,
        KeycloakRealmSyncPreviewRequestDto request,
        string accessToken,
        List<KeycloakRealmSyncOperationDto> operations,
        List<KeycloakRealmDoctorCheckDto> diagnostics,
        CancellationToken cancellationToken)
    {
        var realmResponse = await api.GetRealmAsync(realm, AuthorizationHeader(accessToken), cancellationToken);
        ThrowIfRequestFailed(realmResponse, cancellationToken);
        diagnostics.Add(realmResponse.IsSuccessStatusCode
            ? DoctorCheck("keycloak_realm_exists", "Realm", "healthy", "Keycloak realm exists and is readable.")
            : DoctorCheck(KeycloakFailureCodes.RealmNotFound, "Realm", "blocked", "Keycloak realm could not be read through the Admin API.", "Verify the realm exists and the temporary admin has read access."));

        if (!realmResponse.IsSuccessStatusCode)
        {
            operations.Add(SyncOperation(
                "keycloak-realm-unreadable",
                "realm",
                "realm",
                realm,
                "none",
                "blocked",
                "Realm cannot be repaired.",
                "The apply flow does not create, delete, or reimport realms in post-onboarding repair mode."));
            return;
        }

        await ApplyBlazorClientAsync(api, realm, configuration, request, accessToken, operations, cancellationToken);
        await ApplyOfflineAccessAsync(api, realm, configuration.KeycloakClientId, accessToken, operations, cancellationToken);
        await ApplyApiClientAsync(api, realm, request.ApiClientId, accessToken, operations, cancellationToken);
    }

    private async Task ApplyBlazorClientAsync(
        IKeycloakAdminApi api,
        string realm,
        AuthProviderConfigurationDto configuration,
        KeycloakRealmSyncPreviewRequestDto request,
        string accessToken,
        List<KeycloakRealmSyncOperationDto> operations,
        CancellationToken cancellationToken)
    {
        var blazorClientId = configuration.KeycloakClientId;
        var lookup = await FindClientAsync(api, realm, blazorClientId, accessToken, cancellationToken);
        if (!lookup.Success)
        {
            operations.Add(SyncOperation(
                "keycloak-blazor-client-lookup-failed",
                "client",
                "client",
                blazorClientId,
                "none",
                "blocked",
                "Blazor client lookup failed.",
                "Keycloak did not allow the temporary admin to list clients."));
            return;
        }

        if (string.IsNullOrWhiteSpace(lookup.ClientUuid))
        {
            if (string.IsNullOrWhiteSpace(configuration.KeycloakClientSecret))
            {
                operations.Add(SyncOperation(
                    "keycloak-blazor-client-secret-missing",
                    "client",
                    "client",
                    blazorClientId,
                    "none",
                    "blocked",
                    "Blazor client cannot be created without an application-managed secret.",
                    "Save a Keycloak client secret in instance settings before creating a missing confidential client."));
                return;
            }

            var createResult = await CreateClientAsync(api, realm, blazorClientId, accessToken, bearerOnly: false, cancellationToken);
            if (!createResult.Success)
            {
                operations.Add(SyncOperation(
                    "keycloak-blazor-client-add",
                    "client",
                    "client",
                    blazorClientId,
                    "add",
                    "blocked",
                    "Blazor client could not be created.",
                    createResult.Message));
                return;
            }

            operations.Add(SyncOperation(
                "keycloak-blazor-client-add",
                "client",
                "client",
                blazorClientId,
                "add",
                "applied",
                "Created the confidential Blazor OIDC client.",
                "The configured Blazor client was missing from the realm.",
                ["Created confidential OIDC client"]));

            lookup = await FindClientAsync(api, realm, blazorClientId, accessToken, cancellationToken);
            if (!lookup.Success || string.IsNullOrWhiteSpace(lookup.ClientUuid))
            {
                operations.Add(SyncOperation(
                    "keycloak-blazor-client-created-unreadable",
                    "client",
                    "client",
                    blazorClientId,
                    "none",
                    "blocked",
                    "Created Blazor client could not be reloaded.",
                    "Retry the sync apply after Keycloak indexes the created client."));
                return;
            }
        }

        var representation = await GetClientRepresentationAsync(api, realm, lookup.ClientUuid, accessToken, cancellationToken);
        if (representation is null)
        {
            operations.Add(SyncOperation(
                "keycloak-blazor-client-unreadable",
                "client",
                "client",
                blazorClientId,
                "none",
                "blocked",
                "Blazor client representation cannot be read.",
                "The temporary admin needs permission to view client details."));
            return;
        }

        var changes = new List<string>();
        if (representation["standardFlowEnabled"]?.GetValue<bool>() != true)
        {
            representation["standardFlowEnabled"] = true;
            changes.Add("Set standardFlowEnabled=true");
        }

        if (!HasRefreshTokenSettings(representation))
        {
            PreserveRefreshTokenSettings(representation);
            changes.Add("Preserved refresh-token/offline_access settings");
        }

        changes.AddRange(AddMissingStringValues(representation, "redirectUris", request.BlazorRedirectUris));
        changes.AddRange(AddMissingStringValues(representation, "webOrigins", request.BlazorWebOrigins));

        if (operations.Any(operation => string.Equals(operation.OperationId, "keycloak-blazor-client-add", StringComparison.Ordinal)))
        {
            representation["secret"] = configuration.KeycloakClientSecret;
            changes.Add("Seeded configured confidential client secret");
        }

        if (changes.Count == 0)
        {
            operations.Add(SyncOperation(
                "keycloak-blazor-client-up-to-date",
                "client",
                "client",
                blazorClientId,
                "none",
                "up-to-date",
                "Blazor client settings are already additive-sync compatible.",
                "No Keycloak mutation was required."));
            return;
        }

        var updated = await UpdateClientRepresentationAsync(api, realm, lookup.ClientUuid, accessToken, representation, cancellationToken);
        operations.Add(SyncOperation(
            "keycloak-blazor-client-update",
            "client",
            "client",
            blazorClientId,
            "update",
            updated ? "applied" : "blocked",
            updated ? "Updated additive Blazor client settings." : "Blazor client settings could not be updated.",
            updated
                ? "The apply flow only added required values and did not remove operator-managed values."
                : "Keycloak rejected the additive client representation update.",
            changes));
    }

    private static async Task ApplyOfflineAccessAsync(
        IKeycloakAdminApi api,
        string realm,
        string blazorClientId,
        string accessToken,
        List<KeycloakRealmSyncOperationDto> operations,
        CancellationToken cancellationToken)
    {
        var offlineRole = await GetRealmRoleAsync(api, realm, "offline_access", accessToken, cancellationToken);
        if (offlineRole is null)
        {
            var created = await CreateRealmRoleAsync(api, realm, "offline_access", accessToken, cancellationToken);
            operations.Add(SyncOperation(
                "keycloak-offline-access-role-add",
                "role",
                "realm-role",
                "offline_access",
                "add",
                created ? "applied" : "blocked",
                created ? "Created the offline_access realm role." : "offline_access realm role could not be created.",
                created ? "The role was missing and was added without deleting any existing role." : "Keycloak rejected the additive realm-role create."));
            offlineRole = created
                ? await GetRealmRoleAsync(api, realm, "offline_access", accessToken, cancellationToken)
                : null;
        }

        var defaultRoleName = $"default-roles-{realm.ToLowerInvariant()}";
        var defaultRole = await GetRealmRoleAsync(api, realm, defaultRoleName, accessToken, cancellationToken);
        var defaultRoleId = defaultRole?["id"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(defaultRoleId))
        {
            operations.Add(SyncOperation(
                "keycloak-default-role-missing",
                "role",
                "realm-role",
                defaultRoleName,
                "none",
                "blocked",
                "Default realm role is missing.",
                "The apply flow does not recreate Keycloak-managed default roles."));
        }
        else if (offlineRole is not null)
        {
            var composites = await GetRealmRoleCompositesAsync(api, realm, defaultRoleId, accessToken, cancellationToken);
            if (!composites.Any(role => string.Equals(role.Name, "offline_access", StringComparison.Ordinal)))
            {
                var response = await api.AddRealmRoleCompositesAsync(
                    realm,
                    defaultRoleId,
                    AuthorizationHeader(accessToken),
                    new JsonArray(offlineRole.DeepClone()),
                    cancellationToken);
                ThrowIfRequestFailed(response, cancellationToken);
                operations.Add(SyncOperation(
                    "keycloak-default-role-offline-access-add",
                    "role-composite",
                    "realm-role",
                    defaultRoleName,
                    "update",
                    response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Conflict ? "applied" : "blocked",
                    response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Conflict
                        ? "Added offline_access to the default realm role composites."
                        : "offline_access could not be added to default realm role composites.",
                    "Only the missing composite was added."));
            }
        }

        var scopeId = await FindClientScopeIdAsync(api, realm, "offline_access", accessToken, cancellationToken);
        if (string.IsNullOrWhiteSpace(scopeId))
        {
            var created = await CreateClientScopeAsync(api, realm, "offline_access", accessToken, cancellationToken);
            operations.Add(SyncOperation(
                "keycloak-offline-access-scope-add",
                "client-scope",
                "client-scope",
                "offline_access",
                "add",
                created ? "applied" : "blocked",
                created ? "Created the offline_access client scope." : "offline_access client scope could not be created.",
                created ? "The client scope was missing and was added." : "Keycloak rejected the additive client-scope create."));
            scopeId = created
                ? await FindClientScopeIdAsync(api, realm, "offline_access", accessToken, cancellationToken)
                : null;
        }

        var blazorLookup = await FindClientAsync(api, realm, blazorClientId, accessToken, cancellationToken);
        if (blazorLookup.Success && !string.IsNullOrWhiteSpace(blazorLookup.ClientUuid) && !string.IsNullOrWhiteSpace(scopeId))
        {
            var optionalScopes = await GetClientScopesAsync(api, realm, blazorLookup.ClientUuid, "optional-client-scopes", accessToken, cancellationToken);
            var defaultScopes = await GetClientScopesAsync(api, realm, blazorLookup.ClientUuid, "default-client-scopes", accessToken, cancellationToken);
            var hasOfflineScope = optionalScopes.Concat(defaultScopes).Any(scope => string.Equals(scope.Name, "offline_access", StringComparison.Ordinal));
            if (!hasOfflineScope)
            {
                var result = await EnsureOfflineAccessScopeAsync(api, realm, blazorLookup.ClientUuid, accessToken, cancellationToken);
                operations.Add(SyncOperation(
                    "keycloak-blazor-offline-access-scope-add",
                    "client-scope",
                    "client",
                    blazorClientId,
                    "update",
                    result.Success ? "applied" : "blocked",
                    result.Success ? "Assigned offline_access to the Blazor client." : "offline_access could not be assigned to the Blazor client.",
                    result.Success ? "Added offline_access as an optional client scope." : result.Message));
            }
        }

        if (!string.IsNullOrWhiteSpace(scopeId) && offlineRole is not null)
        {
            var mappings = await GetClientScopeRealmMappingsAsync(api, realm, scopeId, accessToken, cancellationToken);
            if (!mappings.Any(role => string.Equals(role.Name, "offline_access", StringComparison.Ordinal)))
            {
                var result = await EnsureOfflineAccessScopeMappingAsync(api, realm, scopeId, accessToken, cancellationToken);
                operations.Add(SyncOperation(
                    "keycloak-offline-access-scope-mapping-add",
                    "scope-mapping",
                    "client-scope",
                    "offline_access",
                    "update",
                    result.Success ? "applied" : "blocked",
                    result.Success ? "Mapped the offline_access realm role to the offline_access client scope." : "offline_access scope mapping could not be updated.",
                    result.Success ? "Added offline_access realm-role mapping." : result.Message));
            }
        }
    }

    private async Task ApplyApiClientAsync(
        IKeycloakAdminApi api,
        string realm,
        string? apiClientId,
        string accessToken,
        List<KeycloakRealmSyncOperationDto> operations,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiClientId))
            return;

        var lookup = await FindClientAsync(api, realm, apiClientId, accessToken, cancellationToken);
        if (!lookup.Success)
        {
            operations.Add(SyncOperation(
                "keycloak-api-client-lookup-failed",
                "client",
                "client",
                apiClientId,
                "none",
                "blocked",
                "API client lookup failed.",
                "Keycloak did not allow the temporary admin to list clients."));
            return;
        }

        if (string.IsNullOrWhiteSpace(lookup.ClientUuid))
        {
            var createResult = await CreateClientAsync(api, realm, apiClientId, accessToken, bearerOnly: true, cancellationToken);
            operations.Add(SyncOperation(
                "keycloak-api-client-add",
                "client",
                "client",
                apiClientId,
                "add",
                createResult.Success ? "applied" : "blocked",
                createResult.Success ? "Created the API audience client." : "API audience client could not be created.",
                createResult.Success ? "Created bearer-only API client." : createResult.Message,
                ["Create bearer-only API client"]));
            return;
        }

        operations.Add(SyncOperation(
            "keycloak-api-client-up-to-date",
            "client",
            "client",
            apiClientId,
            "none",
            "up-to-date",
            "API audience client already exists.",
            "No Keycloak mutation was required."));
    }

    private static async Task<bool> UpdateClientRepresentationAsync(
        IKeycloakAdminApi api,
        string realm,
        string clientUuid,
        string accessToken,
        JsonObject representation,
        CancellationToken cancellationToken)
    {
        var response = await api.UpdateClientRepresentationAsync(
            realm,
            clientUuid,
            AuthorizationHeader(accessToken),
            representation,
            cancellationToken);
        ThrowIfRequestFailed(response, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    private static async Task<bool> CreateRealmRoleAsync(
        IKeycloakAdminApi api,
        string realm,
        string roleName,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var response = await api.CreateRealmRoleAsync(
            realm,
            AuthorizationHeader(accessToken),
            new JsonObject { ["name"] = roleName },
            cancellationToken);
        ThrowIfRequestFailed(response, cancellationToken);
        return response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Conflict;
    }

    private static async Task<bool> CreateClientScopeAsync(
        IKeycloakAdminApi api,
        string realm,
        string scopeName,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var response = await api.CreateClientScopeAsync(
            realm,
            AuthorizationHeader(accessToken),
            new JsonObject
            {
                ["name"] = scopeName,
                ["protocol"] = "openid-connect"
            },
            cancellationToken);
        ThrowIfRequestFailed(response, cancellationToken);
        return response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Conflict;
    }

    private static IReadOnlyList<string> AddMissingStringValues(
        JsonObject representation,
        string propertyName,
        IReadOnlyList<string> desiredValues)
    {
        var values = representation[propertyName] as JsonArray;
        if (values is null)
        {
            values = [];
            representation[propertyName] = values;
        }

        var currentValues = GetStringArray(values);
        var added = desiredValues
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Except(currentValues, StringComparer.Ordinal)
            .ToArray();
        foreach (var value in added)
        {
            values.Add(value);
        }

        return added.Select(value => $"Add {propertyName} value {value}").ToArray();
    }

    private static void AddMissingStringSetOperation(
        List<KeycloakRealmSyncOperationDto> operations,
        string operationId,
        string clientId,
        string fieldName,
        IReadOnlyList<string> currentValues,
        IReadOnlyList<string> desiredValues)
    {
        var missingValues = desiredValues
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Except(currentValues, StringComparer.Ordinal)
            .ToArray();
        if (missingValues.Length == 0)
            return;

        operations.Add(SyncOperation(
            operationId,
            "client",
            "client",
            clientId,
            "update",
            "planned",
            $"Add missing {fieldName} values to the Blazor client.",
            "The sync plan is additive and does not remove operator-managed values.",
            missingValues.Select(value => $"Add {fieldName} value {value}").ToArray(),
            requiresBackupBeforeApply: true));
    }

    private static IReadOnlyList<string> GetStringArray(JsonNode? node)
    {
        return node is JsonArray array
            ? array.Select(value => value?.GetValue<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().ToArray()
            : [];
    }

    private static KeycloakRealmSyncOperationDto SyncOperation(
        string operationId,
        string category,
        string targetType,
        string target,
        string action,
        string status,
        string summary,
        string reason,
        IReadOnlyList<string>? changes = null,
        bool requiresBackupBeforeApply = false) => new()
        {
            OperationId = operationId,
            Category = category,
            TargetType = targetType,
            Target = target,
            Action = action,
            Status = status,
            Summary = summary,
            Reason = reason,
            Changes = changes ?? [],
            RequiresBackupBeforeApply = requiresBackupBeforeApply
        };

    private static KeycloakClientSecretRotationResultDto CompleteRotationResult(
        KeycloakClientSecretRotationResultDto result,
        string status,
        string message,
        KeycloakRealmSyncOperationDto operation)
    {
        result.Status = status;
        result.Message = message;
        result.Operations = [operation];
        if (string.IsNullOrWhiteSpace(result.OperatorInstructions) && status == "blocked")
        {
            result.OperatorInstructions = operation.Reason;
        }

        return result;
    }

    private static KeycloakRealmSyncPlanDto CompleteSyncPlan(
        KeycloakRealmSyncPlanDto plan,
        IReadOnlyList<KeycloakRealmSyncOperationDto> operations,
        IReadOnlyList<KeycloakRealmDoctorCheckDto> diagnostics)
    {
        plan.Operations = operations;
        plan.Diagnostics = diagnostics;
        plan.RequiresBackupBeforeApply = operations.Any(operation => operation.RequiresBackupBeforeApply);
        plan.Status = HasBlockingCheck(diagnostics) || operations.Any(operation => string.Equals(operation.Status, "blocked", StringComparison.OrdinalIgnoreCase))
            ? "blocked"
            : operations.Any(operation => string.Equals(operation.Status, "planned", StringComparison.OrdinalIgnoreCase))
                ? "changes-planned"
                : operations.Any(operation => string.Equals(operation.Status, "applied", StringComparison.OrdinalIgnoreCase))
                    ? "applied"
                    : "up-to-date";
        plan.Message = plan.Status switch
        {
            "applied" => "Keycloak additive realm repairs were applied.",
            "up-to-date" => "Keycloak realm is already up to date.",
            "changes-planned" => "Keycloak realm sync preview found additive repair operations. Review and back up Keycloak before applying in a future step.",
            _ => "Keycloak realm sync preview is blocked. Resolve the blocking checks and retry."
        };

        return plan;
    }

    private static KeycloakBootstrapResultDto Success(KeycloakBootstrapRequestDto request, bool realmCreated)
    {
        return new KeycloakBootstrapResultDto
        {
            Success = true,
            Mode = request.Mode,
            Realm = request.Realm,
            BlazorClientId = request.BlazorClientId,
            ApiClientId = request.ApiClientId,
            RealmCreated = realmCreated
        };
    }

    private static KeycloakBootstrapResultDto Failure(
        KeycloakBootstrapRequestDto request,
        string failureCode,
        string message,
        bool realmCreated = false,
        bool blazorClientUpdated = false)
    {
        return new KeycloakBootstrapResultDto
        {
            Success = false,
            Message = message,
            FailureCode = failureCode,
            Mode = request.Mode,
            Realm = request.Realm,
            BlazorClientId = request.BlazorClientId,
            ApiClientId = request.ApiClientId,
            RealmCreated = realmCreated,
            BlazorClientUpdated = blazorClientUpdated
        };
    }

    internal interface IKeycloakAdminApi
    {
        [Post("/realms/master/protocol/openid-connect/token")]
        Task<HttpResponseMessage> RequestAdminTokenAsync(
            [Body(BodySerializationMethod.UrlEncoded)] Dictionary<string, object> request,
            CancellationToken cancellationToken = default);

        [Get("/realms/{realm}/.well-known/openid-configuration")]
        Task<IApiResponse> GetDiscoveryAsync(string realm, CancellationToken cancellationToken = default);

        [Get("/admin/realms/{realm}")]
        Task<IApiResponse> GetRealmAsync(
            string realm,
            [Header("Authorization")] string authorization,
            CancellationToken cancellationToken = default);

        [Post("/admin/realms")]
        Task<IApiResponse> CreateRealmAsync(
            [Header("Authorization")] string authorization,
            [Body] KeycloakRealmRepresentation request,
            CancellationToken cancellationToken = default);

        [Get("/admin/realms/{realm}/clients")]
        Task<HttpResponseMessage> FindClientsAsync(
            string realm,
            [Query] string clientId,
            [Header("Authorization")] string authorization,
            CancellationToken cancellationToken = default);

        [Post("/admin/realms/{realm}/clients")]
        Task<IApiResponse> CreateClientAsync(
            string realm,
            [Header("Authorization")] string authorization,
            [Body] KeycloakClientRepresentation request,
            CancellationToken cancellationToken = default);

        [Get("/admin/realms/{realm}/clients/{clientUuid}")]
        Task<HttpResponseMessage> GetClientRepresentationAsync(
            string realm,
            string clientUuid,
            [Header("Authorization")] string authorization,
            CancellationToken cancellationToken = default);

        [Put("/admin/realms/{realm}/clients/{clientUuid}")]
        Task<IApiResponse> UpdateClientRepresentationAsync(
            string realm,
            string clientUuid,
            [Header("Authorization")] string authorization,
            [Body] JsonObject request,
            CancellationToken cancellationToken = default);

        [Get("/admin/realms/{realm}/clients/{clientUuid}/client-secret")]
        Task<HttpResponseMessage> GetCurrentClientSecretAsync(
            string realm,
            string clientUuid,
            [Header("Authorization")] string authorization,
            CancellationToken cancellationToken = default);

        [Put("/admin/realms/{realm}/clients/{clientUuid}/optional-client-scopes/{scopeId}")]
        Task<IApiResponse> AssignOptionalClientScopeAsync(
            string realm,
            string clientUuid,
            string scopeId,
            [Header("Authorization")] string authorization,
            CancellationToken cancellationToken = default);

        [Get("/admin/realms/{realm}/roles/{roleName}")]
        Task<HttpResponseMessage> GetRealmRoleAsync(
            string realm,
            string roleName,
            [Header("Authorization")] string authorization,
            CancellationToken cancellationToken = default);

        [Post("/admin/realms/{realm}/roles")]
        Task<IApiResponse> CreateRealmRoleAsync(
            string realm,
            [Header("Authorization")] string authorization,
            [Body] JsonObject request,
            CancellationToken cancellationToken = default);

        [Get("/admin/realms/{realm}/roles-by-id/{roleId}/composites/realm")]
        Task<HttpResponseMessage> GetRealmRoleCompositesAsync(
            string realm,
            string roleId,
            [Header("Authorization")] string authorization,
            CancellationToken cancellationToken = default);

        [Post("/admin/realms/{realm}/roles-by-id/{roleId}/composites")]
        Task<IApiResponse> AddRealmRoleCompositesAsync(
            string realm,
            string roleId,
            [Header("Authorization")] string authorization,
            [Body] JsonArray request,
            CancellationToken cancellationToken = default);

        [Get("/admin/realms/{realm}/client-scopes")]
        Task<HttpResponseMessage> FindClientScopesAsync(
            string realm,
            [Query] string search,
            [Header("Authorization")] string authorization,
            CancellationToken cancellationToken = default);

        [Post("/admin/realms/{realm}/client-scopes")]
        Task<IApiResponse> CreateClientScopeAsync(
            string realm,
            [Header("Authorization")] string authorization,
            [Body] JsonObject request,
            CancellationToken cancellationToken = default);

        [Get("/admin/realms/{realm}/clients/{clientUuid}/{scopeEndpoint}")]
        Task<HttpResponseMessage> GetClientScopesAsync(
            string realm,
            string clientUuid,
            string scopeEndpoint,
            [Header("Authorization")] string authorization,
            CancellationToken cancellationToken = default);

        [Get("/admin/realms/{realm}/client-scopes/{scopeId}/scope-mappings/realm/composite")]
        Task<HttpResponseMessage> GetClientScopeRealmMappingsAsync(
            string realm,
            string scopeId,
            [Header("Authorization")] string authorization,
            CancellationToken cancellationToken = default);

        [Post("/admin/realms/{realm}/client-scopes/{scopeId}/scope-mappings/realm")]
        Task<IApiResponse> AddClientScopeRealmMappingAsync(
            string realm,
            string scopeId,
            [Header("Authorization")] string authorization,
            [Body] JsonArray request,
            CancellationToken cancellationToken = default);
    }

    internal sealed class KeycloakTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }
    }

    internal sealed record KeycloakRealmRepresentation(string Realm, bool Enabled);

    internal sealed class KeycloakClientLookupResult
    {
        public string Id { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
    }

    internal sealed class KeycloakClientScopeLookupResult
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    internal sealed class KeycloakRoleLookupResult
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    internal sealed class KeycloakClientRepresentation
    {
        public string ClientId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public bool Enabled { get; init; } = true;
        public string Protocol { get; init; } = "openid-connect";
        public bool PublicClient { get; init; }
        public bool StandardFlowEnabled { get; init; }
        public bool DirectAccessGrantsEnabled { get; init; }
        public bool ServiceAccountsEnabled { get; init; }
        public bool BearerOnly { get; init; }

        public static KeycloakClientRepresentation CreateConfidentialOidc(string clientId) => new()
        {
            ClientId = clientId,
            Name = clientId,
            PublicClient = false,
            StandardFlowEnabled = true,
            DirectAccessGrantsEnabled = false,
            ServiceAccountsEnabled = false,
            BearerOnly = false
        };

        public static KeycloakClientRepresentation CreateBearerOnly(string clientId) => new()
        {
            ClientId = clientId,
            Name = clientId,
            PublicClient = false,
            StandardFlowEnabled = false,
            DirectAccessGrantsEnabled = false,
            ServiceAccountsEnabled = false,
            BearerOnly = true
        };
    }

    private sealed record ClientSecretUpdateResult(bool Success, string FailureCode, string Message)
    {
        public static ClientSecretUpdateResult Succeeded() => new(true, string.Empty, string.Empty);

        public static ClientSecretUpdateResult Failure(string failureCode, string message) => new(false, failureCode, message);
    }

    private sealed record ClientLookupResult(bool Success, string? ClientUuid)
    {
        public static ClientLookupResult Succeeded(string? clientUuid) => new(true, clientUuid);

        public static ClientLookupResult Failure() => new(false, null);
    }
}
