using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Lookup;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.Client.Services;

public interface ILookupCacheService
{
    Task<ICollection<CategoryListDto>> GetCategoriesAsync(CancellationToken ct = default);
    Task<ICollection<TagListDto>> GetTagsAsync(CancellationToken ct = default);
    Task<ICollection<EventTypeListDto>> GetEventTypesAsync(CancellationToken ct = default);
    Task<ICollection<EventFormatListDto>> GetEventFormatsAsync(CancellationToken ct = default);
    Task<ICollection<MadhabListDto>> GetMadhabsAsync(CancellationToken ct = default);
    Task<ICollection<LocationListDto>> GetLocationsAsync(CancellationToken ct = default);
    Task<ICollection<LanguageListDto>> GetLanguagesAsync(CancellationToken ct = default);
    void InvalidateAll();
}

public class LookupCacheService : ILookupCacheService, IDisposable
{
    private record CacheEntry<T>(T Data, DateTime ExpiresAt)
    {
        public bool IsValid => DateTime.UtcNow < ExpiresAt;
    }

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    private CacheEntry<ICollection<CategoryListDto>>? _categories;
    private CacheEntry<ICollection<TagListDto>>? _tags;
    private CacheEntry<ICollection<EventTypeListDto>>? _eventTypes;
    private CacheEntry<ICollection<EventFormatListDto>>? _eventFormats;
    private CacheEntry<ICollection<MadhabListDto>>? _madhabs;
    private CacheEntry<ICollection<LocationListDto>>? _locations;
    private CacheEntry<ICollection<LanguageListDto>>? _languages;

    private readonly SemaphoreSlim _lock = new(1, 1);

    private readonly ICategoryService _categoryService;
    private readonly ITagService _tagService;
    private readonly IEventTypeService _eventTypeService;
    private readonly IEventFormatService _eventFormatService;
    private readonly IMadhabService _madhabService;
    private readonly ILocationService _locationService;
    private readonly ILanguageService _languageService;
    private readonly ILogger<LookupCacheService> _logger;

    public LookupCacheService(
        ICategoryService categoryService,
        ITagService tagService,
        IEventTypeService eventTypeService,
        IEventFormatService eventFormatService,
        IMadhabService madhabService,
        ILocationService locationService,
        ILanguageService languageService,
        ILogger<LookupCacheService> logger)
    {
        _categoryService = categoryService;
        _tagService = tagService;
        _eventTypeService = eventTypeService;
        _eventFormatService = eventFormatService;
        _madhabService = madhabService;
        _locationService = locationService;
        _languageService = languageService;
        _logger = logger;
    }

    public Task<ICollection<CategoryListDto>> GetCategoriesAsync(CancellationToken ct = default)
        => GetOrFetchAsync(
            () => _categories,
            entry => _categories = entry,
            () => _categoryService.GetCategoriesAsync(),
            "Categories",
            ct);

    public Task<ICollection<TagListDto>> GetTagsAsync(CancellationToken ct = default)
        => GetOrFetchAsync(
            () => _tags,
            entry => _tags = entry,
            () => _tagService.GetTagsAsync(),
            "Tags",
            ct);

    public Task<ICollection<EventTypeListDto>> GetEventTypesAsync(CancellationToken ct = default)
        => GetOrFetchAsync(
            () => _eventTypes,
            entry => _eventTypes = entry,
            () => _eventTypeService.GetEventTypesAsync(),
            "EventTypes",
            ct);

    public Task<ICollection<EventFormatListDto>> GetEventFormatsAsync(CancellationToken ct = default)
        => GetOrFetchAsync(
            () => _eventFormats,
            entry => _eventFormats = entry,
            () => _eventFormatService.GetEventFormatsAsync(),
            "EventFormats",
            ct);

    public Task<ICollection<MadhabListDto>> GetMadhabsAsync(CancellationToken ct = default)
        => GetOrFetchAsync(
            () => _madhabs,
            entry => _madhabs = entry,
            () => _madhabService.GetMadhabsAsync(),
            "Madhabs",
            ct);

    public Task<ICollection<LocationListDto>> GetLocationsAsync(CancellationToken ct = default)
        => GetOrFetchAsync(
            () => _locations,
            entry => _locations = entry,
            () => _locationService.GetAllLocationsAsync(),
            "Locations",
            ct);

    public Task<ICollection<LanguageListDto>> GetLanguagesAsync(CancellationToken ct = default)
        => GetOrFetchAsync(
            () => _languages,
            entry => _languages = entry,
            () => _languageService.GetLanguagesAsync(),
            "Languages",
            ct);

    public void InvalidateAll()
    {
        _categories = null;
        _tags = null;
        _eventTypes = null;
        _eventFormats = null;
        _madhabs = null;
        _locations = null;
        _languages = null;
        _logger.LogDebug("[LOOKUP CACHE] All caches invalidated");
    }

    private async Task<ICollection<T>> GetOrFetchAsync<T>(
        Func<CacheEntry<ICollection<T>>?> getCache,
        Action<CacheEntry<ICollection<T>>> setCache,
        Func<Task<ICollection<T>>> fetchFunc,
        string name,
        CancellationToken ct)
    {
        // Fast path: cache is valid
        var entry = getCache();
        if (entry is not null && entry.IsValid)
        {
            _logger.LogDebug("[LOOKUP CACHE] Cache hit for {Name}", name);
            return entry.Data;
        }

        await _lock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            entry = getCache();
            if (entry is not null && entry.IsValid)
            {
                _logger.LogDebug("[LOOKUP CACHE] Cache hit for {Name} (after lock)", name);
                return entry.Data;
            }

            _logger.LogDebug("[LOOKUP CACHE] Cache miss for {Name}, fetching from API", name);
            var data = await fetchFunc();
            setCache(new CacheEntry<ICollection<T>>(data, DateTime.UtcNow.Add(CacheDuration)));
            return data;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        // Intentionally no-op. The service owns only managed in-memory cache state and
        // a SemaphoreSlim used to serialize fetches. Disposing the semaphore during
        // Blazor scoped-service teardown can race with in-flight lookup loads.
    }
}
