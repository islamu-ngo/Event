namespace Explore.API.Attributes;

/// <summary>Trusted route metadata selecting the centrally composed file response CSP.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SandboxedFileResponseAttribute : Attribute
{
}
