namespace Explore.API.Attributes;

/// <summary>
/// Marks an exact instance-owned management action that does not require tenant binding
/// or public tenant lifecycle. The action's existing setup/administrator authorization remains mandatory.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class InstanceManagementAttribute : Attribute;
