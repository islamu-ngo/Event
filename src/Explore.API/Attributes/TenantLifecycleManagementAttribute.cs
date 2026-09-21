namespace Explore.API.Attributes;

/// <summary>Marks exact bound-tenant administration; never grants authority by itself.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class TenantLifecycleManagementAttribute : Attribute;
