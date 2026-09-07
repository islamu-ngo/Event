using System.Reflection;
using System.Text.Json;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.DTOs.Footer;
using Explore.Application.DTOs.Instance;
using Explore.Application.DTOs.Onboarding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Event.Architecture.Tests;

public sealed class InstanceOnboardingOpenApiContractTests
{
    private static readonly (string Path, string Method, string Schema)[] Operations =
    [
        ("/api/instanceonboarding/validate-secret", "post", "SetupSecretValidationResultDto"),
        ("/api/instance/settings/smtp/test", "post", "SmtpConnectionTestResultDto"),
        ("/api/instance/settings/auth-provider/status", "get", "ProviderConfigurationStatusDto"),
        ("/api/instance/settings/authz-provider/status", "get", "ProviderConfigurationStatusDto")
    ];

    [Test]
    public async Task InstanceProbeOperations_MustDeclareConcreteSuccessSchemas()
    {
        var repositoryRoot = ResolveRepositoryRoot();
        var schemaPath = Path.Combine(repositoryRoot, "schemas", "openapi_islamu-event.json");
        await using var schemaStream = File.OpenRead(schemaPath);
        using var document = await JsonDocument.ParseAsync(schemaStream);

        foreach (var (path, method, expectedSchema) in Operations)
        {
            var response = document.RootElement
                .GetProperty("paths")
                .GetProperty(path)
                .GetProperty(method)
                .GetProperty("responses")
                .GetProperty("200");
            var schemaReference = response
                .GetProperty("content")
                .EnumerateObject()
                .First(content => content.Name.StartsWith("application/json", StringComparison.Ordinal))
                .Value
                .GetProperty("schema")
                .GetProperty("$ref")
                .GetString();

            await Assert.That(schemaReference).IsEqualTo($"#/components/schemas/{expectedSchema}");
        }
    }

    [Test]
    public async Task SaveInstanceOnboardingProfile_MustExposeTheGeneratedPatchContract()
    {
        var repositoryRoot = ResolveRepositoryRoot();
        var schemaPath = Path.Combine(repositoryRoot, "schemas", "openapi_islamu-event.json");
        await using var schemaStream = File.OpenRead(schemaPath);
        using var document = await JsonDocument.ParseAsync(schemaStream);

        JsonElement operation = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/instanceonboarding/profile")
            .GetProperty("patch");
        string? requestSchema = operation
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("application/json; v=0.1")
            .GetProperty("schema")
            .GetProperty("$ref")
            .GetString();
        string? responseSchema = operation
            .GetProperty("responses")
            .GetProperty("200")
            .GetProperty("content")
            .GetProperty("application/json; v=0.1")
            .GetProperty("schema")
            .GetProperty("$ref")
            .GetString();
        var generatedClientPath = Path.Combine(
            repositoryRoot,
            "src",
            "Explore.Blazor.Client",
            "Clients",
            "EventApiTagClients.g.cs");
        var generatedClient = await File.ReadAllTextAsync(generatedClientPath);

        await Assert.That(operation.GetProperty("operationId").GetString()).IsEqualTo("SaveInstanceOnboardingProfile");
        await Assert.That(requestSchema).IsEqualTo("#/components/schemas/SelfHostOnboardingProfileDto");
        await Assert.That(responseSchema).IsEqualTo("#/components/schemas/BaseCommandResponseOfGuid");
        await Assert.That(generatedClient).Contains("SaveInstanceOnboardingProfileAsync", StringComparison.Ordinal);
    }

    [Test]
    public async Task InstanceSettingsWrites_MustUseDedicatedPatchContracts_AndOnboardingWriteAliasesMustBeAbsent()
    {
        (string ActionName, string Template, string RouteName, Type RequestType)[] patchActions =
        [
            (nameof(InstanceGovernanceSettingsController.UpdateModuleSettings), "modules", RouteNames.UpdateInstanceModuleSettings, typeof(PatchModuleSettingsDto)),
            (nameof(InstanceGovernanceSettingsController.UpdateEventPolicy), "events", RouteNames.UpdateInstanceEventPolicy, typeof(PatchEventPolicyDto)),
            (nameof(InstanceGovernanceSettingsController.UpdateOrganizationPolicy), "organizations", RouteNames.UpdateInstanceOrganizationPolicy, typeof(PatchOrganizationPolicyDto)),
            (nameof(InstancePresentationSettingsController.UpdateBrandingSettings), "branding", RouteNames.UpdateInstanceBrandingSettings, typeof(PatchBrandingSettingsDto)),
            (nameof(InstancePresentationSettingsController.UpdateDomainSettings), "domains", RouteNames.UpdateInstanceDomainSettings, typeof(PatchDomainSettingsDto)),
            (nameof(InstanceGovernanceSettingsController.UpdateTenantDelegationSettings), "tenant-delegation", RouteNames.UpdateInstanceTenantDelegationSettings, typeof(PatchTenantDelegationSettingsDto)),
            (nameof(InstancePresentationSettingsController.UpdateAdminPortalSettings), "admin-portal", RouteNames.UpdateInstanceAdminPortalSettings, typeof(PatchAdminPortalSettingsDto)),
            (nameof(InstanceGovernanceSettingsController.UpdateAiAssistantGovernanceSettings), "ai-assistant", RouteNames.UpdateInstanceAiAssistantGovernanceSettings, typeof(PatchAiAssistantGovernanceSettingsDto)),
            (nameof(InstanceGovernanceSettingsController.UpdateMcpGovernanceSettings), "mcp", RouteNames.UpdateInstanceMcpGovernanceSettings, typeof(PatchMcpGovernanceSettingsDto)),
            (nameof(InstancePresentationSettingsController.UpdateRenderPolicySettings), "render-policy", RouteNames.UpdateInstanceRenderPolicySettings, typeof(PatchRenderPolicySettingsDto)),
            (nameof(InstanceStorageSettingsController.UpdateStorageSettings), "storage", RouteNames.UpdateInstanceStorageSettings, typeof(PatchInstanceStorageSettingsDto)),
            (nameof(InstanceMessagingSettingsController.UpdateSmtpSettings), "smtp", RouteNames.UpdateInstanceSmtpSettings, typeof(PatchInstanceSmtpSettingsDto)),
            (nameof(InstanceMessagingSettingsController.UpdateResolverConfiguration), "resolver-config", RouteNames.UpdateInstanceResolverConfiguration, typeof(PatchResolverConfigurationDto)),
            (nameof(InstanceGovernanceSettingsController.UpdateAnalyticsGovernanceSettings), "analytics-governance", RouteNames.UpdateInstanceAnalyticsGovernanceSettings, typeof(PatchAnalyticsGovernanceSettingsDto)),
            (nameof(InstanceGovernanceSettingsController.UpdateFooterGovernanceSettings), "footer-governance", RouteNames.UpdateFooterGovernanceSettings, typeof(PatchFooterGovernanceSettingsDto)),
            (nameof(InstanceAuthenticationSettingsController.UpdateAuthProviderConfiguration), "auth-provider", RouteNames.UpdateInstanceAuthProviderConfiguration, typeof(PatchAuthProviderConfigurationDto)),
            (nameof(InstanceAuthorizationSettingsController.UpdateAuthorizationProviderConfiguration), "authz-provider", RouteNames.UpdateInstanceAuthorizationProviderConfiguration, typeof(PatchAuthorizationProviderConfigurationDto))
        ];

        // Each settings capability is its own controller now; every one of them must still be [Authorize].
        foreach (var settingsController in new[]
                 {
                     typeof(InstanceGovernanceSettingsController),
                     typeof(InstancePresentationSettingsController),
                     typeof(InstanceStorageSettingsController),
                     typeof(InstanceMessagingSettingsController),
                     typeof(InstanceAuthenticationSettingsController),
                     typeof(InstanceAuthorizationSettingsController),
                 })
        {
            await Assert.That(settingsController.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        }

        foreach (var (actionName, template, routeName, requestType) in patchActions)
        {
            var action = new[]
            {
                typeof(InstanceGovernanceSettingsController),
                typeof(InstancePresentationSettingsController),
                typeof(InstanceStorageSettingsController),
                typeof(InstanceMessagingSettingsController),
                typeof(InstanceAuthenticationSettingsController),
                typeof(InstanceAuthorizationSettingsController),
            }.Select(type => type.GetMethod(actionName)).Single(method => method is not null)!;
            var patch = action.GetCustomAttribute<HttpPatchAttribute>();
            var body = action.GetParameters().Single(parameter => parameter.GetCustomAttribute<FromBodyAttribute>() is not null);

            await Assert.That(patch).IsNotNull();
            await Assert.That(patch!.Template).IsEqualTo(template);
            await Assert.That(patch.Name).IsEqualTo(routeName);
            await Assert.That(action.GetCustomAttribute<HttpPutAttribute>()).IsNull();
            await Assert.That(action.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
            await Assert.That(body.ParameterType).IsEqualTo(requestType);
        }

        var obsoleteTemplates = new HashSet<string>(StringComparer.Ordinal)
        {
            "auth-provider-configuration",
            "authz-provider-configuration"
        };
        var obsoleteActions = typeof(InstanceOnboardingController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(action => action.GetCustomAttributes<HttpMethodAttribute>())
            .Where(attribute => obsoleteTemplates.Contains(attribute.Template ?? string.Empty)
                && attribute.HttpMethods.Any(method => !string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)))
            .Select(attribute => attribute.Template)
            .ToList();

        await Assert.That(obsoleteActions).IsEmpty();
    }

    [Test]
    public async Task TenantDelegationContract_MustNotExposeLegacyDecentralizationControls()
    {
        var serializedDto = JsonSerializer.Serialize(
            new TenantDelegationSettingsDto(),
            JsonSerializerOptions.Web);
        using var serializedDocument = JsonDocument.Parse(serializedDto);

        await Assert.That(serializedDocument.RootElement.TryGetProperty("decentralizationEnabled", out _)).IsFalse();
        await Assert.That(serializedDocument.RootElement.TryGetProperty("lockDecentralizationEnabled", out _)).IsFalse();

        var repositoryRoot = ResolveRepositoryRoot();
        var schemaPath = Path.Combine(repositoryRoot, "schemas", "openapi_islamu-event.json");
        await using var schemaStream = File.OpenRead(schemaPath);
        using var schemaDocument = await JsonDocument.ParseAsync(schemaStream);
        var properties = schemaDocument.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(nameof(TenantDelegationSettingsDto))
            .GetProperty("properties");

        await Assert.That(properties.TryGetProperty("decentralizationEnabled", out _)).IsFalse();
        await Assert.That(properties.TryGetProperty("lockDecentralizationEnabled", out _)).IsFalse();

        var generatedClientPath = Path.Combine(
            repositoryRoot,
            "src",
            "Explore.Blazor.Client",
            "Clients",
            "EventApiTagClients.g.cs");
        var generatedClient = await File.ReadAllTextAsync(generatedClientPath);

        await Assert.That(generatedClient).DoesNotContain("DecentralizationEnabled", StringComparison.Ordinal);
        await Assert.That(generatedClient).DoesNotContain("decentralizationEnabled", StringComparison.Ordinal);
    }

    [Test]
    public async Task GeneratedClient_MustUse_GuestRecoveryPolicyEnum_Contract()
    {
        var repositoryRoot = ResolveRepositoryRoot();
        var generatedClientPath = Path.Combine(
            repositoryRoot,
            "src",
            "Explore.Blazor.Client",
            "Clients",
            "EventApiTagClients.g.cs");
        var generatedClient = await File.ReadAllTextAsync(generatedClientPath);

        await Assert.That(generatedClient).Contains("public GuestRecoveryPolicyEnum? GuestRecoveryPolicy", StringComparison.Ordinal)
            .Because("the generated NSwag client must preserve the OpenAPI string-enum contract for guest recovery policy.");
        await Assert.That(generatedClient).DoesNotContain("public int? GuestRecoveryPolicy", StringComparison.Ordinal)
            .Because("GuestRecoveryPolicy must not regress to integer transport in any generated DTO.");
    }

    [Test]
    public async Task SensitiveInstanceReadContracts_MustOmitSecretInputs()
    {
        string smtpJson = JsonSerializer.Serialize(new InstanceSmtpSettingsDto(), JsonSerializerOptions.Web);
        string aiJson = JsonSerializer.Serialize(new AiAssistantGovernanceSettingsDto
        {
            ApiKey = "secret-api-key",
            ApiKeyConfigured = true
        }, JsonSerializerOptions.Web);

        await Assert.That(smtpJson).DoesNotContain("username", StringComparison.OrdinalIgnoreCase);
        await Assert.That(smtpJson).DoesNotContain("password", StringComparison.OrdinalIgnoreCase);
        await Assert.That(aiJson).DoesNotContain("secret-api-key", StringComparison.Ordinal);

        var repositoryRoot = ResolveRepositoryRoot();
        await using var schemaStream = File.OpenRead(Path.Combine(repositoryRoot, "schemas", "openapi_islamu-event.json"));
        using var schemaDocument = await JsonDocument.ParseAsync(schemaStream);
        JsonElement schemas = schemaDocument.RootElement.GetProperty("components").GetProperty("schemas");
        JsonElement smtpProperties = schemas.GetProperty(nameof(InstanceSmtpSettingsDto)).GetProperty("properties");
        JsonElement aiProperties = schemas.GetProperty(nameof(AiAssistantGovernanceSettingsDto)).GetProperty("properties");
        JsonElement aiProviderWriteProperties = schemas.GetProperty(nameof(AiAssistantProviderConfigurationWriteDto)).GetProperty("properties");

        await Assert.That(smtpProperties.TryGetProperty("username", out _)).IsFalse();
        await Assert.That(smtpProperties.TryGetProperty("password", out _)).IsFalse();
        await Assert.That(smtpProperties.TryGetProperty("usernameConfigured", out _)).IsFalse();
        await Assert.That(smtpProperties.TryGetProperty("passwordConfigured", out _)).IsFalse();
        await Assert.That(aiProperties.TryGetProperty("apiKey", out _)).IsFalse();
        await Assert.That(aiProperties.TryGetProperty("apiKeyConfigured", out _)).IsTrue();
        await Assert.That(aiProviderWriteProperties.TryGetProperty("apiKey", out _)).IsTrue();
    }

    private static string ResolveRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Explore.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root from the architecture test output directory.");
    }
}
