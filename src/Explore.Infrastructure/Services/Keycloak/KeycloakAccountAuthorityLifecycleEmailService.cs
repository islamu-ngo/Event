using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Notifications;
using Explore.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Refit;

namespace Explore.Infrastructure.Services.Keycloak;

public sealed class KeycloakAccountAuthorityLifecycleEmailService(
    IHttpClientFactory httpClientFactory,
    INotificationOrchestrator notificationOrchestrator,
    IOptions<AccountAuthorityLifecycleEmailOptions> lifecycleOptions,
    IOptions<KeycloakLifecycleEmailOptions> keycloakOptions,
    ILogger<KeycloakAccountAuthorityLifecycleEmailService> logger) : IAccountAuthorityLifecycleEmailService
{
    public const string HttpClientName = "KeycloakLifecycleEmailClient";

    private const string RecipientKind = "User";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public Task<AccountAuthorityLifecycleEmailResult> RequestEmailVerificationAsync(
        AccountAuthorityLifecycleEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        return RequestAsync(AccountAuthorityLifecycleEmailAction.EmailVerification, request, cancellationToken);
    }

    public Task<AccountAuthorityLifecycleEmailResult> RequestPasswordResetAsync(
        AccountAuthorityLifecycleEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        return RequestAsync(AccountAuthorityLifecycleEmailAction.PasswordReset, request, cancellationToken);
    }

    public Task<AccountAuthorityLifecycleEmailResult> RequestEmailUpdateVerificationAsync(
        AccountAuthorityLifecycleEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        return RequestAsync(AccountAuthorityLifecycleEmailAction.EmailUpdateVerification, request, cancellationToken);
    }

    private async Task<AccountAuthorityLifecycleEmailResult> RequestAsync(
        AccountAuthorityLifecycleEmailAction action,
        AccountAuthorityLifecycleEmailRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var lifecycle = lifecycleOptions.Value;
        if (!lifecycle.Enabled)
        {
            return CreateResult(
                AccountAuthorityLifecycleEmailStatus.Disabled,
                action,
                lifecycle.AccountAuthorityKind,
                reasonCode: "account_authority_lifecycle_email_disabled");
        }

        var keycloak = keycloakOptions.Value;
        if (!IsProviderConfigured(lifecycle, keycloak, out var providerFailureCode)
            || !TryNormalizeBaseUri(keycloak.BaseUrl, keycloak.AllowLocalUrls, out var baseUri, out providerFailureCode))
        {
            return CreateResult(
                AccountAuthorityLifecycleEmailStatus.ProviderNotConfigured,
                action,
                lifecycle.AccountAuthorityKind,
                reasonCode: providerFailureCode);
        }

        var draft = CreateDraft(action, request, lifecycle.AccountAuthorityKind);
        var orchestration = await notificationOrchestrator.EnqueueAsync(draft, cancellationToken);
        if (orchestration.IsFenced)
        {
            return CreateResult(
                AccountAuthorityLifecycleEmailStatus.Disabled,
                action,
                lifecycle.AccountAuthorityKind,
                reasonCode: "account_authority_lifecycle_email_unavailable");
        }

        var api = CreateApi(baseUri!);

        try
        {
            var token = await RequestAdminTokenAsync(api, keycloak, cancellationToken);
            if (string.IsNullOrWhiteSpace(token))
            {
                return CreateProviderFailure(action, orchestration, "keycloak_lifecycle_auth_failed");
            }

            using var response = await api.ExecuteActionsEmailAsync(
                keycloak.Realm.Trim(),
                request.AccountAuthorityUserId.Trim(),
                GetRedirectUri(request),
                GetClientId(request, keycloak),
                GetLifespanSeconds(request, keycloak),
                AuthorizationHeader(token),
                [GetRequiredAction(action)],
                cancellationToken);

            if (response.HasRequestError(out var requestError))
            {
                cancellationToken.ThrowIfCancellationRequested();
                return CreateProviderFailure(
                    action,
                    orchestration,
                    requestError.InnerException is OperationCanceledException
                        ? "keycloak_lifecycle_timeout"
                        : "keycloak_lifecycle_unreachable");
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Keycloak lifecycle email request failed with status {StatusCode} for action {Action}. Tenant: {TenantId}, UserId: {UserId}",
                    (int?)response.StatusCode,
                    action,
                    request.TenantId,
                    request.UserId);

                return CreateProviderFailure(action, orchestration, "keycloak_lifecycle_email_failed");
            }

            return CreateResult(
                AccountAuthorityLifecycleEmailStatus.DelegationRecorded,
                action,
                orchestration.Decision.AccountAuthorityKind,
                orchestration.Intent.Id,
                orchestration.ExternalDelegation?.Id,
                "keycloak_required_action_email_requested");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return CreateProviderFailure(action, orchestration, "keycloak_lifecycle_timeout");
        }
        catch (HttpRequestException)
        {
            return CreateProviderFailure(action, orchestration, "keycloak_lifecycle_unreachable");
        }
        catch (ApiRequestException ex)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CreateProviderFailure(
                action,
                orchestration,
                ex.InnerException is OperationCanceledException
                    ? "keycloak_lifecycle_timeout"
                    : "keycloak_lifecycle_unreachable");
        }
        catch (JsonException)
        {
            return CreateProviderFailure(action, orchestration, "keycloak_lifecycle_invalid_response");
        }
    }

    private IKeycloakLifecycleEmailApi CreateApi(Uri baseUri)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        client.BaseAddress = new Uri(baseUri.ToString().TrimEnd('/'), UriKind.Absolute);
        return RestService.For<IKeycloakLifecycleEmailApi>(
            client,
            new RefitSettings
            {
                ContentSerializer = new SystemTextJsonContentSerializer(JsonOptions),
                ExceptionFactory = _ => ValueTask.FromResult<Exception?>(null)
            });
    }

    private static async Task<string?> RequestAdminTokenAsync(
        IKeycloakLifecycleEmailApi api,
        KeycloakLifecycleEmailOptions options,
        CancellationToken cancellationToken)
    {
        using var tokenResponse = await api.RequestAdminTokenAsync(
            new Dictionary<string, object>
            {
                ["grant_type"] = "password",
                ["client_id"] = string.IsNullOrWhiteSpace(options.AdminClientId) ? "admin-cli" : options.AdminClientId.Trim(),
                ["username"] = options.AdminUsername.Trim(),
                ["password"] = options.AdminPassword
            },
            cancellationToken);

        if (!tokenResponse.IsSuccessStatusCode || tokenResponse.Content is null)
            return null;

        await using var stream = await tokenResponse.Content.ReadAsStreamAsync(cancellationToken);
        var token = await JsonSerializer.DeserializeAsync<KeycloakTokenResponse>(stream, JsonOptions, cancellationToken);
        return token?.AccessToken;
    }

    private static bool IsProviderConfigured(
        AccountAuthorityLifecycleEmailOptions lifecycle,
        KeycloakLifecycleEmailOptions keycloak,
        out string failureCode)
    {
        failureCode = "account_authority_provider_not_configured";
        if (!lifecycle.ProviderConfigured || lifecycle.AccountAuthorityKind != AccountAuthorityKind.Keycloak)
            return false;

        if (!keycloak.Enabled || keycloak.AccountAuthorityKind != AccountAuthorityKind.Keycloak)
            return false;

        if (string.IsNullOrWhiteSpace(keycloak.BaseUrl)
            || string.IsNullOrWhiteSpace(keycloak.Realm)
            || string.IsNullOrWhiteSpace(keycloak.AdminUsername)
            || string.IsNullOrWhiteSpace(keycloak.AdminPassword))
            return false;

        return true;
    }

    private static bool TryNormalizeBaseUri(string keycloakBaseUrl, bool allowLocalUrls, out Uri? baseUri, out string failureCode)
    {
        baseUri = null;
        failureCode = "keycloak_lifecycle_invalid_url";

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
            failureCode = "keycloak_lifecycle_unsafe_host";
            return false;
        }

        baseUri = new Uri(uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/");
        return true;
    }

    private static bool IsBlockedHost(string host)
    {
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || host.Equals("::1", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
    }

    private static NotificationIntentDraft CreateDraft(
        AccountAuthorityLifecycleEmailAction action,
        AccountAuthorityLifecycleEmailRequest request,
        AccountAuthorityKind accountAuthorityKind)
    {
        var externalUserId = RequireNonEmpty(request.AccountAuthorityUserId, "Account-authority user id is required.");
        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? Guid.CreateVersion7().ToString("N")
            : request.CorrelationId.Trim();
        var templateKey = GetTemplateKey(action);
        var actionKey = GetActionKey(action);

        return new NotificationIntentDraft(
            NotificationCategory.IdentityLifecycle,
            TenantId: request.TenantId,
            RecipientKind: RecipientKind,
            TemplateKey: templateKey,
            SafePayloadReference: $"account-authority:{accountAuthorityKind}:user:{externalUserId}",
            IsUserFacing: true,
            IsIslamuInitiated: true,
            DeduplicationKey: $"identity-lifecycle:{actionKey}:{request.UserId}:{correlationId}",
            CorrelationId: correlationId,
            UserId: request.UserId,
            ExternalProviderId: externalUserId,
            ExternalCorrelationId: correlationId);
    }

    private static AccountAuthorityLifecycleEmailResult CreateProviderFailure(
        AccountAuthorityLifecycleEmailAction action,
        NotificationOrchestrationResult orchestration,
        string reasonCode)
    {
        return CreateResult(
            AccountAuthorityLifecycleEmailStatus.ProviderRequestFailed,
            action,
            orchestration.Decision.AccountAuthorityKind,
            orchestration.Intent.Id,
            orchestration.ExternalDelegation?.Id,
            reasonCode);
    }

    private static AccountAuthorityLifecycleEmailResult CreateResult(
        AccountAuthorityLifecycleEmailStatus status,
        AccountAuthorityLifecycleEmailAction action,
        AccountAuthorityKind accountAuthorityKind,
        Guid? notificationIntentId = null,
        Guid? localDelegationId = null,
        string? reasonCode = null)
    {
        return new AccountAuthorityLifecycleEmailResult(
            status,
            action,
            accountAuthorityKind,
            notificationIntentId,
            localDelegationId,
            reasonCode);
    }

    private static string AuthorizationHeader(string accessToken) => $"Bearer {accessToken}";

    private static string? GetRedirectUri(AccountAuthorityLifecycleEmailRequest request)
    {
        return string.IsNullOrWhiteSpace(request.RedirectUri) ? null : request.RedirectUri.Trim();
    }

    private static string? GetClientId(AccountAuthorityLifecycleEmailRequest request, KeycloakLifecycleEmailOptions options)
    {
        if (!string.IsNullOrWhiteSpace(request.ClientId))
            return request.ClientId.Trim();

        return string.IsNullOrWhiteSpace(options.DefaultClientId) ? null : options.DefaultClientId.Trim();
    }

    private static int? GetLifespanSeconds(AccountAuthorityLifecycleEmailRequest request, KeycloakLifecycleEmailOptions options)
    {
        return request.LifespanSeconds ?? options.DefaultLifespanSeconds;
    }

    private static string GetRequiredAction(AccountAuthorityLifecycleEmailAction action) => action switch
    {
        AccountAuthorityLifecycleEmailAction.EmailVerification => "VERIFY_EMAIL",
        AccountAuthorityLifecycleEmailAction.PasswordReset => "UPDATE_PASSWORD",
        AccountAuthorityLifecycleEmailAction.EmailUpdateVerification => "UPDATE_EMAIL",
        _ => throw new InvalidOperationException($"Unsupported account-authority lifecycle email action '{action}'.")
    };

    private static string GetTemplateKey(AccountAuthorityLifecycleEmailAction action) => action switch
    {
        AccountAuthorityLifecycleEmailAction.EmailVerification => "identity.email.verify",
        AccountAuthorityLifecycleEmailAction.PasswordReset => "identity.password.reset",
        AccountAuthorityLifecycleEmailAction.EmailUpdateVerification => "identity.email-update.verify",
        _ => throw new InvalidOperationException($"Unsupported account-authority lifecycle email action '{action}'.")
    };

    private static string GetActionKey(AccountAuthorityLifecycleEmailAction action) => action switch
    {
        AccountAuthorityLifecycleEmailAction.EmailVerification => "email-verification",
        AccountAuthorityLifecycleEmailAction.PasswordReset => "password-reset",
        AccountAuthorityLifecycleEmailAction.EmailUpdateVerification => "email-update-verification",
        _ => throw new InvalidOperationException($"Unsupported account-authority lifecycle email action '{action}'.")
    };

    private static string RequireNonEmpty(string value, string message)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException(message) : value.Trim();
    }

    internal interface IKeycloakLifecycleEmailApi
    {
        [Post("/realms/master/protocol/openid-connect/token")]
        Task<HttpResponseMessage> RequestAdminTokenAsync(
            [Body(BodySerializationMethod.UrlEncoded)] Dictionary<string, object> request,
            CancellationToken cancellationToken = default);

        [Put("/admin/realms/{realm}/users/{userId}/execute-actions-email")]
        Task<IApiResponse> ExecuteActionsEmailAsync(
            string realm,
            string userId,
            [Query] string? redirectUri,
            [Query] string? clientId,
            [Query] int? lifespan,
            [Header("Authorization")] string authorization,
            [Body] IReadOnlyList<string> requiredActions,
            CancellationToken cancellationToken = default);
    }

    private sealed class KeycloakTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }
    }
}
