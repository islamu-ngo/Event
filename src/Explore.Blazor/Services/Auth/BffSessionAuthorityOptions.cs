namespace Explore.Blazor.Services.Auth;

public sealed class BffSessionAuthorityOptions
{
    public const string SectionName = "Bff:SessionAuthority";
    public string? Endpoint { get; set; }
    public string? Namespace { get; set; }
    public string? Password { get; set; }
    public override string ToString() => nameof(BffSessionAuthorityOptions);
}
