using System.Collections.Concurrent;
using System.Collections.Frozen;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Explore.Persistence.Caching;

public class LookupDataCache : ILookupDataCache
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LookupDataCache> _logger;
    private readonly ConcurrentDictionary<Type, object> _cache = new();

    public LookupDataCache(IServiceScopeFactory scopeFactory, ILogger<LookupDataCache> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public FrozenDictionary<int, T> Get<T>() where T : class
    {
        if (_cache.TryGetValue(typeof(T), out var cached))
        {
            return (FrozenDictionary<int, T>)cached;
        }

        throw new InvalidOperationException($"Lookup cache for {typeof(T).Name} not initialized. Call InitializeAsync first.");
    }

    public T? GetById<T>(int id) where T : class
    {
        var dict = Get<T>();
        return dict.GetValueOrDefault(id);
    }

    public IReadOnlyList<T> GetAll<T>() where T : class
    {
        var dict = Get<T>();
        return dict.Values.ToList().AsReadOnly();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing lookup data cache...");

        using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var enrollment = dbContext.Database.IsNpgsql()
            ? scope.ServiceProvider.GetRequiredService<PostgresNativeQueueEnrollment>() : null;

        await LoadLookupAsync<EventType>(dbContext, enrollment, cancellationToken);
        await LoadLookupAsync<EventFormat>(dbContext, enrollment, cancellationToken);
        await LoadLookupAsync<EventStatus>(dbContext, enrollment, cancellationToken);
        await LoadLookupAsync<AudienceAge>(dbContext, enrollment, cancellationToken);
        await LoadLookupAsync<AudienceGender>(dbContext, enrollment, cancellationToken);
        await LoadLookupAsync<Madhab>(dbContext, enrollment, cancellationToken);
        await LoadLookupAsync<Language>(dbContext, enrollment, cancellationToken);
        await LoadLookupAsync<VisibilityType>(dbContext, enrollment, cancellationToken);
        await LoadLookupAsync<ApprovalStatus>(dbContext, enrollment, cancellationToken);
        await LoadLookupAsync<RegistrationMode>(dbContext, enrollment, cancellationToken);
        await LoadLookupAsync<Role>(dbContext, enrollment, cancellationToken);
        await LoadLookupAsync<OrganizationPosition>(dbContext, enrollment, cancellationToken);
        await LoadLookupAsync<ActorType>(dbContext, enrollment, cancellationToken);
        await LoadLookupAsync<DidCustodyType>(dbContext, enrollment, cancellationToken);
        await LoadLookupAsync<FileType>(dbContext, enrollment, cancellationToken);
        await LoadLookupAsync<TagType>(dbContext, enrollment, cancellationToken);
        await LoadLookupAsync<OwnerType>(dbContext, enrollment, cancellationToken);

        _logger.LogInformation("Lookup data cache initialized with {Count} types", _cache.Count);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        _cache.Clear();
        await InitializeAsync(cancellationToken);
    }

    private async Task LoadLookupAsync<T>(ExploreDbContext dbContext, PostgresNativeQueueEnrollment? enrollment,
        CancellationToken cancellationToken) where T : class
    {
        try
        {
            var items = dbContext.Database.IsNpgsql()
                ? await GlobalLookupCatalogueProviderOperations.ReadAsync<T>(dbContext, enrollment!, cancellationToken)
                : await dbContext.Set<T>().AsNoTracking().ToListAsync(cancellationToken);

            var idProperty = typeof(T).GetProperty("Id");
            if (idProperty is null || idProperty.PropertyType != typeof(int))
            {
                _logger.LogWarning("Lookup type {Type} does not have an int Id property, skipping", typeof(T).Name);
                return;
            }

            var dict = items.ToFrozenDictionary(item => (int)idProperty.GetValue(item)!);
            _cache[typeof(T)] = dict;

            _logger.LogDebug("Cached {Count} {Type} items", items.Count, typeof(T).Name);
        }
        catch (Exception)
        {
            _logger.LogError("Failed to load lookup data for {Type}", typeof(T).Name);
        }
    }
}
