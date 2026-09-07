using System.Reflection;
using System.Runtime.CompilerServices;
using Explore.Application.DTOs.Geocoding;

namespace Event.Application.UnitTests.Features.Geocoding;

internal static class PhotonApplicationContractAssertions
{
    private static readonly Assembly ApplicationAssembly = typeof(AddressSuggestionDto).Assembly;

    internal static Type RequireType(string relativeName, string behavior)
    {
        string fullName = relativeName.StartsWith("Explore.", StringComparison.Ordinal)
            ? relativeName
            : $"Explore.Application.{relativeName}";
        return ApplicationAssembly.GetType(fullName, throwOnError: false)
            ?? throw Red($"{behavior}; missing production contract '{fullName}'.");
    }

    internal static PropertyInfo RequireProperty(Type type, string name, string behavior) =>
        type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
        ?? throw Red($"{behavior}; {type.FullName} must expose '{name}'.");

    internal static MethodInfo RequireMethod(Type type, string name, string behavior) =>
        type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .SingleOrDefault(method => method.Name == name)
        ?? throw Red($"{behavior}; {type.FullName} must expose one '{name}' method.");

    internal static void RequireConstructorDependency(Type handler, Type dependency, string behavior)
    {
        bool found = handler.GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .SelectMany(constructor => constructor.GetParameters())
            .Any(parameter => parameter.ParameterType == dependency);
        if (!found)
        {
            throw Red($"{behavior}; {handler.FullName} must depend on {dependency.FullName}.");
        }
    }

    internal static void RequireAsyncCallBefore(
        Type handler,
        Type firstContract,
        string firstMethod,
        Type secondContract,
        string secondMethod,
        string behavior)
    {
        MethodInfo handle = handler.GetMethod("Handle", BindingFlags.Instance | BindingFlags.Public)
            ?? throw Red($"{behavior}; {handler.FullName} has no public Handle method.");
        Type? stateMachine = handle.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
        MethodInfo? body = stateMachine?.GetMethod(
            "MoveNext",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        byte[] il = body?.GetMethodBody()?.GetILAsByteArray()
            ?? throw Red($"{behavior}; {handler.FullName} has no inspectable async body.");
        IReadOnlyList<MethodBase> calls = ResolveCalls(body!, il);
        int first = FindCall(calls, firstContract, firstMethod);
        int second = FindCall(calls, secondContract, secondMethod);
        if (first < 0 || second < 0 || first >= second)
        {
            throw Red($"{behavior}; {firstContract.Name}.{firstMethod} must complete before "
                + $"{secondContract.Name}.{secondMethod} in {handler.Name}.");
        }
    }

    internal static string[] PublicPropertyNames(Type type) => type
        .GetProperties(BindingFlags.Instance | BindingFlags.Public)
        .Select(property => property.Name)
        .Order(StringComparer.Ordinal)
        .ToArray();

    internal static string[] EnumNames(Type type, string behavior)
    {
        if (!type.IsEnum)
        {
            throw Red($"{behavior}; {type.FullName} must be an enum.");
        }

        return Enum.GetNames(type);
    }

    internal static InvalidOperationException Red(string reason) =>
        new($"RED - absent Photon provider integration: {reason}");

    private static IReadOnlyList<MethodBase> ResolveCalls(MethodInfo body, byte[] il)
    {
        List<MethodBase> calls = [];
        Type[]? declaringArguments = body.DeclaringType?.GetGenericArguments();
        Type[]? methodArguments = body.GetGenericArguments();
        for (int index = 0; index <= il.Length - 5; index++)
        {
            if (il[index] is not 0x28 and not 0x6f)
            {
                continue;
            }

            int token = BitConverter.ToInt32(il, index + 1);
            try
            {
                MethodBase? called = body.Module.ResolveMethod(token, declaringArguments, methodArguments);
                if (called is not null)
                {
                    calls.Add(called);
                }
            }
            catch (ArgumentException)
            {
                // Non-call operand bytes can resemble an opcode while scanning the compact async body.
            }
        }

        return calls;
    }

    private static int FindCall(IReadOnlyList<MethodBase> calls, Type contract, string method) =>
        calls.Select((call, index) => (call, index))
            .Where(item => item.call.DeclaringType == contract && item.call.Name == method)
            .Select(item => item.index)
            .DefaultIfEmpty(-1)
            .First();
}
