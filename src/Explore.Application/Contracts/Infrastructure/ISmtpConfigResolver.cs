// ABOUTME: Contract for composing SMTP governance with externally resolved credentials.
// ABOUTME: Supports tenant governance without database-backed credential overrides.

using Explore.Application.Models;

namespace Explore.Application.Contracts.Infrastructure;

/// <summary>
/// Resolves non-secret SMTP policy from governance and credentials from ISecretResolver.
/// </summary>
public interface ISmtpConfigResolver
{
    /// <summary>
    /// Resolves the effective SMTP configuration for the current tenant.
    /// Returns null if SMTP is not configured (host is empty/missing).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resolved SMTP configuration, or null if not configured.</returns>
    Task<SmtpConfiguration?> ResolveAsync(CancellationToken cancellationToken = default);

    /// <summary>Resolves an explicit tenant scope, or instance transport when tenantId is null.</summary>
    Task<SmtpConfiguration?> ResolveAsync(Guid? tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates the cached SMTP configuration.
    /// Call after SMTP settings are changed in the admin UI.
    /// </summary>
    /// <param name="tenantId">Tenant to invalidate, or null for all tenants.</param>
    void InvalidateCache(Guid? tenantId = null);
}
