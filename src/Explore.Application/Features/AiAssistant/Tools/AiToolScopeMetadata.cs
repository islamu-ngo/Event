namespace Explore.Application.Features.AiAssistant.Tools;

public sealed record AiToolScopeMetadata(
    IReadOnlySet<string> RouteScopes,
    IReadOnlySet<string> WorkflowScopes,
    IReadOnlySet<string> ContextScopes)
{
    public static AiToolScopeMetadata Empty { get; } = new(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase));
}
