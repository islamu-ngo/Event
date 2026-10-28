namespace Explore.ServiceDefaults.Secrets;

internal static class SecretStartupConfigurationProjection
{
    internal static IReadOnlyList<string> Destinations(string folder, string key)
    {
        if (SecretAcquisitionPolicy.IsAuthorityInput(key)
            || key.Contains("${", StringComparison.Ordinal) || key.Contains('/', StringComparison.Ordinal))
            throw new InvalidOperationException("secret_consumer_scope_invalid");
        if ((key.StartsWith("KEYCLOAK", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("CERBOS", StringComparison.OrdinalIgnoreCase)
                || key.Equals("GOOGLE_CLIENT_SECRET", StringComparison.OrdinalIgnoreCase)
                || key.Equals("ATPROTO_OAUTH_CLIENT_PRIVATE_JWKS", StringComparison.OrdinalIgnoreCase))
            && !folder.StartsWith("/runtime/", StringComparison.Ordinal))
            throw new InvalidOperationException("secret_consumer_scope_invalid");
        string? canonical = MapInfisical(key, folder);
        if (canonical is null)
            return [];
        var destinations = new List<string> { canonical };
        if (!folder.StartsWith("/runtime/bff/", StringComparison.Ordinal)
            && (!folder.StartsWith("/database/", StringComparison.Ordinal)
                || folder == "/database/identity" && key.StartsWith("IDENTITY_DATABASE_", StringComparison.OrdinalIgnoreCase)
                || folder == "/database/erasure" && key.StartsWith("ERASURE_DATABASE_", StringComparison.OrdinalIgnoreCase)))
            destinations.Add(key);
        if (canonical == "Database:Name")
            destinations.Add("Database:Database");
        if (canonical.StartsWith("Database:Erasure:", StringComparison.Ordinal))
        {
            string suffix = canonical["Database:Erasure:".Length..];
            destinations.Add("PrivacyErasureAuthorityDatabase:" + suffix);
            destinations.Add("DatabaseErasure:" + suffix);
        }
        if (canonical.StartsWith("Instance:OperatorIdentity:", StringComparison.Ordinal))
            destinations.Add("INSTANCE__OPERATORIDENTITY__" + canonical["Instance:OperatorIdentity:".Length..].ToUpperInvariant());
        if (canonical.StartsWith("Instance:Bootstrap:", StringComparison.Ordinal))
        {
            string suffix = canonical["Instance:Bootstrap:".Length..];
            var flat = new System.Text.StringBuilder("INSTANCE_BOOTSTRAP_");
            for (int index = 0; index < suffix.Length; index++)
            {
                if (index > 0 && char.IsUpper(suffix[index]) && suffix[index - 1] != '_')
                    flat.Append('_');
                flat.Append(char.ToUpperInvariant(suffix[index]));
            }
            destinations.Add(flat.ToString());
        }
        string? alias = canonical switch
        {
            "ManagedControlPlane:Enabled" => "CONTROL_PLANE_MANAGED_MODE",
            "ManagedControlPlane:ControlPlaneUrl" => "CONTROL_PLANE_URL",
            "ManagedControlPlane:ManagedInstanceId" => "CONTROL_PLANE_INSTANCE_ID",
            "ManagedControlPlane:RegistrationToken" => "CONTROL_PLANE_REGISTRATION_TOKEN",
            "ManagedControlPlane:RegistrationCredentials" => "CONTROL_PLANE_REGISTRATION_CREDENTIALS",
            "ManagedControlPlane:MaximumTenantCount" => "CONTROL_PLANE_MAXIMUM_TENANT_COUNT",
            "ManagedControlPlane:TenantAdministratorSignInUrl" => "CONTROL_PLANE_TENANT_ADMINISTRATOR_SIGN_IN_URL",
            "ManagedControlPlane:CredentialLifetime" => "CONTROL_PLANE_CREDENTIAL_LIFETIME",
            "WebPush:Enabled" => "WEB_PUSH_ENABLED",
            "WebPush:VapidPublicKey" => "VAPID_PUBLIC_KEY",
            "WebPush:VapidPrivateKey" => "VAPID_PRIVATE_KEY",
            "WebPush:VapidSubject" => "VAPID_SUBJECT",
            _ => null
        };
        if (alias is not null)
            destinations.Add(alias);
        if (canonical == "ManagedControlPlane:Enabled")
            destinations.Add("CONTROL_PLANE_ENABLED");
        return destinations.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static bool TryMapLocal(SecretConsumerRole consumer, string key, out string folder)
    {
        folder = string.Empty;
        string normalized = key.Replace("__", ":", StringComparison.Ordinal);
        if (SecretAcquisitionPolicy.IsAuthorityInput(key) || SecretAcquisitionPolicy.IsAuthorityInput(normalized))
            return false;
        string? selected = normalized.ToUpperInvariant() switch
        {
            "KEYCLOAK_BLAZOR_CLIENT_SECRET" or "KEYCLOAK_CLIENT_SECRET"
                or "KEYCLOAK:CLIENTSECRET" or "KEYCLOAK:BLAZORCLIENTSECRET" => "/runtime/bff/oidc/keycloak",
            "ATPROTO_OAUTH_CLIENT_PRIVATE_JWKS" or "ATPROTO:OAUTHCLIENTPRIVATEJWKS" => "/runtime/bff/atproto/oauth",
            "SETUP_SECRET" => "/setup",
            "MODERATION:RECONCILIATION:APIKEY" => "/api",
            _ => null
        };
        if (selected is null)
        {
            (string Prefix, string Folder)[] special =
            [
                ("KEYCLOAK_LIFECYCLE_", "/runtime/api/keycloak/lifecycle"),
                ("Keycloak:Lifecycle:", "/runtime/api/keycloak/lifecycle"),
                ("KEYCLOAK_", "/runtime/api/authentication/keycloak"),
                ("Keycloak:", "/runtime/api/authentication/keycloak"),
                ("CERBOS_", "/runtime/api/authorization/cerbos"), ("Cerbos:", "/runtime/api/authorization/cerbos"),
                ("GOOGLE_", "/runtime/bff/oidc/google"), ("Google:", "/runtime/bff/oidc/google"),
                ("MAIL_", "/smtp"), ("SMTP_", "/smtp"), ("Smtp:", "/smtp"),
                ("AUTHENTICATION_", "/api"), ("Authentication:", "/api"),
                ("AUTHORIZATION_", "/api"), ("Authorization:", "/api"),
                ("GEOCODING_", "/api"), ("Geocoding:", "/api"),
                ("LISTMONK_", "/integrations/listmonk"),
                ("DEPLOYMENT_MODE", "/api"),
                ("DATABASE_", "/database"), ("Database:", "/database"),
                ("IDENTITY_DATABASE_", "/database/identity"), ("IdentityDatabase:", "/database/identity"),
                ("ERASURE_DATABASE_", "/database/erasure"), ("Database:Erasure:", "/database/erasure"),
                ("PRIVACY_ERASURE_AUTHORITY_", "/database/erasure"),
                ("PrivacyErasureAuthorityDatabase:", "/database/erasure"), ("DatabaseErasure:", "/database/erasure"),
                ("PrivacyErasure:Authority:Topology", "/database/erasure"),
                ("Instance:Bootstrap:", "/api/bootstrap"), ("INSTANCE_BOOTSTRAP_", "/api/bootstrap"),
                ("Instance:OperatorIdentity:", "/api/operator-identity"), ("OPERATOR_IDENTITY_", "/api/operator-identity"),
                ("CONTROL_PLANE_", "/api/controlplane"), ("ManagedControlPlane:", "/api/controlplane"),
                ("VAPID_", "/api/webpush"), ("WEB_PUSH_", "/api/webpush"), ("WebPush:", "/api/webpush"),
                ("AiProvider:", "/ai")
            ];
            selected = special.Where(item => normalized.StartsWith(item.Prefix, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.Prefix.Length).Select(item => item.Folder).FirstOrDefault();
        }
        if (selected is null)
        {
            selected = SecretAcquisitionPolicy.Roots(consumer).OrderByDescending(root => root.Length)
                .FirstOrDefault(root =>
                {
                    string flat = root.Trim('/').Replace('/', '_').Replace('-', '_');
                    string structured = string.Join(':', root.Trim('/').Split('/').Select(ToPascalCase));
                    return normalized.StartsWith(flat + "_", StringComparison.OrdinalIgnoreCase)
                        || normalized.StartsWith(structured + ":", StringComparison.OrdinalIgnoreCase);
                });
        }
        if (selected is null || !SecretAcquisitionPolicy.AllowsRoot(consumer, selected))
            return false;
        folder = selected;
        return true;
    }

    internal static string? MapInfisical(string secretKey, string path)
    {
        const string permittedClientPrefix = "KEYCLOAK__PERMITTEDCLIENTS__";
        if (path == "/runtime/api/authentication/keycloak"
            && secretKey.StartsWith(permittedClientPrefix, StringComparison.Ordinal)
            && int.TryParse(secretKey[permittedClientPrefix.Length..],
                System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _))
            return secretKey.Replace("__", ":", StringComparison.Ordinal);
        if (path.StartsWith("/runtime/", StringComparison.Ordinal))
            return (path, secretKey) switch
            {
                ("/runtime/api/authentication/keycloak", "KEYCLOAK_ENDPOINT") => "Keycloak:Endpoint",
                ("/runtime/api/authentication/keycloak", "KEYCLOAK_REALM") => "Keycloak:Realm",
                ("/runtime/api/authentication/keycloak", "KEYCLOAK_CLIENT_ID") => "Keycloak:ClientId",
                ("/runtime/api/authentication/keycloak", "KEYCLOAK__AUDIENCE") => "Keycloak:Audience",
                ("/runtime/api/authentication/keycloak", "KEYCLOAK_INTERNAL_URL") => "Keycloak:InternalUrl",
                ("/runtime/api/authentication/keycloak", "KEYCLOAK_REQUIRE_HTTPS_METADATA") => "Keycloak:RequireHttpsMetadata",
                ("/runtime/api/keycloak/lifecycle", "KEYCLOAK_LIFECYCLE_CLIENT_SECRET") => "Keycloak:Lifecycle:ClientSecret",
                ("/runtime/api/authorization/cerbos", "CERBOS_GRPC_ENDPOINT") => "Cerbos:GrpcEndpoint",
                ("/runtime/api/authorization/cerbos", "CERBOS_HTTP_ENDPOINT") => "Cerbos:HttpEndpoint",
                ("/runtime/api/authorization/cerbos", "CERBOS_USE_POLICY_SCOPE") => "Cerbos:UsePolicyScope",
                ("/runtime/api/authorization/cerbos", "CERBOS_USE_TLS") => "Cerbos:UseTls",
                ("/runtime/api/authorization/cerbos", "CERBOS_PLAINTEXT_MODE") => "Cerbos:PlaintextMode",
                ("/runtime/bff/oidc/keycloak", "KEYCLOAK_BLAZOR_CLIENT_SECRET") => "Keycloak:BlazorClientSecret",
                ("/runtime/bff/oidc/keycloak", "KEYCLOAK_ENDPOINT") => "Bff:Keycloak:Endpoint",
                ("/runtime/bff/oidc/keycloak", "KEYCLOAK_REALM") => "Bff:Keycloak:Realm",
                ("/runtime/bff/oidc/keycloak", "KEYCLOAK_CLIENT_ID") => "Bff:Keycloak:ClientId",
                ("/runtime/bff/oidc/google", "GOOGLE_CLIENT_ID") => "Google:ClientId",
                ("/runtime/bff/oidc/google", "GOOGLE_CLIENT_SECRET") => "Google:ClientSecret",
                ("/runtime/bff/atproto/oauth", "ATPROTO_OAUTH_CLIENT_PRIVATE_JWKS") => "Atproto:OauthClientPrivateJwks",
                _ => throw new InvalidOperationException("secret_authority_invalid")
            };
        var normalizedPath = path.Trim('/');

        // 1. Privacy Erasure Authority Database (/database/erasure) -> Database:Erasure:*
        if (normalizedPath.Equals("database/erasure", StringComparison.OrdinalIgnoreCase))
        {
            // ERASURE_DATABASE_ is the external authority contract. Exact folder provenance
            // keeps erasure fields separate from primary database fields.
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
                "IDENTITY_DATABASE_HOST" or "HOST" => "IdentityDatabase:Host",
                "IDENTITY_DATABASE_PORT" or "PORT" => "IdentityDatabase:Port",
                "IDENTITY_DATABASE_NAME" or "DATABASE" or "NAME" => "IdentityDatabase:Name",
                "IDENTITY_DATABASE_SCHEMA" or "SCHEMA" => "IdentityDatabase:Schema",
                "IDENTITY_DATABASE_RUNTIME_USERNAME" or "RUNTIME_USERNAME" => "IdentityDatabase:Runtime:Username",
                "IDENTITY_DATABASE_RUNTIME_PASSWORD" or "RUNTIME_PASSWORD" => "IdentityDatabase:Runtime:Password",
                "IDENTITY_DATABASE_MIGRATOR_USERNAME" or "MIGRATOR_USERNAME" => "IdentityDatabase:Migrator:Username",
                "IDENTITY_DATABASE_MIGRATOR_PASSWORD" or "MIGRATOR_PASSWORD" => "IdentityDatabase:Migrator:Password",
                "IDENTITY_DATABASE_LOCAL_IDENTITY_WRITER_USERNAME" or "LOCAL_IDENTITY_WRITER_USERNAME" => "IdentityDatabase:LocalIdentityWriter:Username",
                "IDENTITY_DATABASE_LOCAL_IDENTITY_WRITER_PASSWORD" or "LOCAL_IDENTITY_WRITER_PASSWORD" => "IdentityDatabase:LocalIdentityWriter:Password",
                "IDENTITY_DATABASE_SERVER_VERSION" or "SERVER_VERSION" => "IdentityDatabase:ServerVersion",
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
                "DATABASE_GLOBAL_USERNAME" or "GLOBAL_USERNAME" => "Database:Global:Username",
                "DATABASE_GLOBAL_PASSWORD" or "GLOBAL_PASSWORD" => "Database:Global:Password",
                "DATABASE_LOCAL_IDENTITY_WRITER_USERNAME" or "LOCAL_IDENTITY_WRITER_USERNAME" => "Database:LocalIdentityWriter:Username",
                "DATABASE_LOCAL_IDENTITY_WRITER_PASSWORD" or "LOCAL_IDENTITY_WRITER_PASSWORD" => "Database:LocalIdentityWriter:Password",
                "DATABASE_INSTANCE_KEY_READ_USERNAME" or "INSTANCE_KEY_READ_USERNAME" => "Database:InstanceKeyRead:Username",
                "DATABASE_INSTANCE_KEY_READ_PASSWORD" or "INSTANCE_KEY_READ_PASSWORD" => "Database:InstanceKeyRead:Password",
                "DATABASE_INSTANCE_KEY_WRITE_USERNAME" or "INSTANCE_KEY_WRITE_USERNAME" => "Database:InstanceKeyWrite:Username",
                "DATABASE_INSTANCE_KEY_WRITE_PASSWORD" or "INSTANCE_KEY_WRITE_PASSWORD" => "Database:InstanceKeyWrite:Password",
                "DATABASE_INSTANCE_KEY_ISSUANCE_USERNAME" or "INSTANCE_KEY_ISSUANCE_USERNAME" => "Database:InstanceKeyIssuance:Username",
                "DATABASE_INSTANCE_KEY_ISSUANCE_PASSWORD" or "INSTANCE_KEY_ISSUANCE_PASSWORD" => "Database:InstanceKeyIssuance:Password",
                "DATABASE_PLATFORM_KEY_REPORT_USERNAME" or "PLATFORM_KEY_REPORT_USERNAME" => "Database:PlatformKeyReport:Username",
                "DATABASE_PLATFORM_KEY_REPORT_PASSWORD" or "PLATFORM_KEY_REPORT_PASSWORD" => "Database:PlatformKeyReport:Password",
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

        // 7. Web Push (/api/webpush or /api/web-push) -> WebPush:*
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
            if (upper == "MODERATION__RECONCILIATION__APIKEY")
                return "Moderation:Reconciliation:ApiKey";
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

        if (normalizedPath.Equals("api", StringComparison.OrdinalIgnoreCase)
            && secretKey.Equals("MODERATION__RECONCILIATION__APIKEY", StringComparison.OrdinalIgnoreCase))
            return "Moderation:Reconciliation:ApiKey";

        if (normalizedPath.Equals("moderation", StringComparison.OrdinalIgnoreCase))
        {
            if (secretKey.Equals("MODERATION_MEASUREMENT_SIGNING_PRIVATE_KEY_PEM", StringComparison.OrdinalIgnoreCase))
                return "Moderation:MeasurementSigningPrivateKeyPem";
            const string testDatabasePrefix = "MODERATION_MEASUREMENT_TEST_DATABASE__";
            if (secretKey.StartsWith(testDatabasePrefix, StringComparison.OrdinalIgnoreCase))
                return "Moderation:MeasurementTestDatabase:" + secretKey[testDatabasePrefix.Length..];
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

    private static string NormalizeRateLimitingPart(string part)
    {
        var upper = part.ToUpperInvariant().Replace("_", "", StringComparison.Ordinal);
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

}
