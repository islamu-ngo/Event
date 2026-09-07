using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Tests.Geocoding;

internal static class PhotonDeploymentContractHost
{
    private const string OptionsTypeName = "Explore.Infrastructure.Geocoding.PhotonGeocodingOptions";
    private const string ValidatorTypeName = "Explore.Infrastructure.Geocoding.PhotonOptionsValidator";

    public static object CreateOptions(string provider = "None")
    {
        object options = Activator.CreateInstance(RequireType(OptionsTypeName))
            ?? throw Red("Photon options must have a public parameterless constructor.");
        SetRequired(options, provider, "Provider");
        return options;
    }

    public static object CreateProductionPhotonOptions()
    {
        object options = CreateOptions("Photon");
        SetRequired(options, new Uri("https://photon.operator.example/"), "Endpoint", "BaseAddress");
        SetRequired(options, 5_000, "TotalTimeoutMilliseconds", "RequestTimeoutMilliseconds");
        SetRequired(options, 2, "MaximumRetryCount", "RetryCount");
        SetRequired(options, new[] { 200, 500 }, "RetryDelaysMilliseconds");
        SetRequired(options, 2_000, "ReadinessTimeoutMilliseconds");
        SetRequired(options, 300, "SelectionLifetimeSeconds");
        SetOptional(options, "en", "Language");
        SetOptional(options, new[] { "BE" }, "CountryCodes");
        SetRequired(options, "dataset-canary-v1", "DatasetVersion");
        SetOptional(options, 10, "MaximumResults", "MaxResults");
        SetOptional(options, 65_536, "MaximumResponseBytes", "MaxResponseBytes");
        return options;
    }

    public static ValidateOptionsResult Validate(object options, string environmentName)
    {
        Type validatorType = RequireType(ValidatorTypeName);
        ConstructorInfo constructor = validatorType.GetConstructors()
            .OrderByDescending(candidate => candidate.GetParameters().Length)
            .FirstOrDefault()
            ?? throw Red("PhotonOptionsValidator must have a public constructor.");
        var environment = new ContractHostEnvironment(environmentName);
        object validator = constructor.Invoke(constructor.GetParameters().Select(parameter =>
            parameter.ParameterType == typeof(IHostEnvironment)
                ? environment
                : throw Red($"Unsupported validator dependency '{parameter.ParameterType.FullName}'."))
            .ToArray());
        MethodInfo method = validatorType.GetMethod("Validate", BindingFlags.Instance | BindingFlags.Public)
            ?? throw Red("PhotonOptionsValidator must expose Validate.");

        try
        {
            return (ValidateOptionsResult)(method.Invoke(validator, [null, options])
                ?? throw Red("Photon validation returned no result."));
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    public static ServiceCollection Compose(
        string environmentName,
        params KeyValuePair<string, string?>[] values)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.ConfigureInfrastructureServices(
            configuration,
            new ContractHostEnvironment(environmentName));
        return services;
    }

    public static Type OptionsType => RequireType(OptionsTypeName);

    public static void SetRequired(object target, object? value, params string[] names)
    {
        if (!TrySet(target, value, names))
        {
            throw Red($"{target.GetType().FullName} must expose {string.Join(" or ", names)}.");
        }
    }

    public static object? ReadRequired(object target, params string[] names)
    {
        PropertyInfo? property = names.Select(target.GetType().GetProperty)
            .FirstOrDefault(item => item is not null);
        if (property is null)
        {
            throw Red($"{target.GetType().FullName} must expose {string.Join(" or ", names)}.");
        }

        return property.GetValue(target);
    }

    public static InvalidOperationException Red(string reason) =>
        new($"RED - missing Photon runtime configuration behavior: {reason}");

    private static Type RequireType(string fullName) =>
        typeof(Explore.Infrastructure.InfrastructureServicesRegistration).Assembly.GetType(fullName)
        ?? throw Red($"Missing production type '{fullName}'.");

    private static void SetOptional(object target, object? value, params string[] names) =>
        _ = TrySet(target, value, names);

    private static bool TrySet(object target, object? value, params string[] names)
    {
        PropertyInfo? property = names.Select(target.GetType().GetProperty)
            .FirstOrDefault(item => item?.SetMethod is not null);
        if (property is null)
        {
            return false;
        }

        property.SetValue(target, ConvertValue(value, property.PropertyType));
        return true;
    }

    private static object? ConvertValue(object? value, Type targetType)
    {
        if (value is null || targetType.IsInstanceOfType(value))
        {
            return value;
        }
        if (value is Uri uri && targetType == typeof(string))
        {
            return uri.AbsoluteUri;
        }
        if (value is string text && targetType == typeof(Uri))
        {
            return new Uri(text);
        }
        if (value is int[] numbers && targetType.IsAssignableFrom(typeof(List<int>)))
        {
            return numbers.ToList();
        }
        if (value is string[] strings && targetType.IsAssignableFrom(typeof(List<string>)))
        {
            return strings.ToList();
        }

        return Convert.ChangeType(value, Nullable.GetUnderlyingType(targetType) ?? targetType);
    }

    private sealed class ContractHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "PhotonContractTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
