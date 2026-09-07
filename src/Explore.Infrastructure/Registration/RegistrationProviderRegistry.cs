using Explore.Application.Contracts.Services.Registration;

namespace Explore.Infrastructure.Registration;

public sealed class RegistrationProviderRegistry : IRegistrationProviderRegistry
{
    private readonly Dictionary<RegistrationProviderTuple, IRegistrationProviderDescriptor> _descriptors;

    public RegistrationProviderRegistry(IEnumerable<IRegistrationProviderDescriptor> descriptors)
    {
        _descriptors = [];
        foreach (IRegistrationProviderDescriptor descriptor in descriptors)
        {
            if (!_descriptors.TryAdd(descriptor.Tuple, descriptor))
            {
                throw new InvalidOperationException($"Duplicate registration provider tuple '{descriptor.Tuple.Key}'.");
            }
        }
    }

    public IRegistrationProviderDescriptor? TryResolve(RegistrationProviderTuple tuple) =>
        _descriptors.GetValueOrDefault(tuple);
}

public sealed class NullRegistrationProviderDescriptor : IRegistrationProviderDescriptor
{
    public RegistrationProviderTuple Tuple { get; } = new("NULL", "NONE", "0", "NONE", "NONE");
    public RegistrationProviderCapabilitySet ProvenCapabilities => RegistrationProviderCapabilitySet.None;
}

public sealed class NativeRegistrationProviderDescriptor : IRegistrationProviderDescriptor
{
    public RegistrationProviderTuple Tuple { get; } = NativeTuple;
    public RegistrationProviderCapabilitySet ProvenCapabilities => RegistrationProviderCapabilitySet.Native;

    public static RegistrationProviderTuple NativeTuple { get; } = new("NATIVE", "NATIVE", "ISLAMU_EVENT", "D3_NATIVE", "BUILTIN");
}
