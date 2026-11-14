namespace Explore.Application.Contracts.Persistence;

/// <summary>Reads only cross-tenant operational effect aggregates under independent Global authority.</summary>
public interface IIncomingWebhookEffectHealthReader
{
    Task<IncomingWebhookEffectHealthSnapshot> ReadAsync(DateTime observedAt, CancellationToken cancellationToken);
}

public sealed record IncomingWebhookEffectHealthSnapshot(int DueEffects, int StaleLeases);
