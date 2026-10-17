using System.Text.Json;
using Explore.Application.DTOs.Ai;
using Explore.Application.Features.AiAssistant.Requests.Queries;

namespace Explore.Infrastructure.Ai;

public sealed class AiModelDiscoveryGateway(
    IHttpClientFactory clients,
    AiProviderConfigurationResolver configurationResolver) : IAiModelDiscoveryGateway
{
    public async Task<AiModelDiscoveryResult> DiscoverAsync(
        AiAssistantModelDiscoveryRequestDto request, CancellationToken cancellationToken)
    {
        var resolution = await configurationResolver.ResolveDiscoveryAsync(request, cancellationToken);
        if (resolution.Configuration is not { } configuration)
            return new([], resolution.FailureCode);
        var settings = configuration.Settings;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
            var client = clients.CreateClient(
                OpenAiCompatibleChatProvider.HttpClientName
                + (configuration.InstanceTransport ? AiTransportAuthority.InstanceClientSuffix : string.Empty));
            var models = await OpenAiCompatibleChatProvider.DiscoverModelsAsync(
                client, settings.EndpointUrl, configuration.ApiKey, timeout.Token);
            return new(models.Select(model => new AiAssistantModelDto
            {
                Id = model.Id,
                DisplayName = model.DisplayName,
                MaxInputTokens = model.MaxInputTokens,
                MaxOutputTokens = model.MaxOutputTokens,
                SupportsToolProposals = model.SupportsToolProposals,
                SupportsStreaming = model.SupportsStreaming
            }).ToArray());
        }
        catch (Exception exception) when (exception is HttpRequestException
            or JsonException or UriFormatException)
        {
            return new([], "provider_failure");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new([], "provider_timeout");
        }
    }
}
