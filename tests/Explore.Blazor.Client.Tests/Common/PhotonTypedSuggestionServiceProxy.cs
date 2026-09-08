using System.Reflection;

namespace Explore.Blazor.Client.Tests.Common;

internal class PhotonTypedSuggestionServiceProxy : DispatchProxy
{
    private IReadOnlyList<HalResourceOfAddressSuggestionDto> _suggestions = [];
    private string _outcome = "None";

    internal static object Create(out PhotonTypedSuggestionServiceProxy proxy)
    {
        object service = DispatchProxy.Create(
            typeof(IAddressSuggestionService),
            typeof(PhotonTypedSuggestionServiceProxy));
        proxy = (PhotonTypedSuggestionServiceProxy)service;
        return service;
    }

    internal void Configure(
        IReadOnlyList<HalResourceOfAddressSuggestionDto> suggestions,
        string outcome)
    {
        _suggestions = suggestions;
        _outcome = outcome;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == nameof(IAddressSuggestionService.SearchAsync))
        {
            Type resultType = RequireTaskResult(targetMethod.ReturnType);
            object result = Activator.CreateInstance(resultType)
                ?? throw Red("typed suggestion result needs a parameterless construction path");
            RequireProperty(resultType, "Suggestions").SetValue(result, _suggestions);
            PropertyInfo outcome = RequireProperty(resultType, "ProviderOutcome");
            outcome.SetValue(result, Enum.Parse(outcome.PropertyType, _outcome));
            MethodInfo fromResult = typeof(Task).GetMethods()
                .Single(method => method.Name == nameof(Task.FromResult))
                .MakeGenericMethod(resultType);
            return fromResult.Invoke(null, [result]);
        }

        return Task.CompletedTask;
    }

    private static Type RequireTaskResult(Type taskType) =>
        taskType.IsGenericType && taskType.GetGenericTypeDefinition() == typeof(Task<>)
            ? taskType.GenericTypeArguments[0]
            : throw Red("address suggestion search must return Task<TypedResult>");

    private static PropertyInfo RequireProperty(Type type, string name) =>
        type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
        ?? throw Red($"typed suggestion result must expose {name}");

    private static InvalidOperationException Red(string reason) =>
        new($"RED - absent typed optional-provider service contract: {reason}.");
}
