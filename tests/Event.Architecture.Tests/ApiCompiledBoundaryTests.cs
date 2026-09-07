using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Architecture.Tests;

public sealed class ApiCompiledBoundaryTests
{
    [Test]
    public async Task ControllersMustNotResolveServicesFromTheRequestContainer()
    {
        Type[] controllers = typeof(Explore.API.Hateoas.RouteNames)
            .Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false }
                && typeof(ControllerBase).IsAssignableFrom(type))
            .ToArray();
        var failures = new List<string>();

        foreach (Type controller in controllers)
        {
            foreach (ParameterInfo parameter in controller
                .GetConstructors()
                .SelectMany(constructor => constructor.GetParameters()))
            {
                if (parameter.ParameterType == typeof(IServiceProvider)
                    || parameter.ParameterType == typeof(IServiceScopeFactory))
                {
                    failures.Add(
                        $"{controller.FullName} injects {parameter.ParameterType.Name}");
                }
            }

            foreach (MethodBase body in EnumerateImplementationBodies(controller))
            {
                if (ResolveCalls(body).Any(IsRequestServicesGetter))
                {
                    failures.Add(
                        $"{controller.FullName}.{body.Name} reads HttpContext.RequestServices");
                }
            }
        }

        await Assert.That(failures).IsEmpty()
            .Because("controller dependencies arrive through typed constructors, never request service location");
    }

    private static IEnumerable<MethodBase> EnumerateImplementationBodies(Type controller)
    {
        const BindingFlags flags = BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.DeclaredOnly;

        foreach (Type type in controller
            .GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .Prepend(controller))
        {
            foreach (MethodInfo method in type.GetMethods(flags))
            {
                if (method.GetMethodBody() is not null)
                {
                    yield return method;
                }
            }

            foreach (ConstructorInfo constructor in type.GetConstructors(flags))
            {
                if (constructor.GetMethodBody() is not null)
                {
                    yield return constructor;
                }
            }
        }
    }

    private static IEnumerable<MethodBase> ResolveCalls(MethodBase body)
    {
        byte[] il = body.GetMethodBody()?.GetILAsByteArray() ?? [];
        Type[]? declaringArguments = body.DeclaringType?.GetGenericArguments();
        Type[]? methodArguments = body.IsGenericMethod
            ? body.GetGenericArguments()
            : null;

        for (int index = 0; index <= il.Length - 5; index++)
        {
            if (il[index] is not 0x28 and not 0x6f)
            {
                continue;
            }

            int token = BitConverter.ToInt32(il, index + 1);
            MethodBase? called;
            try
            {
                called = body.Module.ResolveMethod(
                    token,
                    declaringArguments,
                    methodArguments);
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (called is not null)
            {
                yield return called;
            }
        }
    }

    private static bool IsRequestServicesGetter(MethodBase method) =>
        method.Name == "get_RequestServices"
        && method.DeclaringType is not null
        && typeof(HttpContext).IsAssignableFrom(method.DeclaringType);
}
