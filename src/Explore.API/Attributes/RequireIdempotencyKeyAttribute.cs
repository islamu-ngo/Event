namespace Explore.API.Attributes;

[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Method,
    Inherited = true,
    AllowMultiple = false)]
public sealed class RequireIdempotencyKeyAttribute : Attribute;
