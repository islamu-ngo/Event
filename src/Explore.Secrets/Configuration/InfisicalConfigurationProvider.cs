namespace Explore.Secrets.Configuration;

using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Configuration provider that loads secrets from Infisical via direct REST API calls.
/// Uses HttpClient instead of Infisical.Sdk (whose Rust FFI hangs against self-hosted instances).
/// Secrets are loaded during startup and optionally reloaded periodically.
/// </summary>
public sealed class InfisicalConfigurationProvider : ConfigurationProvider, IDisposable
{
    private readonly InfisicalConfigurationSource _source;
    private string? _accessToken;
    private Timer? _reloadTimer;
    private bool _disposed;

    public InfisicalConfigurationProvider(InfisicalConfigurationSource source)
    {
        _source = source;
    }

    /// <inheritdoc />
    public override void Load()
    {
        try
        {
            LoadViaRestApi();

            if (_source.ReloadOnChange && _reloadTimer is null)
            {
                _reloadTimer = new Timer(
                    _ => ReloadViaRestApi(),
                    null,
                    _source.ReloadInterval,
                    _source.ReloadInterval);
            }
        }
        catch (InvalidOperationException exception) when (IsBoundedReasonCode(exception.Message))
        {
            throw new InvalidOperationException(
                $"{exception.Message}: Infisical startup loading failed. "
                + "Verify the deployment-owned authority and retry.");
        }
        catch (Exception)
        {
            if (_source.ThrowOnFirstLoadFailure)
            {
                throw new InvalidOperationException(
                    "secret_authority_unavailable: Infisical startup loading failed. "
                    + "Verify the deployment-owned authority and retry.");
            }

            Console.Error.WriteLine(
                "[Infisical] secret_authority_unavailable; verify authority configuration and retry.");
        }
    }

    /// <summary>
    /// Loads secrets using direct REST API calls instead of the Infisical.Sdk package.
    /// The SDK (3.0.4) wraps a native Rust FFI binary whose LoginAsync hangs for 100+ seconds
    /// against self-hosted Infisical instances. The REST endpoints respond in &lt;500ms.
    /// IPv4 is forced because many self-hosted deployments publish AAAA records that are
    /// unreachable, causing .NET's Happy Eyeballs to block until timeout.
    /// </summary>
    private void LoadViaRestApi()
    {
        using var handler = CreateIpv4Handler();
        using var http = new HttpClient(handler, disposeHandler: false)
        {
            Timeout = TimeSpan.FromSeconds(15),
        };

        var effectiveUrl = _source.Url.TrimEnd('/');

        // Authenticate if we don't have a token yet
        if (string.IsNullOrEmpty(_accessToken))
        {
            var loginResp = http.PostAsJsonAsync(
                $"{effectiveUrl}/api/v1/auth/universal-auth/login",
                new { clientId = _source.ClientId, clientSecret = _source.ClientSecret })
                .GetAwaiter().GetResult();

            if (!loginResp.IsSuccessStatusCode)
            {
                throw ProviderFailure(loginResp.StatusCode);
            }

            var loginJson = loginResp.Content
                .ReadFromJsonAsync<InfisicalLoginResponse>()
                .GetAwaiter().GetResult();

            _accessToken = loginJson?.AccessToken;
            if (string.IsNullOrEmpty(_accessToken))
            {
                throw new InvalidOperationException("secret_authority_invalid");
            }
        }

        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);

        var newData = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in _source.Paths)
        {
            var listUrl =
                $"{effectiveUrl}/api/v3/secrets/raw"
                + $"?workspaceId={Uri.EscapeDataString(_source.ProjectId)}"
                + $"&environment={Uri.EscapeDataString(_source.Environment)}"
                + $"&secretPath={Uri.EscapeDataString(path)}"
                + "&expandSecretReferences=true&recursive=true";

            var listResp = http.GetAsync(listUrl).GetAwaiter().GetResult();

            if (!listResp.IsSuccessStatusCode)
            {
                throw ProviderFailure(listResp.StatusCode);
            }

            var listJson = listResp.Content
                .ReadFromJsonAsync<InfisicalListSecretsResponse>()
                .GetAwaiter().GetResult();

            if (listJson?.Secrets is null || listJson.Secrets.Count == 0)
            {
                continue;
            }

            foreach (var secret in listJson.Secrets)
            {
                if (string.IsNullOrEmpty(secret.SecretKey)) continue;

                var secretValue = secret.SecretValue;
                if (string.IsNullOrWhiteSpace(secretValue)) continue;

                var secretPath = ValidateSecretPath(secret.SecretPath, path);
                var configKey = ConvertToConfigurationKey(secret.SecretKey, secretPath);
                if (configKey is null)
                {
                    continue;
                }

                newData[configKey] = secretValue;

                // Flat deployment aliases belong to the requested folder, not recursive children.
                // Explicit identity/erasure reads must also never publish primary database aliases.
                if (secretPath.Equals(path.TrimEnd('/'), StringComparison.Ordinal)
                    && (!secretPath.StartsWith("/database/", StringComparison.Ordinal)
                        || secretPath == "/database/identity" && secret.SecretKey.StartsWith("IDENTITY_DATABASE_", StringComparison.OrdinalIgnoreCase)
                        || secretPath == "/database/erasure" && secret.SecretKey.StartsWith("ERASURE_DATABASE_", StringComparison.OrdinalIgnoreCase)))
                {
                    newData[secret.SecretKey] = secretValue;
                }

                if (configKey.Equals("Database:Name", StringComparison.OrdinalIgnoreCase))
                {
                    newData["Database:Database"] = secretValue;
                }
                else if (configKey.StartsWith("Database:Erasure:", StringComparison.OrdinalIgnoreCase))
                {
                    var suffix = configKey["Database:Erasure:".Length..];
                    newData[$"PrivacyErasureAuthorityDatabase:{suffix}"] = secretValue;
                    newData[$"DatabaseErasure:{suffix}"] = secretValue;
                }
                else if (configKey.StartsWith("Instance:OperatorIdentity:", StringComparison.OrdinalIgnoreCase))
                {
                    var suffix = configKey["Instance:OperatorIdentity:".Length..];
                    newData[$"INSTANCE__OPERATORIDENTITY__{suffix.ToUpperInvariant()}"] = secretValue;
                }
                else if (configKey.StartsWith("Instance:Bootstrap:", StringComparison.OrdinalIgnoreCase))
                {
                    var suffix = configKey["Instance:Bootstrap:".Length..];
                    var screamingSuffix = ToScreamingSnakeCase(suffix);
                    newData[$"INSTANCE_BOOTSTRAP_{screamingSuffix}"] = secretValue;
                }
                else if (configKey.StartsWith("ManagedControlPlane:", StringComparison.OrdinalIgnoreCase))
                {
                    var suffix = configKey["ManagedControlPlane:".Length..];
                    switch (suffix)
                    {
                        case "Enabled":
                            newData["CONTROL_PLANE_MANAGED_MODE"] = secretValue;
                            newData["CONTROL_PLANE_ENABLED"] = secretValue;
                            break;
                        case "ControlPlaneUrl":
                            newData["CONTROL_PLANE_URL"] = secretValue;
                            break;
                        case "ManagedInstanceId":
                            newData["CONTROL_PLANE_INSTANCE_ID"] = secretValue;
                            break;
                        case "RegistrationToken":
                            newData["CONTROL_PLANE_REGISTRATION_TOKEN"] = secretValue;
                            break;
                        case "RegistrationCredentials":
                            newData["CONTROL_PLANE_REGISTRATION_CREDENTIALS"] = secretValue;
                            break;
                        case "MaximumTenantCount":
                            newData["CONTROL_PLANE_MAXIMUM_TENANT_COUNT"] = secretValue;
                            break;
                        case "TenantAdministratorSignInUrl":
                            newData["CONTROL_PLANE_TENANT_ADMINISTRATOR_SIGN_IN_URL"] = secretValue;
                            break;
                        case "CredentialLifetime":
                            newData["CONTROL_PLANE_CREDENTIAL_LIFETIME"] = secretValue;
                            break;
                    }
                }
                else if (configKey.StartsWith("WebPush:", StringComparison.OrdinalIgnoreCase))
                {
                    var suffix = configKey["WebPush:".Length..];
                    switch (suffix)
                    {
                        case "Enabled":
                            newData["WEB_PUSH_ENABLED"] = secretValue;
                            break;
                        case "VapidPublicKey":
                            newData["VAPID_PUBLIC_KEY"] = secretValue;
                            break;
                        case "VapidPrivateKey":
                            newData["VAPID_PRIVATE_KEY"] = secretValue;
                            break;
                        case "VapidSubject":
                            newData["VAPID_SUBJECT"] = secretValue;
                            break;
                    }
                }

            }
        }

        Data = newData;
    }

    private void ReloadViaRestApi()
    {
        try
        {
            // Clear token to force re-authentication on reload
            _accessToken = null;
            LoadViaRestApi();
            OnReload();
        }
        catch (Exception)
        {
            Console.Error.WriteLine(
                "[Infisical] secret_authority_reload_failed; keeping the last-known-good configuration.");
        }
    }

    private static string ValidateSecretPath(string? secretPath, string requestedPath)
    {
        var path = secretPath?.TrimEnd('/');
        var root = requestedPath.TrimEnd('/');
        if (secretPath is null || !secretPath.StartsWith('/')
            || secretPath.Split('/').Any(segment => segment is "." or "..")
            || !(root.Length == 0 || path == root || path!.StartsWith(root + "/", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("secret_authority_invalid");
        }
        return path!;
    }

    private static InvalidOperationException ProviderFailure(HttpStatusCode statusCode) =>
        new(statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            ? "secret_authority_unauthorized"
            : "secret_authority_unavailable");

    private static bool IsBoundedReasonCode(string message) => message is
        "secret_authority_unauthorized" or
        "secret_authority_unavailable" or
        "secret_authority_invalid";

    /// <summary>
    /// Creates an HttpHandler that forces IPv4 connections.
    /// Self-hosted Infisical often publishes AAAA records that are unreachable;
    /// .NET's Happy Eyeballs prefers IPv6 and blocks until timeout.
    /// </summary>
    internal static SocketsHttpHandler CreateIpv4Handler() => new()
    {
        ConnectTimeout = TimeSpan.FromSeconds(5),
        ConnectCallback = static async (context, cancellationToken) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(
                context.DnsEndPoint.Host,
                AddressFamily.InterNetwork,
                cancellationToken).ConfigureAwait(false);

            if (addresses.Length == 0)
            {
                throw new SocketException((int)SocketError.HostNotFound);
            }

            var socket = new Socket(
                AddressFamily.InterNetwork,
                SocketType.Stream,
                ProtocolType.Tcp)
            { NoDelay = true };

            try
            {
                await socket.ConnectAsync(
                    addresses,
                    context.DnsEndPoint.Port,
                    cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };

    internal sealed record InfisicalLoginResponse(
        [property: JsonPropertyName("accessToken")] string? AccessToken);

    internal sealed record InfisicalListSecretsResponse(
        [property: JsonPropertyName("secrets")] List<InfisicalRawSecret>? Secrets);

    internal sealed record InfisicalRawSecret(
        [property: JsonPropertyName("secretKey")] string? SecretKey,
        [property: JsonPropertyName("secretValue")] string? SecretValue,
        [property: JsonPropertyName("secretPath")] string? SecretPath,
        [property: JsonPropertyName("version")] int? Version = null);

    /// <summary>
    /// Converts an Infisical secret key to .NET configuration format.
    /// </summary>
    private static string? ConvertToConfigurationKey(string secretKey, string path)
    {
        var normalizedPath = path.Trim('/');

        // 1. Privacy Erasure Authority Database (/database/erasure) -> Database:Erasure:*
        if (normalizedPath.Equals("database/erasure", StringComparison.OrdinalIgnoreCase))
        {
            // ERASURE_DATABASE_ is the external authority contract. Recursive reads use the
            // returned folder provenance, so child fields cannot become primary database fields.
            return secretKey.ToUpperInvariant() switch
            {
                "ERASURE_DATABASE_HOST" => "Database:Erasure:Host",
                "ERASURE_DATABASE_PORT" => "Database:Erasure:Port",
                "ERASURE_DATABASE_NAME" => "Database:Erasure:Database",
                "ERASURE_DATABASE_PROVIDER" => "Database:Erasure:Provider",
                "ERASURE_DATABASE_RUNTIME_USERNAME" => "Database:Erasure:Runtime:Username",
                "ERASURE_DATABASE_RUNTIME_PASSWORD" => "Database:Erasure:Runtime:Password",
                "ERASURE_DATABASE_MIGRATOR_USERNAME" => "Database:Erasure:Migrator:Username",
                "ERASURE_DATABASE_MIGRATOR_PASSWORD" => "Database:Erasure:Migrator:Password",
                "ERASURE_DATABASE_TLS_MODE" => "Database:Erasure:TlsMode",
                "ERASURE_DATABASE_TRUST_SERVER_CERTIFICATE" => "Database:Erasure:TrustServerCertificate",
                "ERASURE_DATABASE_TOPOLOGY" => "PrivacyErasure:Authority:Topology",
                _ => $"Database:Erasure:{ToPascalCase(secretKey)}"
            };
        }

        // 2. Identity Database (/database/identity) -> IdentityDatabase:*
        if (normalizedPath.Equals("database/identity", StringComparison.OrdinalIgnoreCase))
        {
            return secretKey.ToUpperInvariant() switch
            {
                "IDENTITY_DATABASE_TOPOLOGY" => "IdentityDatabase:Topology",
                "IDENTITY_DATABASE_PROVIDER" or "PROVIDER" => "IdentityDatabase:Provider",
                "IDENTITY_DATABASE_CONNECTION_STRING" or "CONNECTION_STRING" => "IdentityDatabase:ConnectionString",
                "IDENTITY_DATABASE_HOST" or "HOST" => "IdentityDatabase:Host",
                "IDENTITY_DATABASE_PORT" or "PORT" => "IdentityDatabase:Port",
                "IDENTITY_DATABASE_NAME" or "DATABASE" or "NAME" => "IdentityDatabase:Name",
                "IDENTITY_DATABASE_SCHEMA" or "SCHEMA" => "IdentityDatabase:Schema",
                "IDENTITY_DATABASE_RUNTIME_USERNAME" or "RUNTIME_USERNAME" => "IdentityDatabase:Runtime:Username",
                "IDENTITY_DATABASE_RUNTIME_PASSWORD" or "RUNTIME_PASSWORD" => "IdentityDatabase:Runtime:Password",
                "IDENTITY_DATABASE_MIGRATOR_USERNAME" or "MIGRATOR_USERNAME" => "IdentityDatabase:Migrator:Username",
                "IDENTITY_DATABASE_MIGRATOR_PASSWORD" or "MIGRATOR_PASSWORD" => "IdentityDatabase:Migrator:Password",
                "IDENTITY_DATABASE_TLS_MODE" or "TLS_MODE" => "IdentityDatabase:TlsMode",
                "IDENTITY_DATABASE_TRUST_SERVER_CERTIFICATE" or "TRUST_SERVER_CERTIFICATE" => "IdentityDatabase:TrustServerCertificate",
                _ => $"IdentityDatabase:{ToPascalCase(secretKey)}"
            };
        }

        // 3. Primary Database (/database)
        if (normalizedPath.Equals("database", StringComparison.OrdinalIgnoreCase))
        {
            return secretKey.ToUpperInvariant() switch
            {
                "DATABASE_PROVIDER" or "PROVIDER" => "Database:Provider",
                "DATABASE_HOST" or "HOST" => "Database:Host",
                "DATABASE_PORT" or "PORT" => "Database:Port",
                "DATABASE_NAME" or "DATABASE" or "NAME" => "Database:Name",
                "DATABASE_SCHEMA" or "SCHEMA" => "Database:Schema",
                "DATABASE_RUNTIME_USERNAME" or "RUNTIME_USERNAME" => "Database:Runtime:Username",
                "DATABASE_RUNTIME_PASSWORD" or "RUNTIME_PASSWORD" => "Database:Runtime:Password",
                "DATABASE_MIGRATOR_USERNAME" or "MIGRATOR_USERNAME" => "Database:Migrator:Username",
                "DATABASE_MIGRATOR_PASSWORD" or "MIGRATOR_PASSWORD" => "Database:Migrator:Password",
                "DATABASE_TLS_MODE" or "TLS_MODE" => "Database:TlsMode",
                "DATABASE_TRUST_SERVER_CERTIFICATE" or "TRUST_SERVER_CERTIFICATE" => "Database:TrustServerCertificate",
                "DATABASE_SERVER_FLAVOR" or "SERVER_FLAVOR" => "Database:ServerFlavor",
                "DATABASE_SERVER_VERSION" or "SERVER_VERSION" => "Database:ServerVersion",
                "ERASURE_DATABASE_TOPOLOGY" => "PrivacyErasure:Authority:Topology",
                "IDENTITY_DATABASE_TOPOLOGY" => "IdentityDatabase:Topology",
                _ => $"Database:{ToPascalCase(secretKey)}"
            };
        }

        // 4. Instance Operator Identity (/api/operator-identity or /api/operatoridentity)
        if (normalizedPath.Equals("api/operator-identity", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Equals("api/operatoridentity", StringComparison.OrdinalIgnoreCase))
        {
            var key = secretKey.ToUpperInvariant();
            if (key.StartsWith("INSTANCE__OPERATORIDENTITY__", StringComparison.Ordinal))
                key = key["INSTANCE__OPERATORIDENTITY__".Length..];
            else if (key.StartsWith("OPERATOR_IDENTITY_", StringComparison.Ordinal))
                key = key["OPERATOR_IDENTITY_".Length..];
            else if (key.StartsWith("OPERATORIDENTITY_", StringComparison.Ordinal))
                key = key["OPERATORIDENTITY_".Length..];

            return key switch
            {
                "OPERATOR_ID" or "OPERATORID" => "Instance:OperatorIdentity:OperatorId",
                "PUBLIC_NAME" or "PUBLICNAME" => "Instance:OperatorIdentity:PublicName",
                "LEGAL_NAME" or "LEGALNAME" => "Instance:OperatorIdentity:LegalName",
                "IS_OFFICIAL_INSTANCE" or "ISOFFICIALINSTANCE" => "Instance:OperatorIdentity:IsOfficialInstance",
                "OFFICIAL_ORIGIN" or "OFFICIALORIGIN" => "Instance:OperatorIdentity:OfficialOrigin",
                "OPERATOR_KIND_CODE" or "OPERATORKINDCODE" => "Instance:OperatorIdentity:OperatorKindCode",
                "JURISDICTION_COUNTRY_CODE" or "JURISDICTIONCOUNTRYCODE" => "Instance:OperatorIdentity:JurisdictionCountryCode",
                "REGISTRATION_IDENTIFIER" or "REGISTRATIONIDENTIFIER" => "Instance:OperatorIdentity:RegistrationIdentifier",
                "PUBLIC_CONTACT_EMAIL" or "PUBLICCONTACTEMAIL" => "Instance:OperatorIdentity:PublicContactEmail",
                "WEBSITE_URL" or "WEBSITEURL" => "Instance:OperatorIdentity:WebsiteUrl",
                "LEGAL_NOTICE_URL" or "LEGALNOTICEURL" => "Instance:OperatorIdentity:LegalNoticeUrl",
                "TERMS_URL" or "TERMSURL" => "Instance:OperatorIdentity:TermsUrl",
                "PRIVACY_URL" or "PRIVACYURL" => "Instance:OperatorIdentity:PrivacyUrl",
                _ => $"Instance:OperatorIdentity:{ToPascalCase(key)}"
            };
        }

        // 5. Instance Bootstrap (/api/bootstrap, /api/instance-bootstrap, or /api/instancebootstrap)
        if (normalizedPath.Equals("api/bootstrap", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Equals("api/instance-bootstrap", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Equals("api/instancebootstrap", StringComparison.OrdinalIgnoreCase))
        {
            var key = secretKey.ToUpperInvariant();
            if (key.StartsWith("INSTANCE_BOOTSTRAP_", StringComparison.Ordinal))
                key = key["INSTANCE_BOOTSTRAP_".Length..];
            else if (key.StartsWith("BOOTSTRAP_", StringComparison.Ordinal))
                key = key["BOOTSTRAP_".Length..];

            return key switch
            {
                "MODE" => "Instance:Bootstrap:Mode",
                "ADMIN_PROVIDER" or "PROVIDER" => "Instance:Bootstrap:AdminProvider",
                "ADMIN_SUBJECT" or "SUBJECT" => "Instance:Bootstrap:AdminSubject",
                "BINDING_GENERATION" or "GENERATION" => "Instance:Bootstrap:BindingGeneration",
                "ADMIN_EMAIL" or "EMAIL" => "Instance:Bootstrap:AdminEmail",
                "ADMIN_FIRST_NAME" or "FIRST_NAME" => "Instance:Bootstrap:AdminFirstName",
                "ADMIN_LAST_NAME" or "LAST_NAME" => "Instance:Bootstrap:AdminLastName",
                "LOCAL_PASSWORD" or "PASSWORD" => "Instance:Bootstrap:LocalPassword",
                _ => $"Instance:Bootstrap:{ToPascalCase(key)}"
            };
        }

        // 6. Control Plane (/api/controlplane or /api/control-plane) -> ManagedControlPlane:*
        if (normalizedPath.Equals("api/controlplane", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Equals("api/control-plane", StringComparison.OrdinalIgnoreCase))
        {
            var key = secretKey.ToUpperInvariant();
            if (key.StartsWith("CONTROL_PLANE_", StringComparison.Ordinal))
                key = key["CONTROL_PLANE_".Length..];
            else if (key.StartsWith("CONTROLPLANE_", StringComparison.Ordinal))
                key = key["CONTROLPLANE_".Length..];

            return key switch
            {
                "MANAGED_MODE" or "ENABLED" => "ManagedControlPlane:Enabled",
                "URL" => "ManagedControlPlane:ControlPlaneUrl",
                "INSTANCE_ID" or "INSTANCEID" => "ManagedControlPlane:ManagedInstanceId",
                "REGISTRATION_TOKEN" or "REGISTRATIONTOKEN" => "ManagedControlPlane:RegistrationToken",
                "REGISTRATION_CREDENTIALS" or "REGISTRATIONCREDENTIALS" => "ManagedControlPlane:RegistrationCredentials",
                "MAXIMUM_TENANT_COUNT" or "MAXIMUMTENANTCOUNT" => "ManagedControlPlane:MaximumTenantCount",
                "TENANT_ADMINISTRATOR_SIGN_IN_URL" or "TENANTADMINISTRATORSIGNINURL" => "ManagedControlPlane:TenantAdministratorSignInUrl",
                "CREDENTIAL_LIFETIME" or "CREDENTIALLIFETIME" => "ManagedControlPlane:CredentialLifetime",
                _ => $"ManagedControlPlane:{ToPascalCase(key)}"
            };
        }

        // 7. Keycloak SMTP (/keycloak/smtp) -> Keycloak bootstrap environment names
        if (normalizedPath.Equals("keycloak/smtp", StringComparison.OrdinalIgnoreCase))
        {
            return secretKey.ToUpperInvariant() switch
            {
                "KEYCLOAK_SMTP_HOST" => "KEYCLOAK_SMTP_HOST",
                "KEYCLOAK_SMTP_PORT" => "KEYCLOAK_SMTP_PORT",
                "KEYCLOAK_SMTP_FROM" => "KEYCLOAK_SMTP_FROM",
                "KEYCLOAK_SMTP_FROM_DISPLAY_NAME" => "KEYCLOAK_SMTP_FROM_DISPLAY_NAME",
                "KEYCLOAK_SMTP_AUTH" => "KEYCLOAK_SMTP_AUTH",
                "KEYCLOAK_SMTP_SSL" => "KEYCLOAK_SMTP_SSL",
                "KEYCLOAK_SMTP_STARTTLS" => "KEYCLOAK_SMTP_STARTTLS",
                "KEYCLOAK_SMTP_REPLY_TO" => "KEYCLOAK_SMTP_REPLY_TO",
                "KEYCLOAK_SMTP_REPLY_TO_DISPLAY_NAME" => "KEYCLOAK_SMTP_REPLY_TO_DISPLAY_NAME",
                "KEYCLOAK_SMTP_ENVELOPE_FROM" => "KEYCLOAK_SMTP_ENVELOPE_FROM",
                "KEYCLOAK_SMTP_USER" => "KEYCLOAK_SMTP_USER",
                "KEYCLOAK_SMTP_PASSWORD" => "KEYCLOAK_SMTP_PASSWORD",
                _ => null
            };
        }

        // 8. Web Push (/api/webpush or /api/web-push) -> WebPush:*
        if (normalizedPath.Equals("api/webpush", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Equals("api/web-push", StringComparison.OrdinalIgnoreCase))
        {
            return secretKey.ToUpperInvariant() switch
            {
                "WEB_PUSH_ENABLED" => "WebPush:Enabled",
                "VAPID_PUBLIC_KEY" => "WebPush:VapidPublicKey",
                "VAPID_PRIVATE_KEY" => "WebPush:VapidPrivateKey",
                "VAPID_SUBJECT" => "WebPush:VapidSubject",
                _ => null
            };
        }

        // 9. Rate Limiting (/api/ratelimiting or /api/rate-limiting) -> RateLimiting:*
        if (normalizedPath.Equals("api/ratelimiting", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Equals("api/rate-limiting", StringComparison.OrdinalIgnoreCase))
        {
            if (secretKey.Contains("__", StringComparison.Ordinal))
            {
                return null;
            }

            var rateLimitingParts = secretKey.Split('_', StringSplitOptions.RemoveEmptyEntries);
            var rateLimitingConfigParts = rateLimitingParts.Select(NormalizeRateLimitingPart);
            return $"RateLimiting:{string.Join(":", rateLimitingConfigParts)}";
        }

        // 10. Flat /api folder aliases
        if (normalizedPath.Equals("api", StringComparison.OrdinalIgnoreCase))
        {
            var upper = secretKey.ToUpperInvariant();
            if (upper.StartsWith("INSTANCE__OPERATORIDENTITY__", StringComparison.Ordinal))
            {
                var clean = upper["INSTANCE__OPERATORIDENTITY__".Length..];
                return clean switch
                {
                    "OPERATOR_ID" or "OPERATORID" => "Instance:OperatorIdentity:OperatorId",
                    "PUBLIC_NAME" or "PUBLICNAME" => "Instance:OperatorIdentity:PublicName",
                    "LEGAL_NAME" or "LEGALNAME" => "Instance:OperatorIdentity:LegalName",
                    "IS_OFFICIAL_INSTANCE" or "ISOFFICIALINSTANCE" => "Instance:OperatorIdentity:IsOfficialInstance",
                    "OFFICIAL_ORIGIN" or "OFFICIALORIGIN" => "Instance:OperatorIdentity:OfficialOrigin",
                    "OPERATOR_KIND_CODE" or "OPERATORKINDCODE" => "Instance:OperatorIdentity:OperatorKindCode",
                    "JURISDICTION_COUNTRY_CODE" or "JURISDICTIONCOUNTRYCODE" => "Instance:OperatorIdentity:JurisdictionCountryCode",
                    "REGISTRATION_IDENTIFIER" or "REGISTRATIONIDENTIFIER" => "Instance:OperatorIdentity:RegistrationIdentifier",
                    "PUBLIC_CONTACT_EMAIL" or "PUBLICCONTACTEMAIL" => "Instance:OperatorIdentity:PublicContactEmail",
                    "WEBSITE_URL" or "WEBSITEURL" => "Instance:OperatorIdentity:WebsiteUrl",
                    "LEGAL_NOTICE_URL" or "LEGALNOTICEURL" => "Instance:OperatorIdentity:LegalNoticeUrl",
                    "TERMS_URL" or "TERMSURL" => "Instance:OperatorIdentity:TermsUrl",
                    "PRIVACY_URL" or "PRIVACYURL" => "Instance:OperatorIdentity:PrivacyUrl",
                    _ => $"Instance:OperatorIdentity:{ToPascalCase(clean)}"
                };
            }

            if (upper.StartsWith("INSTANCE_BOOTSTRAP_", StringComparison.Ordinal))
            {
                var clean = upper["INSTANCE_BOOTSTRAP_".Length..];
                return clean switch
                {
                    "MODE" => "Instance:Bootstrap:Mode",
                    "ADMIN_PROVIDER" or "PROVIDER" => "Instance:Bootstrap:AdminProvider",
                    "ADMIN_SUBJECT" or "SUBJECT" => "Instance:Bootstrap:AdminSubject",
                    "BINDING_GENERATION" or "GENERATION" => "Instance:Bootstrap:BindingGeneration",
                    "ADMIN_EMAIL" or "EMAIL" => "Instance:Bootstrap:AdminEmail",
                    "ADMIN_FIRST_NAME" or "FIRST_NAME" => "Instance:Bootstrap:AdminFirstName",
                    "ADMIN_LAST_NAME" or "LAST_NAME" => "Instance:Bootstrap:AdminLastName",
                    "LOCAL_PASSWORD" or "PASSWORD" => "Instance:Bootstrap:LocalPassword",
                    _ => $"Instance:Bootstrap:{ToPascalCase(clean)}"
                };
            }

            if (upper.StartsWith("CONTROL_PLANE_", StringComparison.Ordinal))
            {
                var clean = upper["CONTROL_PLANE_".Length..];
                return clean switch
                {
                    "MANAGED_MODE" or "ENABLED" => "ManagedControlPlane:Enabled",
                    "URL" => "ManagedControlPlane:ControlPlaneUrl",
                    "INSTANCE_ID" or "INSTANCEID" => "ManagedControlPlane:ManagedInstanceId",
                    "REGISTRATION_TOKEN" or "REGISTRATIONTOKEN" => "ManagedControlPlane:RegistrationToken",
                    "REGISTRATION_CREDENTIALS" or "REGISTRATIONCREDENTIALS" => "ManagedControlPlane:RegistrationCredentials",
                    "MAXIMUM_TENANT_COUNT" or "MAXIMUMTENANTCOUNT" => "ManagedControlPlane:MaximumTenantCount",
                    "TENANT_ADMINISTRATOR_SIGN_IN_URL" or "TENANTADMINISTRATORSIGNINURL" => "ManagedControlPlane:TenantAdministratorSignInUrl",
                    "CREDENTIAL_LIFETIME" or "CREDENTIALLIFETIME" => "ManagedControlPlane:CredentialLifetime",
                    _ => $"ManagedControlPlane:{ToPascalCase(clean)}"
                };
            }

            if (upper.StartsWith("RATELIMITING__", StringComparison.Ordinal))
            {
                var clean = upper["RATELIMITING__".Length..];
                var legacyRateLimitingParts = clean.Split("__", StringSplitOptions.RemoveEmptyEntries);
                var legacyRateLimitingConfigParts = legacyRateLimitingParts.Select(NormalizeRateLimitingPart);
                return $"RateLimiting:{string.Join(":", legacyRateLimitingConfigParts)}";
            }

            if (upper is "VAPID_PUBLIC_KEY" or "PUBLIC_KEY")
                return "WebPush:VapidPublicKey";
            if (upper is "VAPID_PRIVATE_KEY" or "PRIVATE_KEY")
                return "WebPush:VapidPrivateKey";
            if (upper is "VAPID_SUBJECT" or "SUBJECT")
                return "WebPush:VapidSubject";
            if (upper is "WEB_PUSH_ENABLED")
                return "WebPush:Enabled";

        }

        // 11. Special mappings for common patterns
        if (secretKey.Equals("AI_TOOL_PROPOSALS_ENABLED", StringComparison.OrdinalIgnoreCase))
        {
            return "AiProvider:ToolProposalsEnabled";
        }

        // 12. Default path to section conversion
        var pathSegments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var section = pathSegments.Length == 0 ? string.Empty : string.Join(":", pathSegments.Select(ToPascalCase)) + ":";

        // Remove section prefix from key if present
        var keyWithoutSection = secretKey;
        var sectionUpper = section.TrimEnd(':').Replace(":", "_", StringComparison.Ordinal).ToUpperInvariant();
        if (!string.IsNullOrEmpty(sectionUpper) &&
            secretKey.StartsWith(sectionUpper + "_", StringComparison.OrdinalIgnoreCase))
        {
            keyWithoutSection = secretKey[(sectionUpper.Length + 1)..];
        }

        // Handle double underscore as subsection separator
        var parts = keyWithoutSection.Split("__", StringSplitOptions.RemoveEmptyEntries);
        var configParts = parts.Select(ToPascalCase);
        var configKey = string.Join(":", configParts);

        return section + configKey;
    }

    /// <summary>
    /// Converts SCREAMING_SNAKE_CASE to PascalCase.
    /// </summary>
    private static string ToPascalCase(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var parts = input.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var pascalParts = parts.Select(part =>
        {
            if (part.Length == 0) return string.Empty;
            return char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant();
        });

        return string.Join("", pascalParts);
    }

    /// <summary>
    /// Converts PascalCase to SCREAMING_SNAKE_CASE.
    /// </summary>
    private static string ToScreamingSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (char.IsUpper(c) && i > 0 && input[i - 1] != '_')
            {
                sb.Append('_');
            }
            sb.Append(char.ToUpperInvariant(c));
        }

        return sb.ToString();
    }

    private static string NormalizeRateLimitingPart(string part)
    {
        var upper = part.ToUpperInvariant().Replace("_", "");
        return upper switch
        {
            "ANONYMOUSREGISTRATION" => "AnonymousRegistration",
            "IPPERMITLIMIT" => "IpPermitLimit",
            "SUBNETPERMITLIMIT" => "SubnetPermitLimit",
            "WINDOWSECONDS" => "WindowSeconds",
            "CONCURRENCYLIMIT" => "ConcurrencyLimit",
            "QUEUELIMIT" => "QueueLimit",
            "TOKENLIMIT" => "TokenLimit",
            "REPLENISHPERIODSECONDS" => "ReplenishPeriodSeconds",
            "TOKENSPERPERIOD" => "TokensPerPeriod",
            "PERMITLIMIT" => "PermitLimit",
            "SEGMENTSPERWINDOW" => "SegmentsPerWindow",
            "PUBLICINGESTION" => "PublicIngestion",
            "PUBLICTRANSACTIONAL" => "PublicTransactional",
            "CONTROLPLANE" => "ControlPlane",
            "GLOBAL" => "Global",
            "AUTHENTICATED" => "Authenticated",
            "WRITE" => "Write",
            _ => ToPascalCase(part)
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _reloadTimer?.Dispose();
        _disposed = true;
    }
}
