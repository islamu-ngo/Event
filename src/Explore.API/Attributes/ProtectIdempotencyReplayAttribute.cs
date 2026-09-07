namespace Explore.API.Attributes;

[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Method,
    Inherited = true,
    AllowMultiple = false)]
public sealed class ProtectIdempotencyReplayAttribute(params string[] responseHeaders) : Attribute
{
    public IReadOnlyList<string> ResponseHeaders { get; } = responseHeaders;
}
