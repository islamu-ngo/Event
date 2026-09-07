using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Explore.Application.Contracts.Infrastructure.Geocoding;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Tests.Geocoding;

internal sealed class PhotonAdapterContractHost : IDisposable
{
    private const string AdapterTypeName = "Explore.Infrastructure.Geocoding.PhotonGeocodingAdapter";
    private const string OptionsTypeName = "Explore.Infrastructure.Geocoding.PhotonGeocodingOptions";
    private readonly HttpClient _httpClient;
    private readonly IAddressSuggestionProviderGateway _adapter;

    private PhotonAdapterContractHost(
        PhotonScriptedHttpHandler handler,
        PhotonManualTimeProvider timeProvider,
        PhotonObservabilityCapture observability,
        Action<object>? configureOptions)
    {
        Handler = handler;
        TimeProvider = timeProvider;
        Observability = observability;
        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://photon.operator.test/"),
            Timeout = Timeout.InfiniteTimeSpan
        };

        Type optionsType = RequireType(OptionsTypeName);
        Options = Activator.CreateInstance(optionsType)
            ?? throw Red($"{OptionsTypeName} must have a public parameterless constructor.");
        configureOptions?.Invoke(Options);

        Type adapterType = RequireType(AdapterTypeName);
        _adapter = (IAddressSuggestionProviderGateway)Create(
            adapterType,
            Options,
            _httpClient,
            timeProvider,
            observability,
            dataProtectionProvider: null);
    }

    public PhotonScriptedHttpHandler Handler { get; }

    public PhotonManualTimeProvider TimeProvider { get; }

    public PhotonObservabilityCapture Observability { get; }

    public object Options { get; }

    public static object CreateDefaultOptions()
    {
        Type optionsType = RequireType(OptionsTypeName);
        return Activator.CreateInstance(optionsType)
            ?? throw Red($"{OptionsTypeName} must have a public parameterless constructor.");
    }

    public static PhotonAdapterContractHost Create(
        PhotonScriptedHttpHandler handler,
        PhotonManualTimeProvider timeProvider,
        PhotonObservabilityCapture observability,
        bool photonEnabled = true,
        int maximumResults = 3,
        int maximumResponseBytes = 65_536)
    {
        return new PhotonAdapterContractHost(handler, timeProvider, observability, options =>
        {
            if (!photonEnabled)
            {
                return;
            }

            SetRequired(options, "Photon", "Provider");
            SetRequired(options, new Uri("https://photon.operator.test/"), "Endpoint", "BaseAddress");
            SetRequired(options, "fr", "Language");
            SetRequired(options, new[] { "BE", "NL" }, "CountryCodes");
            SetRequired(options, maximumResults, "MaximumResults", "MaxResults");
            SetRequired(options, maximumResponseBytes, "MaximumResponseBytes", "MaxResponseBytes");
            SetOptional(options, "dataset-canary-v1", "DatasetVersion");
        });
    }

    public async Task<PhotonSearchOutcome> SearchAsync(
        string searchText = "Rue Provider 30",
        int limit = 20,
        Guid? tenantId = null,
        Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        _ = tenantId;
        _ = userId;
        AddressGeocoderResult raw = await _adapter.SearchAsync(
            new AddressGeocoderRequest(searchText, limit),
            cancellationToken);
        return PhotonSearchOutcome.From(raw);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        Observability.Dispose();
    }

    internal static Type RequireType(string fullName) =>
        typeof(Explore.Infrastructure.InfrastructureServicesRegistration).Assembly.GetType(fullName)
        ?? throw Red($"Missing production type '{fullName}'.");

    internal static void SetRequired(object target, object? value, params string[] names)
    {
        if (!TrySet(target, value, names))
        {
            throw Red($"{target.GetType().FullName} must expose {string.Join(" or ", names)}.");
        }
    }

    internal static void SetOptional(object target, object? value, params string[] names) =>
        _ = TrySet(target, value, names);

    internal static object? Invoke(MethodInfo method, object target, params object?[] arguments)
    {
        try
        {
            return method.Invoke(target, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    internal static InvalidOperationException Red(string reason) =>
        new($"RED - missing Photon production behavior: {reason}");

    private static object Create(
        Type implementationType,
        object options,
        HttpClient client,
        TimeProvider timeProvider,
        PhotonObservabilityCapture observability,
        object? dataProtectionProvider)
    {
        ConstructorInfo constructor = implementationType.GetConstructors()
            .OrderByDescending(candidate => candidate.GetParameters().Length)
            .FirstOrDefault()
            ?? throw Red($"{implementationType.FullName} must have a public constructor.");
        object?[] arguments = constructor.GetParameters().Select(parameter =>
        {
            Type type = parameter.ParameterType;
            if (type == typeof(HttpClient)) return client;
            if (type == typeof(TimeProvider)) return timeProvider;
            if (type == typeof(System.Diagnostics.Metrics.IMeterFactory)) return observability;
            if (type.IsInstanceOfType(dataProtectionProvider)) return dataProtectionProvider;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IOptions<>))
            {
                return Activator.CreateInstance(typeof(OptionsWrapper<>).MakeGenericType(type.GenericTypeArguments[0]), options);
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ILogger<>))
            {
                return Activator.CreateInstance(typeof(Logger<>).MakeGenericType(type.GenericTypeArguments[0]), observability.LoggerFactory);
            }

            throw Red($"Unsupported public constructor dependency '{type.FullName}' on {implementationType.FullName}.");
        }).ToArray();
        return constructor.Invoke(arguments);
    }

    private static bool TrySet(object target, object? value, params string[] names)
    {
        PropertyInfo? property = names.Select(name => target.GetType().GetProperty(name)).FirstOrDefault(item => item is not null);
        if (property?.SetMethod is null)
        {
            return false;
        }

        object? converted = ConvertValue(value, property.PropertyType);
        property.SetValue(target, converted);
        return true;
    }

    private static object? ConvertValue(object? value, Type targetType)
    {
        if (value is null || targetType.IsInstanceOfType(value)) return value;
        if (value is Uri uri && targetType == typeof(string)) return uri.AbsoluteUri;
        if (value is string text && targetType == typeof(Uri)) return new Uri(text);
        if (value is string[] values && targetType.IsAssignableFrom(typeof(List<string>))) return values.ToList();
        if (targetType.IsEnum && value is string enumName) return Enum.Parse(targetType, enumName, ignoreCase: true);
        return Convert.ChangeType(value, Nullable.GetUnderlyingType(targetType) ?? targetType);
    }
}

internal sealed record PhotonSearchOutcome(object? Raw, IReadOnlyList<PhotonSuggestionView> Suggestions)
{
    public static PhotonSearchOutcome From(AddressGeocoderResult raw)
    {
        return new PhotonSearchOutcome(
            raw,
            raw.Selections.Select(PhotonSuggestionView.From).ToArray());
    }
}
