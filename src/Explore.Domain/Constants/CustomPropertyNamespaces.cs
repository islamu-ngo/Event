namespace Explore.Domain.Constants;

public static class CustomPropertyNamespaces
{
    public const string Platform = "platform";
    public const string Sector = "sector";
    public const string Tenant = "tenant";
    public const string Pack = "pack";

    public static readonly string[] ReservedRoots = [Platform, Sector, Pack];

    public static bool IsReserved(string? namespaceValue)
    {
        if (string.IsNullOrWhiteSpace(namespaceValue))
        {
            return false;
        }

        return IsRootOrChild(namespaceValue, Platform)
            || IsRootOrChild(namespaceValue, Sector)
            || IsRootOrChild(namespaceValue, Pack);
    }

    public static bool IsTenantOwned(string? namespaceValue)
    {
        if (string.IsNullOrWhiteSpace(namespaceValue))
        {
            return false;
        }

        return IsRootOrChild(namespaceValue, Tenant);
    }

    private static bool IsRootOrChild(string namespaceValue, string root)
    {
        return namespaceValue.Equals(root, StringComparison.OrdinalIgnoreCase)
            || namespaceValue.StartsWith(root + ".", StringComparison.OrdinalIgnoreCase);
    }
}
