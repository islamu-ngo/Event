namespace Explore.API.Attributes;

/// <summary>Requires anonymous tenant visibility for a machine-public projection, regardless of caller authentication.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TenantMachinePublicReadAttribute : Attribute;
