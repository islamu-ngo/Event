using System;
using System.Threading;
using System.Threading.Tasks;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Events;

namespace Explore.Blazor.Client.Services;

public class EventSessionSpeakerService : IEventSessionSpeakerService
{
    private readonly IEventSessionSpeakerClient _client;

    public EventSessionSpeakerService(IEventSessionSpeakerClient client)
    {
        _client = client;
    }

    public async Task<HalCollectionResourceOfEventSessionSpeakerListDto> GetSpeakersBySessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _client.GetEventSessionSpeakersBySessionAsync(
                sessionId,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ApiException)
        {
            return new HalCollectionResourceOfEventSessionSpeakerListDto();
        }
    }

    public async Task<BaseCommandResponseOfGuid?> AddSpeakerToSessionAsync(
        Guid sessionId,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _client.CreateEventSessionSpeakerAsync(
                sessionId,
                new CreateEventSessionSpeakerDto
                {
                    ActorId = actorId,
                    EventSessionId = sessionId
                },
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ApiException)
        {
            return null;
        }
    }

    public async Task<bool> RemoveSpeakerFromSessionAsync(
        Guid sessionId,
        Guid speakerId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.DeleteEventSessionSpeakerAsync(
                sessionId,
                speakerId,
                cancellationToken: cancellationToken);

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ApiException)
        {
            return false;
        }
    }
}
