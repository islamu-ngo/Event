namespace Explore.Blazor.Services.Auth;

/// <summary>Server-owned source custody required by the cookie store during destination sign-in.</summary>
public sealed record BffSessionTransferSource(
    BffSessionBinding Binding, string Revision, DateTimeOffset HandoffExpiresUtc)
{
    public override string ToString() => nameof(BffSessionTransferSource);
}
