namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Opaque federation-setting observation issued only by the original selected store.
/// Implementing this interface does not grant source custody or human authority.
/// </summary>
public interface IAtprotoFederationSettingsSource;
