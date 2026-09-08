namespace Explore.Application.Contracts.Infrastructure.Ai;

public interface IAiChatProvider
{
    Task<AiChatProviderResult> SendAsync(AiChatPayload request, CancellationToken cancellationToken = default);
}
