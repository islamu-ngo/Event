using System;
using Explore.Domain.Ai;

namespace Explore.Application.Features.AiAssistant.Tools;

public sealed record AiToolDefinition(
    AiProposedActionKind Kind,
    string Name,
    string DisplayName,
    string JsonSchema,
    IReadOnlySet<string> AllowedPayloadFields,
    IReadOnlySet<string> ForbiddenPayloadFields,
    Type? PayloadMapperType = null,
    AiToolAuthorizationRequirement? RequiredAuthorization = null,
    AiToolConfirmationMode ConfirmationMode = AiToolConfirmationMode.Required,
    bool ExposeToProvider = true,
    bool ExposeToMcp = true,
    AiToolAgentMetadata? AgentMetadata = null)
{
    public AiToolAgentMetadata EffectiveAgentMetadata => AgentMetadata ?? AiToolAgentMetadata.Default;
}
