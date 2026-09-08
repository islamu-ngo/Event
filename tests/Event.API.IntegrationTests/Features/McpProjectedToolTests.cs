using System.Text.Json;
using Explore.API.Mcp;
using Explore.Application.Features.AiAssistant.Requests.Commands;
using Explore.Application.Features.AiAssistant.Tools;
using Explore.Application.Responses;
using Explore.Domain.Ai;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NSubstitute;
using TUnit.Core;

namespace ApiIntegrationTests.Features;

public sealed class McpProjectedToolTests
{
    private static readonly string[] ProjectedProposalToolNames = AiMcpProjectedToolFactory
        .CreateTools(AiToolContractRegistry.CreateDefault())
        .Select(tool => tool.ProtocolTool.Name)
        .Order(StringComparer.Ordinal)
        .ToArray();

    [Test]
    public async Task CreateTools_ProjectsMcpExposedRegistryDefinitions()
    {
        var tools = AiMcpProjectedToolFactory.CreateTools(AiToolContractRegistry.CreateDefault());

        await Assert.That(tools.Select(tool => tool.ProtocolTool.Name)
                .Order(StringComparer.Ordinal))
            .IsEquivalentTo(ProjectedProposalToolNames, TUnit.Assertions.Enums.CollectionOrdering.Matching);

        var tool = tools.Single(candidate => candidate.ProtocolTool.Name == "propose_create_event_draft");
        await Assert.That(tool.ProtocolTool.Name).IsEqualTo("propose_create_event_draft");
        await Assert.That(tool.ProtocolTool.Title).IsEqualTo("Create event draft");
        await Assert.That(tool.ProtocolTool.Description).Contains("proposed action");
        await Assert.That(tool.ProtocolTool.Annotations).IsNotNull();
        await Assert.That(tool.ProtocolTool.Annotations!.ReadOnlyHint).IsFalse();
        await Assert.That(tool.ProtocolTool.Annotations.DestructiveHint).IsFalse();
        await Assert.That(tool.ProtocolTool.Annotations.IdempotentHint).IsFalse();
        await Assert.That(tool.ProtocolTool.Annotations.OpenWorldHint).IsFalse();
        await Assert.That(tool.ProtocolTool.Meta!["islamuRiskClass"]!.GetValue<string>()).IsEqualTo("Medium");
        await Assert.That(tool.ProtocolTool.Meta!["islamuApprovalMode"]!.GetValue<string>()).IsEqualTo("HumanConfirmationRequired");
        await Assert.That(tool.ProtocolTool.Meta!["islamuDestructive"]!.GetValue<bool>()).IsFalse();
        await Assert.That(tool.Metadata.OfType<AuthorizeAttribute>()).HasSingleItem();
    }

    [Test]
    public async Task CreateTools_ExcludesDefinitionsNotExposedToMcp()
    {
        var hiddenDefinition = CreateEventDraftAiToolDefinition.Create() with { ExposeToMcp = false };
        var registry = new AiToolContractRegistry([hiddenDefinition]);

        var tools = AiMcpProjectedToolFactory.CreateTools(registry);

        await Assert.That(tools).IsEmpty();
    }

    [Test]
    public async Task ProjectedToolInputSchema_PreservesRegistryPayloadFieldsAndAddsProposalEnvelope()
    {
        var definition = CreateEventDraftAiToolDefinition.Create();
        var tool = new AiMcpProjectedProposalTool(definition);

        var schema = tool.ProtocolTool.InputSchema;
        await Assert.That(schema.GetProperty("type").GetString()).IsEqualTo("object");
        await Assert.That(schema.GetProperty("additionalProperties").GetBoolean()).IsFalse();

        var properties = schema.GetProperty("properties");
        await Assert.That(properties.TryGetProperty("conversationId", out _)).IsTrue();
        await Assert.That(properties.TryGetProperty("summary", out _)).IsTrue();
        await Assert.That(properties.TryGetProperty("title", out _)).IsTrue();
        await Assert.That(properties.TryGetProperty("tenantId", out _)).IsFalse();

        var projectedPayloadFields = properties.EnumerateObject()
            .Select(property => property.Name)
            .Where(name => name is not "conversationId" and not "summary")
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        await Assert.That(projectedPayloadFields).IsEquivalentTo(
            definition.AllowedPayloadFields.Order(StringComparer.OrdinalIgnoreCase),
            TUnit.Assertions.Enums.CollectionOrdering.Matching);

        var required = schema.GetProperty("required")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToArray();
        await Assert.That(required).Contains("conversationId");
        await Assert.That(required).Contains("title");
    }

    [Test]
    public async Task ProjectedToolInputSchema_ForUpdateDraft_RequiresEventIdAndConcurrencyStamp()
    {
        var definition = UpdateEventDraftAiToolDefinition.Create();
        var tool = new AiMcpProjectedProposalTool(definition);

        var schema = tool.ProtocolTool.InputSchema;
        var properties = schema.GetProperty("properties");
        await Assert.That(properties.TryGetProperty("eventId", out _)).IsTrue();
        await Assert.That(properties.TryGetProperty("expectedConcurrencyStamp", out _)).IsTrue();
        await Assert.That(properties.TryGetProperty("tenantId", out _)).IsFalse();
        await Assert.That(properties.TryGetProperty("actorId", out _)).IsFalse();
        await Assert.That(properties.TryGetProperty("eventStatusId", out _)).IsFalse();

        var required = schema.GetProperty("required")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToArray();
        await Assert.That(required).Contains("conversationId");
        await Assert.That(required).Contains("eventId");
        await Assert.That(required).Contains("expectedConcurrencyStamp");
        await Assert.That(required).Contains("title");
    }

    [Test]
    public async Task ProjectedToolInputSchema_ForPublishEvent_RequiresEventIdConcurrencyStampAndReadiness()
    {
        var definition = PublishEventAiToolDefinition.Create();
        var tool = new AiMcpProjectedProposalTool(definition);

        var schema = tool.ProtocolTool.InputSchema;
        var properties = schema.GetProperty("properties");
        await Assert.That(properties.TryGetProperty("eventId", out _)).IsTrue();
        await Assert.That(properties.TryGetProperty("expectedConcurrencyStamp", out _)).IsTrue();
        await Assert.That(properties.TryGetProperty("readinessIsReady", out _)).IsTrue();
        await Assert.That(properties.TryGetProperty("readinessErrorCount", out _)).IsTrue();
        await Assert.That(properties.TryGetProperty("tenantId", out _)).IsFalse();
        await Assert.That(properties.TryGetProperty("eventStatusId", out _)).IsFalse();
        await Assert.That(properties.TryGetProperty("publishedAt", out _)).IsFalse();

        var required = schema.GetProperty("required")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToArray();
        await Assert.That(required).Contains("conversationId");
        await Assert.That(required).Contains("eventId");
        await Assert.That(required).Contains("expectedConcurrencyStamp");
        await Assert.That(required).Contains("readinessIsReady");
        await Assert.That(required).Contains("readinessErrorCount");
    }

    [Test]
    public async Task ProjectedToolInputSchema_ForDeleteEvent_RequiresDestructiveConfirmation()
    {
        var definition = DeleteEventAiToolDefinition.Create();
        var tool = new AiMcpProjectedProposalTool(definition);

        await Assert.That(tool.ProtocolTool.Annotations!.DestructiveHint).IsTrue();
        await Assert.That(tool.ProtocolTool.Meta!["islamuDestructive"]!.GetValue<bool>()).IsTrue();

        var schema = tool.ProtocolTool.InputSchema;
        var properties = schema.GetProperty("properties");
        await Assert.That(properties.TryGetProperty("eventId", out _)).IsTrue();
        await Assert.That(properties.TryGetProperty("expectedConcurrencyStamp", out _)).IsTrue();
        await Assert.That(properties.TryGetProperty("managementContextHasDelete", out _)).IsTrue();
        await Assert.That(properties.TryGetProperty("destructiveSummary", out _)).IsTrue();
        await Assert.That(properties.TryGetProperty("confirmationPhrase", out _)).IsTrue();
        await Assert.That(properties.TryGetProperty("tenantId", out _)).IsFalse();

        var required = schema.GetProperty("required")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToArray();
        await Assert.That(required).Contains("conversationId");
        await Assert.That(required).Contains("eventId");
        await Assert.That(required).Contains("expectedConcurrencyStamp");
        await Assert.That(required).Contains("managementContextHasDelete");
        await Assert.That(required).Contains("destructiveSummary");
        await Assert.That(required).Contains("confirmationPhrase");
        await Assert.That(required).Contains("acknowledgedConsequences");
    }

    [Test]
    public async Task ProjectedToolInputSchema_ForAspectTools_RequiresModuleAndPermissionContext()
    {
        var upsertIslamic = new AiMcpProjectedProposalTool(UpsertEventIslamicAspectAiToolDefinition.Create());
        var deleteTech = new AiMcpProjectedProposalTool(DeleteEventTechAspectAiToolDefinition.Create());

        var upsertProperties = upsertIslamic.ProtocolTool.InputSchema.GetProperty("properties");
        await Assert.That(upsertProperties.TryGetProperty("aspectKind", out _)).IsTrue();
        await Assert.That(upsertProperties.TryGetProperty("managementContextHasEdit", out _)).IsTrue();
        await Assert.That(upsertProperties.TryGetProperty("genderMode", out _)).IsTrue();
        await Assert.That(upsertProperties.TryGetProperty("tenantId", out _)).IsFalse();
        await Assert.That(upsertIslamic.ProtocolTool.Annotations!.DestructiveHint).IsFalse();

        var deleteProperties = deleteTech.ProtocolTool.InputSchema.GetProperty("properties");
        await Assert.That(deleteProperties.TryGetProperty("aspectKind", out _)).IsTrue();
        await Assert.That(deleteProperties.TryGetProperty("managementContextHasEdit", out _)).IsTrue();
        await Assert.That(deleteProperties.TryGetProperty("confirmationPhrase", out _)).IsTrue();
        await Assert.That(deleteProperties.TryGetProperty("concurrencyStamp", out _)).IsFalse();
        await Assert.That(deleteTech.ProtocolTool.Annotations!.DestructiveHint).IsTrue();
    }

    [Test]
    public async Task ProjectedToolInputSchema_ForPhaseFiveSubResourceTools_RequiresContextAndServerOwnedExclusions()
    {
        var createSession = new AiMcpProjectedProposalTool(FindSubResourceDefinition(AiProposedActionKind.CreateEventSession));
        var applyTemplateSync = new AiMcpProjectedProposalTool(FindSubResourceDefinition(AiProposedActionKind.ApplyEventTemplateSync));

        var createSessionProperties = createSession.ProtocolTool.InputSchema.GetProperty("properties");
        await Assert.That(createSessionProperties.TryGetProperty("conversationId", out _)).IsTrue();
        await Assert.That(createSessionProperties.TryGetProperty("eventId", out _)).IsTrue();
        await Assert.That(createSessionProperties.TryGetProperty("expectedConcurrencyStamp", out _)).IsTrue();
        await Assert.That(createSessionProperties.TryGetProperty("managementContextHasAddSession", out _)).IsTrue();
        await Assert.That(createSessionProperties.TryGetProperty("title", out _)).IsTrue();
        await Assert.That(createSessionProperties.TryGetProperty("tenantId", out _)).IsFalse();
        await Assert.That(createSessionProperties.TryGetProperty("userId", out _)).IsFalse();

        var createSessionRequired = createSession.ProtocolTool.InputSchema.GetProperty("required")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToArray();
        await Assert.That(new[] { "conversationId",
        "eventId",
        "expectedConcurrencyStamp",
        "managementContextHasAddSession",
        "title",
        "startTime",
        "endTime" }.All(createSessionRequired.Contains)).IsTrue();

        var templateSyncProperties = applyTemplateSync.ProtocolTool.InputSchema.GetProperty("properties");
        await Assert.That(templateSyncProperties.TryGetProperty("plan", out var planProperty)).IsTrue();
        await Assert.That(planProperty.GetProperty("additionalProperties").GetBoolean()).IsFalse();
        await Assert.That(planProperty.GetProperty("properties").TryGetProperty("targetTemplateVersion", out _)).IsTrue();
        await Assert.That(planProperty.GetProperty("properties").TryGetProperty("modifiedDefinitionKeys", out _)).IsTrue();
        await Assert.That(templateSyncProperties.TryGetProperty("sourceTemplateVersion", out _)).IsFalse();
        await Assert.That(templateSyncProperties.TryGetProperty("tenantId", out _)).IsFalse();
    }

    [Test]
    public async Task ProjectedToolInputSchema_ForModerationTools_RequiresHalContextAndHeavyAcknowledgement()
    {
        var lightModeration = new AiMcpProjectedProposalTool(FindModerationDefinition(AiProposedActionKind.LightModerateEvent));
        var heavyModeration = new AiMcpProjectedProposalTool(FindModerationDefinition(AiProposedActionKind.HeavyModerateEvent));

        await Assert.That(lightModeration.ProtocolTool.Name).IsEqualTo("propose_light_moderate_event");
        await Assert.That(lightModeration.ProtocolTool.Meta!["islamuRiskClass"]!.GetValue<string>()).IsEqualTo("High");
        await Assert.That(lightModeration.ProtocolTool.Annotations!.DestructiveHint).IsFalse();

        var lightProperties = lightModeration.ProtocolTool.InputSchema.GetProperty("properties");
        await Assert.That(lightProperties.TryGetProperty("conversationId", out _)).IsTrue();
        await Assert.That(lightProperties.TryGetProperty("eventId", out _)).IsTrue();
        await Assert.That(lightProperties.TryGetProperty("expectedConcurrencyStamp", out _)).IsTrue();
        await Assert.That(lightProperties.TryGetProperty("managementContextHasModerateLight", out _)).IsTrue();
        await Assert.That(lightProperties.TryGetProperty("reasonCode", out _)).IsTrue();
        await Assert.That(lightProperties.TryGetProperty("actorId", out _)).IsFalse();
        await Assert.That(lightProperties.TryGetProperty("tenantId", out _)).IsFalse();

        await Assert.That(heavyModeration.ProtocolTool.Name).IsEqualTo("propose_heavy_moderate_event");
        await Assert.That(heavyModeration.ProtocolTool.Meta!["islamuRiskClass"]!.GetValue<string>()).IsEqualTo("Critical");
        await Assert.That(heavyModeration.ProtocolTool.Annotations!.DestructiveHint).IsTrue();
        await Assert.That(heavyModeration.ProtocolTool.Meta!["islamuDestructive"]!.GetValue<bool>()).IsTrue();

        var heavyProperties = heavyModeration.ProtocolTool.InputSchema.GetProperty("properties");
        await Assert.That(heavyProperties.TryGetProperty("managementContextHasModerateHeavy", out _)).IsTrue();
        await Assert.That(heavyProperties.TryGetProperty("destructiveSummary", out _)).IsTrue();
        await Assert.That(heavyProperties.TryGetProperty("confirmationPhrase", out var confirmationPhrase)).IsTrue();
        await Assert.That(heavyProperties.TryGetProperty("acknowledgedConsequences", out _)).IsTrue();
        await Assert.That(confirmationPhrase.GetProperty("enum").EnumerateArray().Single().GetString()).IsEqualTo("HEAVY_MODERATE_EVENT");
    }

    [Test]
    public async Task MapArgumentsToCommand_CreatesGenericProposalCommandWithoutRuntimeEnvelopeFields()
    {
        var conversationId = Guid.CreateVersion7();
        var tool = new AiMcpProjectedProposalTool(CreateEventDraftAiToolDefinition.Create());
        var arguments = CreateArguments(
            $$"""
              {
                "conversationId": "{{conversationId}}",
                "summary": "Draft an event",
                "title": "Projected MCP draft",
                "eventTypeId": 7
              }
              """);

        var command = tool.MapArgumentsToCommand(arguments);

        await Assert.That(command.ConversationId).IsEqualTo(conversationId);
        await Assert.That(command.ToolName).IsEqualTo("CreateEventDraft");
        await Assert.That(command.Summary).IsEqualTo("Draft an event");

        using var payload = JsonDocument.Parse(command.PayloadJson);
        await Assert.That(payload.RootElement.TryGetProperty("conversationId", out _)).IsFalse();
        await Assert.That(payload.RootElement.TryGetProperty("summary", out _)).IsFalse();
        await Assert.That(payload.RootElement.GetProperty("title").GetString()).IsEqualTo("Projected MCP draft");
        await Assert.That(payload.RootElement.GetProperty("eventTypeId").GetInt32()).IsEqualTo(7);
    }

    [Test]
    public async Task MapArgumentsToCommand_RejectsUnexpectedOrHiddenFieldsBeforeMediatR()
    {
        var conversationId = Guid.CreateVersion7();
        var tool = new AiMcpProjectedProposalTool(CreateEventDraftAiToolDefinition.Create());
        var arguments = CreateArguments(
            $$"""
              {
                "conversationId": "{{conversationId}}",
                "title": "Projected MCP draft",
                "tenantId": "{{Guid.CreateVersion7()}}"
              }
              """);

        Action act = () => _ = tool.MapArgumentsToCommand(arguments);

        var exception = Assert.Throws<ArgumentException>(act);
        await Assert.That(exception.Message).Contains("tenantId");
    }

    [Test]
    public async Task MapArgumentsToCommand_ForUpdateDraft_CreatesProposalCommandWithDraftPayload()
    {
        var conversationId = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();
        var concurrencyStamp = Guid.CreateVersion7();
        var tool = new AiMcpProjectedProposalTool(UpdateEventDraftAiToolDefinition.Create());
        var arguments = CreateArguments(
            $$"""
              {
                "conversationId": "{{conversationId}}",
                "summary": "Update an event draft",
                "eventId": "{{eventId}}",
                "expectedConcurrencyStamp": "{{concurrencyStamp}}",
                "title": "Projected MCP update"
              }
              """);

        var command = tool.MapArgumentsToCommand(arguments);

        await Assert.That(command.ConversationId).IsEqualTo(conversationId);
        await Assert.That(command.ToolName).IsEqualTo("UpdateEventDraft");
        await Assert.That(command.Summary).IsEqualTo("Update an event draft");

        using var payload = JsonDocument.Parse(command.PayloadJson);
        await Assert.That(payload.RootElement.TryGetProperty("conversationId", out _)).IsFalse();
        await Assert.That(payload.RootElement.TryGetProperty("summary", out _)).IsFalse();
        await Assert.That(payload.RootElement.GetProperty("eventId").GetGuid()).IsEqualTo(eventId);
        await Assert.That(payload.RootElement.GetProperty("expectedConcurrencyStamp").GetGuid()).IsEqualTo(concurrencyStamp);
        await Assert.That(payload.RootElement.GetProperty("title").GetString()).IsEqualTo("Projected MCP update");
    }

    [Test]
    public async Task MapArgumentsToCommand_ForPublishEvent_CreatesProposalCommandWithPublishPayload()
    {
        var conversationId = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();
        var concurrencyStamp = Guid.CreateVersion7();
        var tool = new AiMcpProjectedProposalTool(PublishEventAiToolDefinition.Create());
        var arguments = CreateArguments(
            $$"""
              {
                "conversationId": "{{conversationId}}",
                "summary": "Publish an event",
                "eventId": "{{eventId}}",
                "expectedConcurrencyStamp": "{{concurrencyStamp}}",
                "readinessIsReady": true,
                "readinessErrorCount": 0
              }
              """);

        var command = tool.MapArgumentsToCommand(arguments);

        await Assert.That(command.ConversationId).IsEqualTo(conversationId);
        await Assert.That(command.ToolName).IsEqualTo("PublishEvent");
        await Assert.That(command.Summary).IsEqualTo("Publish an event");

        using var payload = JsonDocument.Parse(command.PayloadJson);
        await Assert.That(payload.RootElement.TryGetProperty("conversationId", out _)).IsFalse();
        await Assert.That(payload.RootElement.TryGetProperty("summary", out _)).IsFalse();
        await Assert.That(payload.RootElement.GetProperty("eventId").GetGuid()).IsEqualTo(eventId);
        await Assert.That(payload.RootElement.GetProperty("expectedConcurrencyStamp").GetGuid()).IsEqualTo(concurrencyStamp);
        await Assert.That(payload.RootElement.GetProperty("readinessIsReady").GetBoolean()).IsTrue();
        await Assert.That(payload.RootElement.GetProperty("readinessErrorCount").GetInt32()).IsEqualTo(0);
    }

    [Test]
    public async Task MapArgumentsToCommand_ForDeleteEvent_CreatesProposalCommandWithDestructivePayload()
    {
        var conversationId = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();
        var concurrencyStamp = Guid.CreateVersion7();
        var tool = new AiMcpProjectedProposalTool(DeleteEventAiToolDefinition.Create());
        var arguments = CreateArguments(
            $$"""
              {
                "conversationId": "{{conversationId}}",
                "summary": "Delete an event",
                "eventId": "{{eventId}}",
                "expectedConcurrencyStamp": "{{concurrencyStamp}}",
                "managementContextHasDelete": true,
                "destructiveSummary": "Delete duplicate draft",
                "confirmationPhrase": "DELETE_EVENT",
                "acknowledgedConsequences": true
              }
              """);

        var command = tool.MapArgumentsToCommand(arguments);

        await Assert.That(command.ConversationId).IsEqualTo(conversationId);
        await Assert.That(command.ToolName).IsEqualTo("DeleteEvent");
        await Assert.That(command.Summary).IsEqualTo("Delete an event");

        using var payload = JsonDocument.Parse(command.PayloadJson);
        await Assert.That(payload.RootElement.GetProperty("eventId").GetGuid()).IsEqualTo(eventId);
        await Assert.That(payload.RootElement.GetProperty("expectedConcurrencyStamp").GetGuid()).IsEqualTo(concurrencyStamp);
        await Assert.That(payload.RootElement.GetProperty("managementContextHasDelete").GetBoolean()).IsTrue();
        await Assert.That(payload.RootElement.GetProperty("confirmationPhrase").GetString()).IsEqualTo("DELETE_EVENT");
    }

    [Test]
    public async Task MapArgumentsToCommand_ForAspectTool_CreatesProposalCommandWithAspectPayload()
    {
        var conversationId = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();
        var concurrencyStamp = Guid.CreateVersion7();
        var tool = new AiMcpProjectedProposalTool(UpsertEventTechAspectAiToolDefinition.Create());
        var arguments = CreateArguments(
            $$"""
              {
                "conversationId": "{{conversationId}}",
                "summary": "Update Tech aspect",
                "eventId": "{{eventId}}",
                "expectedConcurrencyStamp": "{{concurrencyStamp}}",
                "aspectKind": "tech",
                "managementContextHasEdit": true,
                "skillLevel": 0,
                "requiresLaptop": true
              }
              """);

        var command = tool.MapArgumentsToCommand(arguments);

        await Assert.That(command.ToolName).IsEqualTo("UpsertEventTechAspect");
        await Assert.That(command.ConversationId).IsEqualTo(conversationId);
        await Assert.That(command.Summary).IsEqualTo("Update Tech aspect");
        using var payload = JsonDocument.Parse(command.PayloadJson);
        await Assert.That(payload.RootElement.GetProperty("eventId").GetGuid()).IsEqualTo(eventId);
        await Assert.That(payload.RootElement.GetProperty("expectedConcurrencyStamp").GetGuid()).IsEqualTo(concurrencyStamp);
        await Assert.That(payload.RootElement.GetProperty("aspectKind").GetString()).IsEqualTo("tech");
        await Assert.That(payload.RootElement.GetProperty("managementContextHasEdit").GetBoolean()).IsTrue();
        await Assert.That(payload.RootElement.GetProperty("skillLevel").GetInt32()).IsEqualTo(0);
        await Assert.That(payload.RootElement.GetProperty("requiresLaptop").GetBoolean()).IsTrue();
    }

    [Test]
    public async Task MapArgumentsToCommand_ForPhaseFiveSubResourceTool_CreatesProposalCommandWithPayloadOnly()
    {
        var conversationId = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();
        var concurrencyStamp = Guid.CreateVersion7();
        var tool = new AiMcpProjectedProposalTool(FindSubResourceDefinition(AiProposedActionKind.CreateEventSession));
        var arguments = CreateArguments(
            $$"""
              {
                "conversationId": "{{conversationId}}",
                "summary": "Create a session",
                "eventId": "{{eventId}}",
                "expectedConcurrencyStamp": "{{concurrencyStamp}}",
                "managementContextHasAddSession": true,
                "title": "Opening session",
                "startTime": "2026-07-01T09:00:00Z",
                "endTime": "2026-07-01T10:00:00Z"
              }
              """);

        var command = tool.MapArgumentsToCommand(arguments);

        await Assert.That(command.ToolName).IsEqualTo("CreateEventSession");
        await Assert.That(command.ConversationId).IsEqualTo(conversationId);
        await Assert.That(command.Summary).IsEqualTo("Create a session");
        using var payload = JsonDocument.Parse(command.PayloadJson);
        await Assert.That(payload.RootElement.TryGetProperty("conversationId", out _)).IsFalse();
        await Assert.That(payload.RootElement.TryGetProperty("summary", out _)).IsFalse();
        await Assert.That(payload.RootElement.GetProperty("eventId").GetGuid()).IsEqualTo(eventId);
        await Assert.That(payload.RootElement.GetProperty("expectedConcurrencyStamp").GetGuid()).IsEqualTo(concurrencyStamp);
        await Assert.That(payload.RootElement.GetProperty("managementContextHasAddSession").GetBoolean()).IsTrue();
        await Assert.That(payload.RootElement.GetProperty("title").GetString()).IsEqualTo("Opening session");
    }

    [Test]
    public async Task ProjectedToolOptionsSetup_AddsRegistryProjectionToMcpOptions()
    {
        var options = new McpServerOptions();

        new AiMcpProjectedToolOptionsSetup(AiToolContractRegistry.CreateDefault()).Configure(options);

        await Assert.That(options.ToolCollection).IsNotNull();
        await Assert.That(ProjectedProposalToolNames.All(options.ToolCollection!.PrimitiveNames.Contains)).IsTrue();
    }

    [Test]
    public async Task ProjectedTool_DoesNotDependOnRepositories()
    {
        var constructorParameters = typeof(AiMcpProjectedProposalTool)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType.Name);

        await Assert.That(constructorParameters).DoesNotContain(name => name.Contains("Repository", StringComparison.Ordinal));
    }

    [Test]
    public async Task InvokeAsync_ResolvesMediatorFromRequestScopeAndPropagatesCancellationToken()
    {
        var conversationId = Guid.CreateVersion7();
        var tool = new AiMcpProjectedProposalTool(CreateEventDraftAiToolDefinition.Create());
        var mediator = Substitute.For<IMediator>();
        var expectedToken = new CancellationTokenSource().Token;
        mediator.Send(Arg.Any<ProposeAiToolActionCommand>(), expectedToken)
            .Returns(BaseCommandResponse.Success(
                Guid.CreateVersion7(),
                "Confirm the proposed action before side effects."));
        await using var services = new ServiceCollection()
            .AddSingleton(mediator)
            .BuildServiceProvider();
        var request = new RequestContext<CallToolRequestParams>(
            Substitute.For<McpServer>(),
            new JsonRpcRequest { Method = "tools/call" },
            new CallToolRequestParams
            {
                Name = tool.ProtocolTool.Name,
                Arguments = CreateArguments(
                    $$"""
                      {
                        "conversationId": "{{conversationId}}",
                        "summary": "Draft an event",
                        "title": "Projected MCP draft"
                      }
                      """)
            })
        {
            Services = services
        };

        var result = await tool.InvokeAsync(request, expectedToken);

        await Assert.That(result.IsError).IsFalse();
        await mediator.Received(1).Send(
            Arg.Is<ProposeAiToolActionCommand>(command =>
                command.ConversationId == conversationId &&
                command.ToolName == "CreateEventDraft" &&
                command.Summary == "Draft an event" &&
                command.PayloadJson.Contains("Projected MCP draft", StringComparison.Ordinal)),
            expectedToken);
    }

    [Test]
    public async Task McpAdapterTelemetry_NormalizesUntrustedDiagnosticsDimensions()
    {
        await Assert.That(McpAdapterTelemetry.ActivitySourceName).IsEqualTo("Explore.Mcp");
        await Assert.That(McpAdapterTelemetry.MeterName).IsEqualTo("Explore.Mcp");
        await Assert.That(McpAdapterTelemetry.NormalizeToolNameForDiagnostics("Bearer secret propose_create_event_draft")).IsEqualTo("unknown");
        foreach (var toolName in ProjectedProposalToolNames)
        {
            await Assert.That(McpAdapterTelemetry.NormalizeToolNameForDiagnostics(toolName)).IsEqualTo(toolName);
        }

        await Assert.That(McpAdapterTelemetry.NormalizeToolNameForDiagnostics("search_public_events")).IsEqualTo("search_public_events");
        await Assert.That(McpAdapterTelemetry.NormalizeToolNameForDiagnostics("get_public_event")).IsEqualTo("get_public_event");
        await Assert.That(McpAdapterTelemetry.NormalizeToolNameForDiagnostics("get_public_event_program_summary")).IsEqualTo("get_public_event_program_summary");
        await Assert.That(McpAdapterTelemetry.NormalizeToolNameForDiagnostics("list_public_event_sessions")).IsEqualTo("list_public_event_sessions");
        await Assert.That(McpAdapterTelemetry.NormalizeToolNameForDiagnostics("list_my_events")).IsEqualTo("list_my_events");
        await Assert.That(McpAdapterTelemetry.NormalizeToolNameForDiagnostics("get_event_creation_context")).IsEqualTo("get_event_creation_context");
        await Assert.That(McpAdapterTelemetry.NormalizeToolNameForDiagnostics("get_event_publish_readiness")).IsEqualTo("get_event_publish_readiness");
        await Assert.That(McpAdapterTelemetry.NormalizeToolNameForDiagnostics("get_event_program_management_context")).IsEqualTo("get_event_program_management_context");
        await Assert.That(McpAdapterTelemetry.NormalizeToolNameForDiagnostics("get_event_custom_properties_context")).IsEqualTo("get_event_custom_properties_context");
        await Assert.That(McpAdapterTelemetry.NormalizeToolNameForDiagnostics("get_event_registrations_context")).IsEqualTo("get_event_registrations_context");
        await Assert.That(McpAdapterTelemetry.NormalizeToolNameForDiagnostics("get_event_team_context")).IsEqualTo("get_event_team_context");
        await Assert.That(McpAdapterTelemetry.NormalizeToolNameForDiagnostics("get_event_template_catalog_context")).IsEqualTo("get_event_template_catalog_context");
        await Assert.That(McpAdapterTelemetry.NormalizeToolNameForDiagnostics("get_event_template_sync_context")).IsEqualTo("get_event_template_sync_context");
        await Assert.That(McpAdapterTelemetry.NormalizeToolNameForDiagnostics("get_event_session_template_sync_context")).IsEqualTo("get_event_session_template_sync_context");
        await Assert.That(McpAdapterTelemetry.NormalizeFailureCodeForDiagnostics("private-prompt-marker")).IsEqualTo("unknown");
        await Assert.That(McpAdapterTelemetry.NormalizeFailureCodeForDiagnostics("invalid_tool_arguments")).IsEqualTo("invalid_tool_arguments");
    }

    private static Dictionary<string, JsonElement> CreateArguments(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement
            .EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.OrdinalIgnoreCase);
    }

    private static AiToolDefinition FindSubResourceDefinition(AiProposedActionKind kind)
        => EventSubResourceAiToolDefinitions.CreateAll().Single(definition => definition.Kind == kind);

    private static AiToolDefinition FindModerationDefinition(AiProposedActionKind kind)
        => EventModerationAiToolDefinitions.CreateAll().Single(definition => definition.Kind == kind);
}
