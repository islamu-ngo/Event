namespace Explore.API.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public sealed class SuppressIdempotencyResponseStorageAttribute : Attribute;
