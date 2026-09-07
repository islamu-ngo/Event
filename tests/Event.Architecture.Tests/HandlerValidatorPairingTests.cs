using FluentValidation;
using MediatR;

namespace Event.Architecture.Tests;

public sealed class HandlerValidatorPairingTests
{
    [Test]
    public async Task CqrsHandlersMustNotInjectFluentValidationValidators()
    {
        Type[] handlers = typeof(Explore.Application.ApplicationServicesRegistration)
            .Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(type => type.GetInterfaces().Any(IsRequestHandlerInterface))
            .ToArray();

        string[] failures = handlers
            .SelectMany(handler => handler
                .GetConstructors()
                .SelectMany(constructor => constructor.GetParameters())
                .Where(parameter => IsValidatorType(parameter.ParameterType))
                .Select(parameter =>
                    $"{handler.FullName} injects {parameter.ParameterType.FullName}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(failures).IsEmpty()
            .Because("validators are manually instantiated at the owning behavior seam, never injected");
    }

    private static bool IsRequestHandlerInterface(Type type) =>
        type.IsGenericType
        && type.GetGenericTypeDefinition() == typeof(IRequestHandler<,>);

    private static bool IsValidatorType(Type type) =>
        type.IsGenericType
            ? type.GetGenericTypeDefinition() == typeof(IValidator<>)
            : type.GetInterfaces().Any(candidate =>
                candidate.IsGenericType
                && candidate.GetGenericTypeDefinition() == typeof(IValidator<>));
}
