using Explore.Application.Contracts.Services.Registration;
using Explore.Infrastructure.Registration;

namespace Explore.Infrastructure.Tests.Registration;

public sealed class RegistrationProviderRegistryTests
{
    [Test]
    public async Task TryResolve_UsesExactTupleAndUnknownIsNull()
    {
        NativeRegistrationProviderDescriptor native = new();
        RegistrationProviderRegistry registry = new([native]);

        await Assert.That(registry.TryResolve(NativeRegistrationProviderDescriptor.NativeTuple)).IsSameReferenceAs(native);
        await Assert.That(registry.TryResolve(NativeRegistrationProviderDescriptor.NativeTuple with { ApiVersion = "other" })).IsNull();
    }

    [Test]
    public async Task Constructor_RejectsDuplicateTuple()
    {
        NativeRegistrationProviderDescriptor native = new();

        await Assert.That(() => new RegistrationProviderRegistry([native, native])).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task TryResolve_UsesStructuralTupleWhenStringKeysCollide()
    {
        TestDescriptor first = new(new("A|B", "C", "D", "E", "F"));
        TestDescriptor second = new(new("A", "B|C", "D", "E", "F"));
        RegistrationProviderRegistry registry = new([first, second]);

        await Assert.That(first.Tuple.Key).IsEqualTo(second.Tuple.Key);
        await Assert.That(registry.TryResolve(first.Tuple)).IsSameReferenceAs(first);
        await Assert.That(registry.TryResolve(second.Tuple)).IsSameReferenceAs(second);
    }

    private sealed record TestDescriptor(RegistrationProviderTuple Tuple) : IRegistrationProviderDescriptor
    {
        public RegistrationProviderCapabilitySet ProvenCapabilities => RegistrationProviderCapabilitySet.None;
    }
}
