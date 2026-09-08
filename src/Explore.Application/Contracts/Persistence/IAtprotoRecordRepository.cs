using Explore.Domain;
using Explore.Domain.Federation;

namespace Explore.Application.Contracts.Persistence;

public interface IAtprotoRecordRepository
{
    Task<AtprotoRecord?> GetById(Guid id);
    Task<bool> Exists(Guid id);
    Task<List<AtprotoRecord>> GetAllAtprotoRecords();
    Task<AtprotoRecord?> GetAtprotoRecordByUri(string uri);
    Task<List<AtprotoRecord>> GetAtprotoRecordsByDid(string did);
    Task<List<AtprotoRecord>> GetAtprotoRecordsByCollection(string collection);
    Task<AtprotoRecord?> GetOwnedRecordAsync(
        Guid tenantId,
        Guid userId,
        string sourceEntityType,
        Guid sourceEntityId,
        CancellationToken cancellationToken = default);

    Task<AtprotoOutboundRecordOwnership?> GetOwnedRecordForSourceAsync(
        Guid tenantId,
        string sourceEntityType,
        Guid sourceEntityId,
        CancellationToken cancellationToken = default);

    Task<List<AtprotoOutboundRecordOwnership>> GetLiveGroundedEventOwnershipsForActorAsync(
        Guid actorId,
        CancellationToken cancellationToken = default);

    Task<List<AtprotoOutboundRecordOwnership>> GetLiveGroundedEventOwnershipsForActorAndDidAsync(
        Guid actorId,
        string did,
        CancellationToken cancellationToken = default);
}
