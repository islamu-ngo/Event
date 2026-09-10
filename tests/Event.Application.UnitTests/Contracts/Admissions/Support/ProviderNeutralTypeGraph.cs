namespace ApplicationUnitTests.Contracts.Admissions.Support;

internal static class ProviderNeutralTypeGraph
{
    internal static IReadOnlyCollection<Type> Closure(IEnumerable<Type> roots)
    {
        var visited = new HashSet<Type>();
        var pending = new Stack<Type>(roots);
        while (pending.TryPop(out Type? type))
        {
            if (!visited.Add(type)) continue;

            if (type.HasElementType && type.GetElementType() is { } elementType)
                pending.Push(elementType);

            Type? nullable = Nullable.GetUnderlyingType(type);
            if (nullable is not null) pending.Push(nullable);

            if (type.IsGenericType)
            {
                pending.Push(type.GetGenericTypeDefinition());
                foreach (Type argument in type.GetGenericArguments()) pending.Push(argument);
            }

            if (type.BaseType is not null) pending.Push(type.BaseType);
            foreach (Type contract in type.GetInterfaces()) pending.Push(contract);
        }

        return visited;
    }

    internal static void EnsureProviderNeutralPublicConstructors(Type serviceType)
    {
        Type[] constructorTypes = serviceType.GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();
        if (Closure(constructorTypes).Any(IsProviderSpecific))
            throw new InvalidOperationException($"Provider-specific public constructor found on {serviceType.Name}.");
    }

    internal static IEnumerable<Type> PublicSignatureTypes(Type contract)
    {
        yield return contract;
        foreach (System.Reflection.ConstructorInfo constructor in contract.GetConstructors())
            foreach (System.Reflection.ParameterInfo parameter in constructor.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        foreach (System.Reflection.MethodInfo method in contract.GetMethods(
                     System.Reflection.BindingFlags.Instance |
                     System.Reflection.BindingFlags.Public |
                     System.Reflection.BindingFlags.DeclaredOnly))
        {
            yield return method.ReturnType;
            foreach (System.Reflection.ParameterInfo parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }
    }

    internal static bool IsProviderSpecific(Type type)
    {
        string name = type.FullName ?? type.Name;
        return name.Contains("Stripe", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("PaymentIntent", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("RefundProvider", StringComparison.OrdinalIgnoreCase);
    }
}
