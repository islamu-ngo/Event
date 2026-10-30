namespace Explore.Blazor.Services.Auth;

public readonly record struct BffSessionBinding(string Handle, string OwnerPartitionKey)
{
    public override string ToString() => nameof(BffSessionBinding);
}
