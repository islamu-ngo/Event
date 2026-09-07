namespace Explore.Application.DTOs.Ai;

public sealed record AiAssistantBootstrapDto
{
    public Guid TenantId { get; init; }
    public bool Enabled { get; init; }
    public bool Available { get; init; }
    public string? DisabledReason { get; init; }
    public string Provider { get; init; } = "none";
    public string? DefaultModelId { get; init; }
    public IReadOnlyList<AiAssistantActorContextDto> ActorContexts { get; init; } = [];
    public IReadOnlyList<AiAssistantModelDto> Models { get; init; } = [];
    public AiAssistantFeatureFlagsDto Features { get; init; } = new();
    public AiAssistantLimitsDto Limits { get; init; } = new();
    public int RetentionDays { get; init; }
}

public sealed record AiAssistantActorContextDto
{
    public Guid ActorId { get; init; }
    public Guid? ScopeId { get; init; }
    public string ActorType { get; init; } = string.Empty;
    public string ActorDisplayName { get; init; } = string.Empty;
}

public sealed record AiAssistantModelDto
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public int? MaxInputTokens { get; init; }
    public int? MaxOutputTokens { get; init; }
    public bool SupportsToolProposals { get; init; }
    public bool SupportsStreaming { get; init; }
}

public sealed record AiAssistantModelDiscoveryRequestDto
{
    public string EndpointUrl { get; init; } = string.Empty;
    public string? ApiKey { get; init; }
}

public sealed record AiAssistantFeatureFlagsDto
{
    public bool ToolProposalsEnabled { get; init; }
    public bool StreamingEnabled { get; init; }
}

public sealed record AiAssistantLimitsDto
{
    public int MaxInputTokens { get; init; }
    public int MaxOutputTokens { get; init; }
    public decimal Temperature { get; init; }
    public int TimeoutSeconds { get; init; }
    public int DailyMessageLimit { get; init; }
}
