using Explore.Application.Contracts.Operations;
using MediatR;

namespace Event.Architecture.Tests;

internal static class OperationContractDiscovery
{
    internal static bool IsRequest(Type type) => typeof(IBaseRequest).IsAssignableFrom(type)
        || IsNativeRequest(type);

    internal static bool IsNativeRequest(Type type) => typeof(ICommand).IsAssignableFrom(type)
        || type.GetInterfaces().Any(IsResultContract);

    internal static bool IsCommand(Type type) => typeof(ICommand).IsAssignableFrom(type)
        || type.GetInterfaces().Any(contract => contract.IsGenericType
            && contract.GetGenericTypeDefinition() == typeof(ICommand<>));

    internal static bool IsResultContract(Type type) => type.IsGenericType
        && (type.GetGenericTypeDefinition() == typeof(ICommand<>)
            || type.GetGenericTypeDefinition() == typeof(IQuery<>));

    internal static bool IsHandler(Type type) => type.IsGenericType
        && (type.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)
            || type.GetGenericTypeDefinition() == typeof(IRequestHandler<>)
            || IsNativeHandler(type));

    internal static bool IsNativeHandler(Type type) => type.IsGenericType
        && (type.GetGenericTypeDefinition() == typeof(ICommandHandler<>)
            || type.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
            || type.GetGenericTypeDefinition() == typeof(IQueryHandler<,>));
}
