using System.Text.Json;

namespace Event.Architecture.Tests;

public sealed class KeycloakRealmOwnershipTests
{
    private static readonly string RepoRoot = ResolveRepoRoot();
    private static readonly string[] ExpectedClientIds = ["islamu-event-blazor", "islamu-event-api"];
    private static readonly string[] ExpectedProductionRedirectUris =
    [
        "http://localhost:7002/signin-oidc",
        "http://admin.localhost:7002/signin-oidc",
        "https://localhost:7177/signin-oidc",
        "https://admin.localhost:7177/signin-oidc"
    ];
    private static readonly string[] ExpectedProductionWebOrigins =
    [
        "http://localhost:7002",
        "http://admin.localhost:7002",
        "https://localhost:7177",
        "https://admin.localhost:7177"
    ];
    private static readonly string[] ExpectedProductionLogoutRedirectUris =
    [
        "http://localhost:7002/signout-callback-oidc",
        "http://admin.localhost:7002/signout-callback-oidc",
        "https://localhost:7177/signout-callback-oidc",
        "https://admin.localhost:7177/signout-callback-oidc"
    ];
    private static readonly string[] ReservedIdentifiers =
    [
        "islamu-event-control-plane",
        "event-control-plane-api",
        "event-control-plane-partner-automation",
        "control-plane-operator"
    ];

    [Test]
    [Arguments("docker/keycloak/realm-export.json")]
    [Arguments("docker/keycloak/ISLAMU-realm.test.json")]
    public async Task EventRealmExportMustContainOnlyEventOwnedResources(string relativePath)
    {
        using var document = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(RepoRoot, relativePath)));
        var root = document.RootElement;

        await Assert.That(root.GetProperty("realm").GetString()).IsEqualTo("ISLAMU");

        var clientIds = root.GetProperty("clients")
            .EnumerateArray()
            .Select(client => client.GetProperty("clientId").GetString() ?? string.Empty)
            .ToArray();
        await Assert.That(clientIds.Length).IsEqualTo(ExpectedClientIds.Length);
        await Assert.That(clientIds).IsEquivalentTo(ExpectedClientIds)
            .Because($"{relativePath} must contain exactly the two public Event clients.");

        var violations = CollectReservedIdentifierViolations(root);
        await Assert.That(violations).IsEmpty()
            .Because(string.Join('\n', violations));
    }

    [Test]
    [Arguments("docker/keycloak/realm-export.json")]
    [Arguments("docker/keycloak/ISLAMU-realm.test.json")]
    public async Task EventRealmExportsMustUseHardenedRealmSecurityDefaults(string relativePath)
    {
        using var document = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(RepoRoot, relativePath)));
        var root = document.RootElement;

        await Assert.That(root.GetProperty("sslRequired").GetString()).IsEqualTo("external");
        await Assert.That(root.GetProperty("registrationAllowed").GetBoolean()).IsTrue();
        await Assert.That(root.GetProperty("verifyEmail").GetBoolean()).IsTrue();
        await Assert.That(root.GetProperty("passwordPolicy").GetString())
            .IsEqualTo("length(12) and notUsername and notEmail and passwordHistory(5)");
        await Assert.That(root.GetProperty("ssoSessionIdleTimeout").GetInt32()).IsEqualTo(1800);
        await Assert.That(root.GetProperty("ssoSessionMaxLifespan").GetInt32()).IsEqualTo(36000);
        await Assert.That(root.GetProperty("ssoSessionIdleTimeoutRememberMe").GetInt32()).IsEqualTo(2592000);
        await Assert.That(root.GetProperty("ssoSessionMaxLifespanRememberMe").GetInt32()).IsEqualTo(7776000);
        await Assert.That(root.GetProperty("offlineSessionIdleTimeout").GetInt32()).IsEqualTo(2592000);
        await Assert.That(root.GetProperty("offlineSessionMaxLifespan").GetInt32()).IsEqualTo(7776000);
        await Assert.That(root.GetProperty("offlineSessionMaxLifespanEnabled").GetBoolean()).IsTrue();
    }

    [Test]
    public async Task ProductionRealmExportMustUseExactBffCallbacksAndNoEmbeddedClientSecret()
    {
        using var document = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(RepoRoot, "docker/keycloak/realm-export.json")));
        var blazorClient = document.RootElement.GetProperty("clients")
            .EnumerateArray()
            .Single(client => client.GetProperty("clientId").GetString() == "islamu-event-blazor");

        var redirectUris = blazorClient.GetProperty("redirectUris")
            .EnumerateArray()
            .Select(value => value.GetString() ?? string.Empty)
            .ToArray();
        var webOrigins = blazorClient.GetProperty("webOrigins")
            .EnumerateArray()
            .Select(value => value.GetString() ?? string.Empty)
            .ToArray();
        var logoutRedirectUris = blazorClient.GetProperty("attributes")
            .GetProperty("post.logout.redirect.uris")
            .GetString()!
            .Split("##", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        await Assert.That(blazorClient.TryGetProperty("secret", out _)).IsFalse();
        await Assert.That(redirectUris).IsEquivalentTo(ExpectedProductionRedirectUris);
        await Assert.That(webOrigins).IsEquivalentTo(ExpectedProductionWebOrigins);
        await Assert.That(logoutRedirectUris).IsEquivalentTo(ExpectedProductionLogoutRedirectUris);
        await Assert.That(redirectUris.Any(value => value.Contains('*', StringComparison.Ordinal))).IsFalse();
        await Assert.That(webOrigins).DoesNotContain("+");
        await Assert.That(logoutRedirectUris.Any(value => value.Contains('*', StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task KeycloakInitMustReconcileHardenedRealmAndClientSettingsWithoutStaticSecretFallback()
    {
        var script = await File.ReadAllTextAsync(
            Path.Combine(RepoRoot, "docker/keycloak/keycloak-init.sh"));
        var appHost = await File.ReadAllTextAsync(
            Path.Combine(RepoRoot, "src/Explore.AppHost/AppHost.cs"));

        await Assert.That(script).Contains("-s sslRequired=external");
        await Assert.That(script).Contains("-s verifyEmail=true");
        await Assert.That(script).Contains("-s 'passwordPolicy=length(12) and notUsername and notEmail and passwordHistory(5)'");
        await Assert.That(script).Contains("-s ssoSessionIdleTimeout=1800");
        await Assert.That(script).Contains("-s offlineSessionMaxLifespanEnabled=true");
        await Assert.That(script).Contains("-s \"webOrigins=$BLAZOR_WEB_ORIGINS\"");
        await Assert.That(script).Contains("attributes.\"pkce.code.challenge.method\"");
        await Assert.That(script).Contains(
            JsonSerializer.Serialize(ExpectedProductionRedirectUris)
                .Replace("\"", "\\\"", StringComparison.Ordinal));
        await Assert.That(script).Contains(
            JsonSerializer.Serialize(ExpectedProductionWebOrigins)
                .Replace("\"", "\\\"", StringComparison.Ordinal));
        await Assert.That(script).Contains(string.Join("##", ExpectedProductionLogoutRedirectUris));
        await Assert.That(script).DoesNotContain("attributes=$attributes");
        await Assert.That(script).DoesNotContain("DEFAULT_LOCAL_BLAZOR_SECRET");
        await Assert.That(script).DoesNotContain("KEYCLOAK_INIT_ALLOW_DEFAULT_LOCAL_SECRET");
        await Assert.That(script).DoesNotContain("webOrigins=[\"+\"]");
        await Assert.That(script).DoesNotContain("/*");
        await Assert.That(appHost).DoesNotContain("islamu-event-blazor-secret");
        await Assert.That(appHost).DoesNotContain("new GenerateParameterDefault");
        await Assert.That(appHost).Contains("configuration[\"KEYCLOAK_BLAZOR_CLIENT_SECRET\"] ?? string.Empty");
        await Assert.That(appHost).Contains(".WithEnvironment(\"KEYCLOAK_BLAZOR_REDIRECT_URIS\"");
        await Assert.That(appHost).Contains(".WithEnvironment(\"KEYCLOAK_BLAZOR_WEB_ORIGINS\"");
        await Assert.That(appHost).Contains(".WithEnvironment(\"KEYCLOAK_BLAZOR_LOGOUT_REDIRECT_URIS\"");
    }

    [Test]
    public async Task AspireKeycloakInitMustUseAllocatedBffPortsForExactCallbacks()
    {
        var appHost = await File.ReadAllTextAsync(
            Path.Combine(RepoRoot, "src/Explore.AppHost/AppHost.cs"));

        await Assert.That(appHost).Contains(
            ".GetEndpoint(\"http\", KnownNetworkIdentifiers.LocalhostNetwork)");
        await Assert.That(appHost).Contains(
            ".GetEndpoint(\"https\", KnownNetworkIdentifiers.LocalhostNetwork)");
        await Assert.That(appHost).Contains("http://localhost:{httpPort}/signin-oidc");
        await Assert.That(appHost).Contains("https://localhost:{httpsPort}/signin-oidc");
        await Assert.That(appHost).Contains("http://localhost:{httpPort}/signout-callback-oidc");
        await Assert.That(appHost).Contains("https://localhost:{httpsPort}/signout-callback-oidc");
        await Assert.That(appHost).DoesNotContain(
            ".WithEnvironment(\"KEYCLOAK_BLAZOR_REDIRECT_URIS\", configuration[\"KEYCLOAK_BLAZOR_REDIRECT_URIS\"] ?? string.Empty)");
    }

    [Test]
    public async Task ReservedIdentifierCollectorReportsBoundedDiagnosticAndCleansTemporaryFixture()
    {
        var violations = await CollectViolationsFromTemporaryFixtureAsync(
            """
            {
              "realm": "ISLAMU",
              "clients": [{ "clientId": "event-control-plane-api" }],
              "clientScopes": [],
              "roles": { "realm": [] },
              "users": []
            }
            """);

        await Assert.That(violations).IsEquivalentTo(
            ["client ID contains reserved identifier 'event-control-plane-api'."]);
        await Assert.That(violations.All(violation => violation.Length <= 128)).IsTrue();
    }

    [Test]
    [Arguments(
        """
        {
          "clients": [
            { "clientId": "islamu-event-blazor", "description": "event-control-plane-api" },
            { "clientId": "islamu-event-api" }
          ],
          "clientScopes": [],
          "roles": { "realm": [] },
          "users": []
        }
        """,
        "client contains reserved identifier 'event-control-plane-api'.")]
    [Arguments(
        """
        {
          "clients": [
            { "clientId": "islamu-event-blazor" },
            { "clientId": "islamu-event-api" }
          ],
          "clientScopes": [],
          "roles": { "realm": { "name": "control-plane-operator" } },
          "users": []
        }
        """,
        "roles.realm must be an array.")]
    [Arguments(
        """
        {
          "clients": [
            { "clientId": "islamu-event-blazor" },
            { "clientId": "islamu-event-api" }
          ],
          "clientScopes": [],
          "defaultDefaultClientScopes": ["event-control-plane-partner-automation"],
          "roles": { "realm": [] },
          "users": []
        }
        """,
        "client scope contains reserved identifier 'event-control-plane-partner-automation'.")]
    public async Task ReservedIdentifierCollectorRejectsFailOpenBypassFixtures(
        string fixtureJson,
        string expectedViolation)
    {
        var violations = await CollectViolationsFromTemporaryFixtureAsync(fixtureJson);

        await Assert.That(violations).Contains(expectedViolation);
        await Assert.That(violations.All(violation => violation.Length <= 128)).IsTrue();
    }

    private static string[] CollectReservedIdentifierViolations(JsonElement root)
    {
        var violations = new HashSet<string>(StringComparer.Ordinal);
        if (!IsExpectedObject(root, "root", violations))
        {
            return violations.ToArray();
        }

        foreach (var client in EnumerateArray(root, "clients", "clients", violations))
        {
            if (!IsExpectedObject(client, "clients[]", violations))
            {
                continue;
            }

            CollectScopeReferences(
                client,
                "defaultClientScopes",
                "clients[].defaultClientScopes",
                violations);
            CollectScopeReferences(
                client,
                "optionalClientScopes",
                "clients[].optionalClientScopes",
                violations);

            foreach (var property in client.EnumerateObject())
            {
                if (property.Name is "protocolMappers" or "defaultClientScopes" or "optionalClientScopes")
                {
                    continue;
                }

                var category = property.NameEquals("clientId") ? "client ID" : "client";
                CollectStringViolations(property.Value, category, violations);
            }

            CollectMapperViolations(client, "clients[].protocolMappers", violations);
        }

        CollectScopeReferences(
            root,
            "defaultDefaultClientScopes",
            "defaultDefaultClientScopes",
            violations);
        CollectScopeReferences(
            root,
            "defaultOptionalClientScopes",
            "defaultOptionalClientScopes",
            violations);

        foreach (var clientScope in EnumerateArray(root, "clientScopes", "clientScopes", violations))
        {
            if (!IsExpectedObject(clientScope, "clientScopes[]", violations))
            {
                continue;
            }

            foreach (var property in clientScope.EnumerateObject())
            {
                if (!property.NameEquals("protocolMappers"))
                {
                    CollectStringViolations(property.Value, "client scope", violations);
                }
            }

            CollectMapperViolations(clientScope, "clientScopes[].protocolMappers", violations);
        }

        if (root.TryGetProperty("roles", out var roles))
        {
            if (!IsExpectedObject(roles, "roles", violations))
            {
                return violations.Order(StringComparer.Ordinal).ToArray();
            }

            foreach (var realmRole in EnumerateArray(roles, "realm", "roles.realm", violations))
            {
                CollectStringViolations(realmRole, "realm role", violations);
            }
        }

        foreach (var user in EnumerateArray(root, "users", "users", violations))
        {
            CollectStringViolations(user, "user", violations);
        }

        return violations.Order(StringComparer.Ordinal).ToArray();
    }

    private static async Task<string[]> CollectViolationsFromTemporaryFixtureAsync(string fixtureJson)
    {
        var fixturePath = Path.Combine(
            Path.GetTempPath(),
            $"event-keycloak-realm-ownership-{Guid.NewGuid():N}.json");
        string[] violations;

        try
        {
            await File.WriteAllTextAsync(fixturePath, fixtureJson);
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(fixturePath));
            violations = CollectReservedIdentifierViolations(document.RootElement);
        }
        finally
        {
            File.Delete(fixturePath);
        }

        await Assert.That(File.Exists(fixturePath)).IsFalse();
        return violations;
    }

    private static void CollectScopeReferences(
        JsonElement owner,
        string propertyName,
        string path,
        HashSet<string> violations)
    {
        foreach (var clientScope in EnumerateArray(owner, propertyName, path, violations))
        {
            CollectStringViolations(clientScope, "client scope", violations);
        }
    }

    private static void CollectMapperViolations(
        JsonElement owner,
        string path,
        HashSet<string> violations)
    {
        foreach (var mapper in EnumerateArray(owner, "protocolMappers", path, violations))
        {
            if (!IsExpectedObject(mapper, $"{path}[]", violations))
            {
                continue;
            }

            if (mapper.TryGetProperty("name", out var name))
            {
                CollectStringViolations(name, "mapper name", violations);
            }

            if (mapper.TryGetProperty("config", out var config))
            {
                CollectStringViolations(config, "mapper config", violations);
            }
        }
    }

    private static void CollectStringViolations(
        JsonElement element,
        string category,
        HashSet<string> violations)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var value = element.GetString();
                foreach (var reservedIdentifier in ReservedIdentifiers)
                {
                    if (value?.Contains(reservedIdentifier, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        violations.Add($"{category} contains reserved identifier '{reservedIdentifier}'.");
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectStringViolations(item, category, violations);
                }

                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    CollectStringViolations(property.Value, category, violations);
                }

                break;
        }
    }

    private static IEnumerable<JsonElement> EnumerateArray(
        JsonElement owner,
        string propertyName,
        string path,
        HashSet<string> violations)
    {
        if (!owner.TryGetProperty(propertyName, out var array))
        {
            yield break;
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            violations.Add($"{path} must be an array.");
            yield break;
        }

        foreach (var item in array.EnumerateArray())
        {
            yield return item;
        }
    }

    private static bool IsExpectedObject(
        JsonElement element,
        string path,
        HashSet<string> violations)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        violations.Add($"{path} must be an object.");
        return false;
    }

    private static string ResolveRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Explore.slnx"))
                && Directory.Exists(Path.Combine(current.FullName, "docker", "keycloak")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate repository root containing Explore.slnx and docker/keycloak.");
    }
}
