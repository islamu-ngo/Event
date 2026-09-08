namespace Explore.Domain;

internal static class TenantIdentity
{
    internal static void Set(ref Guid field, Guid value, string entityName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException($"{entityName} tenant identity is required.", nameof(value));
        }

        if (field != Guid.Empty && field != value)
        {
            throw new InvalidOperationException($"{entityName} tenant identity is immutable.");
        }

        field = value;
    }
}
