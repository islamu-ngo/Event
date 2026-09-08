namespace Explore.Blazor.Client.Contracts.Services;

public sealed record GuestRegistrationOrderCapability
{
    internal GuestRegistrationOrderCapability(string value) => Value = value;

    internal string Value { get; }
}
