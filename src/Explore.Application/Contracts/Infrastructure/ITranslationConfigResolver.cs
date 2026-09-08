using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Infrastructure;

/// <summary>
/// Resolves TMS (Translation Management System) configuration from the cascading settings engine.
/// <para>
/// Resolution order:
/// 1. Check for tenant-specific override (tenant chooses their own TMS)
/// 2. Fall back to system default
/// </para>
/// </summary>
public interface ITranslationConfigResolver
{
    /// <summary>
    /// Resolves the effective translation configuration for the current tenant.
    /// Always returns a non-null configuration (defaults to Provider=None, DefaultLanguage="en").
    /// </summary>
    Task<TranslationConfiguration> ResolveAsync(CancellationToken ct = default);

    /// <summary>
    /// Invalidates the cached translation configuration.
    /// Call after localization settings are changed in the admin UI.
    /// </summary>
    /// <param name="tenantId">Tenant to invalidate, or null for all tenants.</param>
    void InvalidateCache(Guid? tenantId = null);
}

/// <summary>
/// TMS connection parameters + language governance resolved from GovernanceSettings.
/// </summary>
/// <param name="Provider">The active TMS provider (None → Offline, Tolgee, or Weblate).</param>
/// <param name="ApiUrl">TMS API base URL (e.g., "https://app.tolgee.io" or self-hosted URL).</param>
/// <param name="ProjectId">TMS project identifier.</param>
/// <param name="Component">Weblate component slug (Weblate-specific, null for Tolgee).</param>
/// <param name="DefaultLanguage">Default language code (e.g., "en"). Fallback when no translation exists.</param>
public record TranslationConfiguration(
    TranslationManagementProviderEnum Provider,
    string? ApiUrl,
    string? ProjectId,
    string? Component,
    string DefaultLanguage)
{
    /// <summary>
    /// Ops-controlled subset of the Culture Registry that is enabled for this instance.
    /// Drives the language picker, request localization middleware, and user preference validation.
    /// Defaults to a single-entry list containing <see cref="DefaultLanguage"/> when unset.
    /// </summary>
    public IReadOnlyList<string> EnabledLanguages { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Language used when a requested translation key is missing in the user's language.
    /// Must be present in <see cref="EnabledLanguages"/>; the resolver silently replaces invalid values
    /// with <c>"en"</c>.
    /// </summary>
    public string FallbackLanguage { get; init; } = "en";

    /// <summary>
    /// Kill-switch: when false, the in-app language picker hides itself without a redeploy.
    /// </summary>
    public bool ClientPickerEnabled { get; init; } = true;

    /// <summary>
    /// Emergency toggle: when true, <c>RuntimeTranslationProvider</c> short-circuits to the offline
    /// provider regardless of <see cref="Provider"/>.
    /// </summary>
    public bool ForceOfflineMode { get; init; }
}
