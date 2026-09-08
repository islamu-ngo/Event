using System.ComponentModel;
using System.Reflection;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Mcp;
using Explore.Application.Features.AiAssistant.Tools;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using TUnit.Core;

namespace ApiIntegrationTests.Features;

public sealed class McpSdkContractTests
{
    [Test]
    public async Task McpSurfaceTypesUseOfficialSdkTypeAttributes()
    {
        await Assert.That(typeof(AiToolRegistryMcpTools).GetCustomAttribute<McpServerToolTypeAttribute>()).IsNotNull();
        await Assert.That(typeof(AiAssistantMcpTools).GetCustomAttribute<McpServerToolTypeAttribute>()).IsNotNull();
        await Assert.That(typeof(EventManagementMcpTools).GetCustomAttribute<McpServerToolTypeAttribute>()).IsNotNull();
        await Assert.That(typeof(AiAssistantMcpResources).GetCustomAttribute<McpServerResourceTypeAttribute>()).IsNotNull();
        await Assert.That(typeof(EventManagementMcpResources).GetCustomAttribute<McpServerResourceTypeAttribute>()).IsNotNull();
        await Assert.That(typeof(AiAssistantMcpPrompts).GetCustomAttribute<McpServerPromptTypeAttribute>()).IsNotNull();
    }

    [Test]
    public async Task McpToolsPromptsAndResourcesHaveDescriptionsForLlmDiscovery()
    {
        var methods = new[]
        {
            RequiredMethod(typeof(AiToolRegistryMcpTools), nameof(AiToolRegistryMcpTools.ListAiToolContracts)),
            RequiredMethod(typeof(AiAssistantMcpTools), nameof(AiAssistantMcpTools.ProposeAiToolActionAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.SearchPublicEventsAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetPublicEventAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetPublicEventProgramSummaryAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.ListPublicEventSessionsAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.ListMyEventsAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetEventCreationContextAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetEventPublishReadinessAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetEventProgramManagementContextAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetEventCustomPropertiesContextAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetEventRegistrationsContextAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetEventTeamContextAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetEventTemplateCatalogContextAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetEventTemplateSyncContextAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetEventSessionTemplateSyncContextAsync)),
            RequiredMethod(typeof(AiAssistantMcpResources), nameof(AiAssistantMcpResources.ListConversationsAsync)),
            RequiredMethod(typeof(AiAssistantMcpResources), nameof(AiAssistantMcpResources.GetConversationAsync)),
            RequiredMethod(typeof(EventManagementMcpResources), nameof(EventManagementMcpResources.GetEventManagementContextAsync)),
            RequiredMethod(typeof(AiAssistantMcpPrompts), nameof(AiAssistantMcpPrompts.CreateEventDraftWithConfirmation)),
            RequiredMethod(typeof(AiAssistantMcpPrompts), nameof(AiAssistantMcpPrompts.ManageEventWithConfirmation))
        };

        foreach (var method in methods)
        {
            await Assert.That(string.IsNullOrWhiteSpace(
                    method.GetCustomAttribute<DescriptionAttribute>()?.Description))
                .IsFalse()
                .Because(method.Name);
        }
    }

    [Test]
    public async Task McpSchemaParametersHaveDescriptionsExceptInjectedCancellation()
    {
        var exposedMethods = new[]
        {
            RequiredMethod(typeof(AiAssistantMcpTools), nameof(AiAssistantMcpTools.ProposeAiToolActionAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.SearchPublicEventsAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetPublicEventAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetPublicEventProgramSummaryAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.ListPublicEventSessionsAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.ListMyEventsAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetEventCreationContextAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetEventPublishReadinessAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetEventProgramManagementContextAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetEventCustomPropertiesContextAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetEventRegistrationsContextAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetEventTeamContextAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetEventTemplateCatalogContextAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetEventTemplateSyncContextAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetEventSessionTemplateSyncContextAsync)),
            RequiredMethod(typeof(AiAssistantMcpResources), nameof(AiAssistantMcpResources.GetConversationAsync)),
            RequiredMethod(typeof(EventManagementMcpResources), nameof(EventManagementMcpResources.GetEventManagementContextAsync))
        };

        foreach (var parameter in exposedMethods.SelectMany(method => method.GetParameters()))
        {
            if (parameter.ParameterType == typeof(CancellationToken))
            {
                continue;
            }

            await Assert.That(string.IsNullOrWhiteSpace(
                    parameter.GetCustomAttribute<DescriptionAttribute>()?.Description))
                .IsFalse()
                .Because($"{parameter.Member.Name}.{parameter.Name}");
        }
    }

    [Test]
    public async Task McpCallableMethodsDeclareExplicitAuthorizationPosture()
    {
        var anonymousSafeMethods = new HashSet<MethodInfo>
        {
            RequiredMethod(typeof(AiToolRegistryMcpTools), nameof(AiToolRegistryMcpTools.ListAiToolContracts)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.SearchPublicEventsAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetPublicEventAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetPublicEventProgramSummaryAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.ListPublicEventSessionsAsync))
        };

        foreach (var method in McpCallableMethods())
        {
            if (anonymousSafeMethods.Contains(method))
            {
                await Assert.That(method.GetCustomAttribute<AllowAnonymousAttribute>()).IsNotNull().Because(method.Name);
                await Assert.That(method.GetCustomAttribute<AuthorizeAttribute>()).IsNull().Because(method.Name);
                continue;
            }

            await Assert.That(method.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull().Because(method.Name);
            await Assert.That(method.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull().Because(method.Name);
        }
    }

    [Test]
    public async Task McpAuthorizedMethodsUseApiKeyScopeAwarePolicies()
    {
        await Assert.That(RequiredMethod(typeof(AiAssistantMcpTools), nameof(AiAssistantMcpTools.ProposeAiToolActionAsync))
            .GetCustomAttribute<AuthorizeAttribute>()?.Policy).IsEqualTo(McpAuthorizationPolicies.Propose);

        await Assert.That(RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.ListMyEventsAsync))
            .GetCustomAttribute<AuthorizeAttribute>()?.Policy).IsEqualTo(McpAuthorizationPolicies.EventManagementRead);

        await Assert.That(RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetEventCreationContextAsync))
            .GetCustomAttribute<AuthorizeAttribute>()?.Policy).IsEqualTo(McpAuthorizationPolicies.EventManagementRead);

        await Assert.That(RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetEventPublishReadinessAsync))
            .GetCustomAttribute<AuthorizeAttribute>()?.Policy).IsEqualTo(McpAuthorizationPolicies.EventManagementRead);

        await Assert.That(RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetEventProgramManagementContextAsync))
            .GetCustomAttribute<AuthorizeAttribute>()?.Policy).IsEqualTo(McpAuthorizationPolicies.EventManagementRead);

        await Assert.That(RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetEventCustomPropertiesContextAsync))
            .GetCustomAttribute<AuthorizeAttribute>()?.Policy).IsEqualTo(McpAuthorizationPolicies.EventManagementRead);

        await Assert.That(RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetEventRegistrationsContextAsync))
            .GetCustomAttribute<AuthorizeAttribute>()?.Policy).IsEqualTo(McpAuthorizationPolicies.EventManagementRead);

        await Assert.That(RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetEventTeamContextAsync))
            .GetCustomAttribute<AuthorizeAttribute>()?.Policy).IsEqualTo(McpAuthorizationPolicies.EventManagementRead);

        await Assert.That(RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetEventTemplateCatalogContextAsync))
            .GetCustomAttribute<AuthorizeAttribute>()?.Policy).IsEqualTo(McpAuthorizationPolicies.EventManagementRead);

        await Assert.That(RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetEventTemplateSyncContextAsync))
            .GetCustomAttribute<AuthorizeAttribute>()?.Policy).IsEqualTo(McpAuthorizationPolicies.EventManagementRead);

        await Assert.That(RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetEventSessionTemplateSyncContextAsync))
            .GetCustomAttribute<AuthorizeAttribute>()?.Policy).IsEqualTo(McpAuthorizationPolicies.EventManagementRead);

        await Assert.That(RequiredMethod(typeof(AiAssistantMcpResources), nameof(AiAssistantMcpResources.ListConversationsAsync))
            .GetCustomAttribute<AuthorizeAttribute>()?.Policy).IsEqualTo(McpAuthorizationPolicies.Read);

        await Assert.That(RequiredMethod(typeof(AiAssistantMcpResources), nameof(AiAssistantMcpResources.GetConversationAsync))
            .GetCustomAttribute<AuthorizeAttribute>()?.Policy).IsEqualTo(McpAuthorizationPolicies.Read);

        await Assert.That(RequiredMethod(typeof(EventManagementMcpResources), nameof(EventManagementMcpResources.GetEventManagementContextAsync))
            .GetCustomAttribute<AuthorizeAttribute>()?.Policy).IsEqualTo(McpAuthorizationPolicies.EventManagementRead);

        await Assert.That(RequiredMethod(typeof(AiAssistantMcpPrompts), nameof(AiAssistantMcpPrompts.CreateEventDraftWithConfirmation))
            .GetCustomAttribute<AuthorizeAttribute>()?.Policy).IsEqualTo(McpAuthorizationPolicies.Propose);

        await Assert.That(RequiredMethod(typeof(AiAssistantMcpPrompts), nameof(AiAssistantMcpPrompts.ManageEventWithConfirmation))
            .GetCustomAttribute<AuthorizeAttribute>()?.Policy).IsEqualTo(McpAuthorizationPolicies.Propose);

        foreach (var definition in AiToolContractRegistry.CreateDefault().Definitions.Where(definition => definition.ExposeToMcp))
        {
            var projectedTool = new AiMcpProjectedProposalTool(definition);
            await Assert.That(projectedTool.Metadata.OfType<AuthorizeAttribute>().Single().Policy).IsEqualTo(McpAuthorizationPolicies.Propose);
        }
    }

    [Test]
    public async Task McpSurfaceTypesDoNotPermitAnonymousAccess()
    {
        var surfaceTypes = new[]
        {
            typeof(AiToolRegistryMcpTools),
            typeof(AiAssistantMcpTools),
            typeof(EventManagementMcpTools),
            typeof(AiAssistantMcpResources),
            typeof(EventManagementMcpResources),
            typeof(AiAssistantMcpPrompts)
        };

        foreach (var type in surfaceTypes)
        {
            await Assert.That(type.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull().Because(type.FullName);
        }
    }

    [Test]
    public async Task ApiHostBindsMcpHttpTransportToStatelessStreamableHttpAtStartup()
    {
        await using var factory = CreateMcpEnabledFactory();
        using var client = factory.CreateClient();

        var transportOptions = factory.Services
            .GetRequiredService<IOptions<HttpServerTransportOptions>>()
            .Value;

        await Assert.That(transportOptions.Stateless).IsTrue();
#pragma warning disable MCP9004
        await Assert.That(transportOptions.EnableLegacySse).IsFalse();
#pragma warning restore MCP9004
    }

    [Test]
    public async Task ApiHostUsesStreamableHttpOnlyForProductMcpTransport()
    {
        var services = ReadRepoFile("src/Explore.API/Hosting/ApiHostServiceCollectionExtensions.cs");
        var application = ReadRepoFile("src/Explore.API/Hosting/ApiHostApplicationExtensions.cs");
        var program = ReadRepoFile("src/Explore.API/Program.cs");

        await Assert.That(services).Contains(".WithHttpTransport(options =>");
        await Assert.That(services).Contains("options.Stateless = mcpAdapterSettings.Stateless");
        await Assert.That(services).Contains("builder.Services.PostConfigure<McpAdapterSettings>");
        await Assert.That(services).Contains("settings.EndpointPath = endpointPath.StartsWith('/')");
        await Assert.That(application).Contains("app.MapMcp(mcpAdapterSettings.EndpointPath)");
        await Assert.That(application).Contains(".AllowAnonymous()");
        await Assert.That(services).DoesNotContain(".WithStdioServerTransport");
        await Assert.That(application).DoesNotContain(".WithStdioServerTransport");
        await Assert.That(services).DoesNotContain("options.EnableLegacySse");
        await Assert.That(application).DoesNotContain("options.EnableLegacySse");
        await Assert.That(program).Contains("AddApiHostServices");
        await Assert.That(program).DoesNotContain(".WithHttpTransport(");
        await Assert.That(program).DoesNotContain("app.MapMcp(");
    }

    [Test]
    public async Task ApiHostRegistersMcpSurfacesExplicitlyForAotReviewability()
    {
        var services = ReadRepoFile("src/Explore.API/Hosting/ApiHostServiceCollectionExtensions.cs");
        var program = ReadRepoFile("src/Explore.API/Program.cs");

        await Assert.That(services).Contains(".WithTools<AiToolRegistryMcpTools>()");
        await Assert.That(services).Contains(".WithTools<AiAssistantMcpTools>()");
        await Assert.That(services).Contains(".WithTools<EventManagementMcpTools>()");
        await Assert.That(services).Contains(".WithResources<AiAssistantMcpResources>()");
        await Assert.That(services).Contains(".WithResources<EventManagementMcpResources>()");
        await Assert.That(services).Contains(".WithPrompts<AiAssistantMcpPrompts>()");
        await Assert.That(services).Contains("AiMcpProjectedToolOptionsSetup");
        await Assert.That(services).DoesNotContain(".WithToolsFromAssembly");
        await Assert.That(services).DoesNotContain(".WithResourcesFromAssembly");
        await Assert.That(services).DoesNotContain(".WithPromptsFromAssembly");
        await Assert.That(program).Contains("AddApiHostServices");
        await Assert.That(program).DoesNotContain(".WithTools<");
        await Assert.That(program).DoesNotContain(".WithResources<");
        await Assert.That(program).DoesNotContain(".WithPrompts<");
    }

    [Test]
    public async Task ServiceDefaultsExportsBoundedMcpTelemetrySourceAndMeter()
    {
        var serviceDefaults = ReadRepoFile("src/Explore.ServiceDefaults/Extensions.cs");

        await Assert.That(serviceDefaults).Contains(".AddMeter(\"Explore.Mcp\")");
        await Assert.That(serviceDefaults).Contains(".AddSource(\"Explore.Mcp\")");
    }

    private static MethodInfo[] McpCallableMethods()
        =>
        [
            RequiredMethod(typeof(AiToolRegistryMcpTools), nameof(AiToolRegistryMcpTools.ListAiToolContracts)),
            RequiredMethod(typeof(AiAssistantMcpTools), nameof(AiAssistantMcpTools.ProposeAiToolActionAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.SearchPublicEventsAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetPublicEventAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetPublicEventProgramSummaryAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.ListPublicEventSessionsAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.ListMyEventsAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetEventCreationContextAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetEventPublishReadinessAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetEventProgramManagementContextAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetEventCustomPropertiesContextAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetEventRegistrationsContextAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetEventTeamContextAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetEventTemplateCatalogContextAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetEventTemplateSyncContextAsync)),
            RequiredMethod(typeof(EventManagementMcpTools), nameof(EventManagementMcpTools.GetEventSessionTemplateSyncContextAsync)),
            RequiredMethod(typeof(AiAssistantMcpResources), nameof(AiAssistantMcpResources.ListConversationsAsync)),
            RequiredMethod(typeof(AiAssistantMcpResources), nameof(AiAssistantMcpResources.GetConversationAsync)),
            RequiredMethod(typeof(EventManagementMcpResources), nameof(EventManagementMcpResources.GetEventManagementContextAsync)),
            RequiredMethod(typeof(AiAssistantMcpPrompts), nameof(AiAssistantMcpPrompts.CreateEventDraftWithConfirmation)),
            RequiredMethod(typeof(AiAssistantMcpPrompts), nameof(AiAssistantMcpPrompts.ManageEventWithConfirmation))
        ];

    private static MethodInfo RequiredMethod(Type type, string methodName)
        => type.GetMethod(methodName)
            ?? throw new InvalidOperationException($"{type.FullName}.{methodName} was not found.");

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

    private static string ReadRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidatePath = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidatePath))
            {
                return File.ReadAllText(candidatePath);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file '{relativePath}'.", relativePath);
    }
}
