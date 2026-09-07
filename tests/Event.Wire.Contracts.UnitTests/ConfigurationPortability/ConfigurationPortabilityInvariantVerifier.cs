namespace ISLAMU.Wire.Contracts.UnitTests.ConfigurationPortability;

using System.Collections;
using System.Reflection;

internal static class ConfigurationPortabilityInvariantVerifier
{
    private static readonly string[] ForbiddenAuthorityFragments =
    [
        "Password", "Secret", "ApiKey", "AccessToken", "RefreshToken",
        "Credential", "ConnectionString", "BuyerEmail", "UserId", "SubjectId",
        "TenantId", "InstanceId", "TargetTenant", "TargetInstance",
        "ProviderAccount", "ProviderRequest", "DeploymentHost", "DatabaseHost",
        "Checkpoint", "ReconciliationState", "AcceptanceHistory"
    ];

    internal static IReadOnlyList<string> FindForbiddenPublicMembers(
        IEnumerable<Type> roots)
    {
        var failures = new List<string>();
        var visited = new HashSet<Type>();
        foreach (Type root in roots)
            Visit(root, root.Name, visited, failures, depth: 0);
        return failures.Order(StringComparer.Ordinal).Take(32).ToArray();
    }

    internal static IReadOnlyList<string> FindValueLeaks(
        IEnumerable<object> diagnostics,
        string sentinel)
    {
        var failures = new List<string>();
        foreach (object diagnostic in diagnostics)
        {
            foreach (PropertyInfo property in diagnostic.GetType().GetProperties(
                         BindingFlags.Public | BindingFlags.Instance))
            {
                object? value = property.GetValue(diagnostic);
                if (value?.ToString()?.Contains(sentinel, StringComparison.Ordinal) == true)
                    failures.Add($"{diagnostic.GetType().Name}.{property.Name}");
            }

            if (diagnostic.ToString()?.Contains(sentinel, StringComparison.Ordinal) == true)
                failures.Add($"{diagnostic.GetType().Name}.ToString");
        }

        return failures.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    internal static IEnumerable<object> PublicEnumerable(object target, string propertyName) =>
        (ConfigurationPortabilityContractRuntime.Property(target, propertyName) as IEnumerable
            ?? throw new InvalidOperationException(
                $"Public member '{target.GetType().FullName}.{propertyName}' is not enumerable."))
        .Cast<object>();

    private static void Visit(
        Type type,
        string path,
        ISet<Type> visited,
        ICollection<string> failures,
        int depth)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (depth > 12 || IsTerminal(type) || !visited.Add(type))
            return;

        if (TryCollectionElement(type, out Type? element))
        {
            Visit(element, path + "[]", visited, failures, depth + 1);
            return;
        }

        foreach (PropertyInfo property in type.GetProperties(
                     BindingFlags.Public | BindingFlags.Instance))
        {
            string memberPath = $"{path}.{property.Name}";
            if (ForbiddenAuthorityFragments.Any(fragment =>
                    property.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            {
                failures.Add(memberPath);
            }

            Visit(property.PropertyType, memberPath, visited, failures, depth + 1);
        }
    }

    private static bool IsTerminal(Type type) =>
        type.IsPrimitive
        || type.IsEnum
        || type == typeof(string)
        || type == typeof(decimal)
        || type == typeof(DateTime)
        || type == typeof(DateTimeOffset)
        || type == typeof(Guid)
        || type.Namespace is not null
            && type.Namespace.StartsWith("System.Text.Json", StringComparison.Ordinal);

    private static bool TryCollectionElement(Type type, out Type element)
    {
        if (type.IsArray)
        {
            element = type.GetElementType()!;
            return true;
        }

        Type? dictionary = type.GetInterfaces().Append(type).FirstOrDefault(candidate =>
            candidate.IsGenericType
            && candidate.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>));
        if (dictionary is not null)
        {
            element = dictionary.GetGenericArguments()[1];
            return true;
        }

        Type? enumerable = type.GetInterfaces().Append(type).FirstOrDefault(candidate =>
            candidate.IsGenericType
            && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        if (enumerable is not null)
        {
            element = enumerable.GetGenericArguments()[0];
            return true;
        }

        element = null!;
        return false;
    }
}

internal sealed record SyntheticForbiddenPortableRecord(string ProviderCredential);
internal sealed record SyntheticLeakingDiagnostic(string Code, string Path, string SuppliedValue);
