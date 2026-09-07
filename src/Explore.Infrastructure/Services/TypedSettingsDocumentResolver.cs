namespace Explore.Infrastructure.Services;

using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Settings;
using Explore.Domain.Settings.Documents;
using Microsoft.Extensions.Caching.Memory;

public sealed class TypedSettingsDocumentResolver : ITypedSettingsDocumentResolver
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(5);

    private readonly ITenantSettingsDocumentRepository _tenantSettingsDocumentRepository;
    private readonly IMemoryCache _cache;

    public TypedSettingsDocumentResolver(
        ITenantSettingsDocumentRepository tenantSettingsDocumentRepository,
        IMemoryCache cache)
    {
        _tenantSettingsDocumentRepository = tenantSettingsDocumentRepository;
        _cache = cache;
    }

    public async Task<ResolvedSettingsDocument<TPayload>?> ResolveTenantDocumentAsync<TPayload>(
        SettingsResolutionContext context,
        string documentKey,
        CancellationToken cancellationToken = default)
        where TPayload : notnull
    {
        var documents = await ResolveTenantDocumentsAsync<TPayload>(context, [documentKey], cancellationToken);
        return documents.Count == 0 ? null : documents[0];
    }

    public async Task<IReadOnlyList<ResolvedSettingsDocument<TPayload>>> ResolveTenantDocumentsAsync<TPayload>(
        SettingsResolutionContext context,
        IEnumerable<string> documentKeys,
        CancellationToken cancellationToken = default)
        where TPayload : notnull
    {
        if (context.TenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required for tenant settings document resolution.", nameof(context));
        }

        var requestedKeys = documentKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (requestedKeys.Length == 0)
        {
            return [];
        }

        foreach (var key in requestedKeys)
        {
            ValidateRequestedDocument(context, key);
        }

        var tenantDocuments = await GetTenantDocumentsAsync(context.TenantId, requestedKeys, cancellationToken);
        var documentsByKey = tenantDocuments.ToDictionary(document => document.DocumentKey, StringComparer.Ordinal);
        var resolved = new List<ResolvedSettingsDocument<TPayload>>(requestedKeys.Length);

        foreach (var key in requestedKeys)
        {
            if (!documentsByKey.TryGetValue(key, out var document))
            {
                continue;
            }

            resolved.Add(ToResolvedDocument<TPayload>(document));
        }

        return resolved;
    }

    public void InvalidateTenantDocumentCache(Guid tenantId, string? documentKey = null)
    {
        if (tenantId == Guid.Empty)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(documentKey))
        {
            _cache.Remove(GetTenantDocumentsCacheKey(tenantId));
            return;
        }

        _cache.Remove(GetTenantDocumentsCacheKey(tenantId));
    }

    private async Task<IReadOnlyList<TenantSettingsDocument>> GetTenantDocumentsAsync(
        Guid tenantId,
        IReadOnlyCollection<string> documentKeys,
        CancellationToken cancellationToken)
    {
        var cacheKey = GetTenantDocumentsCacheKey(tenantId);
        var allTenantDocuments = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheExpiration;
            return await _tenantSettingsDocumentRepository.GetManyForTenant(
                tenantId,
                SettingsDocumentKeys.Tenant.All,
                cancellationToken);
        }) ?? [];

        return allTenantDocuments
            .Where(document => documentKeys.Contains(document.DocumentKey, StringComparer.Ordinal))
            .ToArray();
    }

    private static void ValidateRequestedDocument(SettingsResolutionContext context, string documentKey)
    {
        if (!SettingsDocumentTaxonomy.IsNonSecretTenantDocument(documentKey))
        {
            throw new ArgumentException(
                "Document key is not an approved non-secret tenant settings document.",
                nameof(documentKey));
        }

        if (!context.RequestsDocument(documentKey))
        {
            throw new InvalidOperationException(
                $"Document '{documentKey}' was not declared in the settings resolution context.");
        }
    }

    private static ResolvedSettingsDocument<TPayload> ToResolvedDocument<TPayload>(TenantSettingsDocument document)
        where TPayload : notnull
    {
        var payload = JsonSerializer.Deserialize<TPayload>(document.PayloadJson, SerializerOptions)
            ?? throw new InvalidOperationException($"Document '{document.DocumentKey}' payload could not be deserialized.");

        return new ResolvedSettingsDocument<TPayload>
        {
            DocumentId = document.Id,
            DocumentKey = document.DocumentKey,
            SchemaVersion = document.SchemaVersion,
            DefaultsVersion = document.DefaultsVersion,
            Payload = payload,
            Source = SettingsDocumentSource.Tenant,
            SourceScopeId = document.TenantId,
            ConcurrencyStamp = document.ConcurrencyStamp,
            UpdatedAt = document.UpdatedAt,
            UpdatedBy = document.UpdatedBy
        };
    }

    private static string GetTenantDocumentsCacheKey(Guid tenantId) =>
        $"TypedSettings:Tenant:{tenantId}:Documents";
}
