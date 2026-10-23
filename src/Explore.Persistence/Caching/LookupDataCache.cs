using System.Collections.Frozen;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Explore.Persistence.Caching;

public class LookupDataCache : ILookupDataCache, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LookupDataCache> _logger;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private FrozenDictionary<Type, object> _cache = FrozenDictionary<Type, object>.Empty;

    public LookupDataCache(IServiceScopeFactory scopeFactory, ILogger<LookupDataCache> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public FrozenDictionary<int, T> Get<T>() where T : class
    {
        if (Volatile.Read(ref _cache).TryGetValue(typeof(T), out var cached))
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
        await _loadGate.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Initializing lookup data cache...");
            var snapshot = await LoadSnapshotAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Volatile.Write(ref _cache, snapshot);
            _logger.LogInformation("Lookup data cache initialized with {Count} types", snapshot.Count);
        }
        finally
        {
            _loadGate.Release();
        }
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default) => InitializeAsync(cancellationToken);

    private async Task<FrozenDictionary<Type, object>> LoadSnapshotAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        await using var dbContext = await scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<ExploreDbContext>>()
            .CreateDbContextAsync(cancellationToken);
        var enrollment = dbContext.Database.IsNpgsql()
            ? scope.ServiceProvider.GetRequiredService<PostgresNativeQueueEnrollment>() : null;
        var snapshot = new Dictionary<Type, object>(17);

        await LoadLookupAsync<EventType>(snapshot, dbContext, enrollment, cancellationToken);
        await LoadLookupAsync<EventFormat>(snapshot, dbContext, enrollment, cancellationToken);
        await LoadLookupAsync<EventStatus>(snapshot, dbContext, enrollment, cancellationToken);
        await LoadLookupAsync<AudienceAge>(snapshot, dbContext, enrollment, cancellationToken);
        await LoadLookupAsync<AudienceGender>(snapshot, dbContext, enrollment, cancellationToken);
        await LoadLookupAsync<Madhab>(snapshot, dbContext, enrollment, cancellationToken);
        await LoadLookupAsync<Language>(snapshot, dbContext, enrollment, cancellationToken);
        await LoadLookupAsync<VisibilityType>(snapshot, dbContext, enrollment, cancellationToken);
        await LoadLookupAsync<ApprovalStatus>(snapshot, dbContext, enrollment, cancellationToken);
        await LoadLookupAsync<RegistrationMode>(snapshot, dbContext, enrollment, cancellationToken);
        await LoadLookupAsync<Role>(snapshot, dbContext, enrollment, cancellationToken);
        await LoadLookupAsync<OrganizationPosition>(snapshot, dbContext, enrollment, cancellationToken);
        await LoadLookupAsync<ActorType>(snapshot, dbContext, enrollment, cancellationToken);
        await LoadLookupAsync<DidCustodyType>(snapshot, dbContext, enrollment, cancellationToken);
        await LoadLookupAsync<FileType>(snapshot, dbContext, enrollment, cancellationToken);
        await LoadLookupAsync<TagType>(snapshot, dbContext, enrollment, cancellationToken);
        await LoadLookupAsync<OwnerType>(snapshot, dbContext, enrollment, cancellationToken);

        return snapshot.ToFrozenDictionary();
    }

    private async Task LoadLookupAsync<T>(Dictionary<Type, object> snapshot, ExploreDbContext dbContext,
        PostgresNativeQueueEnrollment? enrollment, CancellationToken cancellationToken) where T : class
    {
        var items = await GlobalLookupCatalogueProviderOperations.ReadAsync<T>(dbContext, enrollment, cancellationToken);

        var idProperty = typeof(T).GetProperty("Id");
        if (idProperty is null || idProperty.PropertyType != typeof(int))
            throw new InvalidOperationException("global_lookup_model_shape_drift");

        snapshot.Add(typeof(T), items.ToFrozenDictionary(item => (int)idProperty.GetValue(item)!));
        _logger.LogDebug("Loaded {Count} {Type} items", items.Count, typeof(T).Name);
    }

    public void Dispose()
    {
        _loadGate.Dispose();
        GC.SuppressFinalize(this);
    }
}
