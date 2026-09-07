using System.Text.Json.Serialization;

namespace Explore.API.Mcp;

[JsonSerializable(typeof(AiToolRegistryMcpTools.AiToolContractListDescriptor))]
[JsonSerializable(typeof(AiToolRegistryMcpTools.AiToolContractDescriptor))]
[JsonSerializable(typeof(AiToolRegistryMcpTools.AiToolAuthorizationDescriptor))]
[JsonSerializable(typeof(AiAssistantMcpTools.AiMcpCommandResultDescriptor))]
[JsonSerializable(typeof(AiAssistantMcpResources.AiMcpConversationListDescriptor))]
[JsonSerializable(typeof(AiAssistantMcpResources.AiMcpConversationSummaryDescriptor))]
[JsonSerializable(typeof(AiAssistantMcpResources.AiMcpConversationDetailDescriptor))]
[JsonSerializable(typeof(AiAssistantMcpResources.AiMcpMessageDescriptor))]
[JsonSerializable(typeof(AiAssistantMcpResources.AiMcpRunDescriptor))]
[JsonSerializable(typeof(AiAssistantMcpResources.AiMcpReferenceDescriptor))]
[JsonSerializable(typeof(AiAssistantMcpResources.AiMcpProposedActionDescriptor))]
public sealed partial class AiToolRegistryMcpJsonContext : JsonSerializerContext;
