namespace Event.Architecture.Tests;

using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Hateoas;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// <c>HateoasAssemblerRegistration</c> wires several hundred closed generic contracts by hand. The registration
/// style is allowed to change — Phase 4 replaces repeated triples with compile-time helpers — but the resulting
/// graph is a behavioral contract: a dropped policy silently removes a HAL affordance, and because affordances
/// are the client's authorization signal, that is a security-visible regression rather than a cosmetic one.
/// <para>
/// These tests therefore pin the graph's invariants rather than its syntax. They also print the full descriptor
/// inventory, so a refactor can be diffed registration-by-registration between two runs.
/// </para>
/// </summary>
public class HateoasRegistrationGraphTests
{
    private static IReadOnlyList<ServiceDescriptor> HalDescriptors() =>
        [.. new ServiceCollection()
            .AddHateoasAssemblers()
            .Where(descriptor => IsHalContract(descriptor.ServiceType) || IsHalPolicyImplementation(descriptor.ServiceType))];

    [Test]
    public async Task EveryHalRegistration_IsScoped()
    {
        var failures = HalDescriptors()
            .Where(descriptor => descriptor.Lifetime != ServiceLifetime.Scoped)
            .Select(descriptor => $"{Describe(descriptor.ServiceType)} is registered as {descriptor.Lifetime}; HAL policies read per-request authorization state and must be Scoped.")
            .Order(StringComparer.Ordinal)
            .ToList();

        Report(failures);
        await Assert.That(failures).IsEmpty();
    }

    [Test]
    public async Task NoHalContract_IsRegisteredTwiceWithDifferentImplementations()
    {
        var failures = HalDescriptors()
            .Where(descriptor => IsHalContract(descriptor.ServiceType))
            .GroupBy(descriptor => Describe(descriptor.ServiceType), StringComparer.Ordinal)
            .Where(group => group.Select(DescribeImplementation).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(group => $"{group.Key} has conflicting registrations: {string.Join(", ", group.Select(DescribeImplementation).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))}. The last one wins, so one of them is dead configuration.")
            .Order(StringComparer.Ordinal)
            .ToList();

        Report(failures);
        await Assert.That(failures).IsEmpty();
    }

    /// <summary>
    /// An assembler without its detail policy emits a resource with no actions, which reads to a client as
    /// "you may not do anything here" rather than as a wiring mistake.
    /// </summary>
    [Test]
    public async Task EveryResourceAssembler_HasADetailLinkPolicyForItsDetailType()
    {
        var descriptors = HalDescriptors();

        var policyTypes = descriptors
            .Where(descriptor => IsClosedGeneric(descriptor.ServiceType, typeof(ILinkPolicy<>)))
            .Select(descriptor => descriptor.ServiceType.GetGenericArguments()[0])
            .ToHashSet();

        var failures = descriptors
            .Where(descriptor => IsClosedGeneric(descriptor.ServiceType, typeof(IResourceAssembler<,>)))
            .Select(descriptor => descriptor.ServiceType.GetGenericArguments()[0])
            .Distinct()
            .Where(detailType => !policyTypes.Contains(detailType))
            .Select(detailType => $"IResourceAssembler for {detailType.Name} has no ILinkPolicy<{detailType.Name}>, so its resources would carry no detail affordances.")
            .Order(StringComparer.Ordinal)
            .ToList();

        Report(failures);
        await Assert.That(failures).IsEmpty();
    }

    /// <summary>
    /// Prints the graph so two runs can be diffed across a registration refactor. This is evidence, not an
    /// assertion: the assertion that the graph is unchanged is the diff itself.
    /// </summary>
    [Test]
    public async Task HalRegistrationInventory_IsStableAndNonEmpty()
    {
        var inventory = HalDescriptors()
            .Select(descriptor => $"{Describe(descriptor.ServiceType)} => {DescribeImplementation(descriptor)} [{descriptor.Lifetime}]")
            .Order(StringComparer.Ordinal)
            .ToList();

        Console.WriteLine($"HAL registration inventory ({inventory.Count} entries):");
        foreach (var entry in inventory)
        {
            Console.WriteLine($"  {entry}");
        }

        await Assert.That(inventory).IsNotEmpty();
    }

    private static bool IsHalContract(Type serviceType) =>
        IsClosedGeneric(serviceType, typeof(ILinkPolicy<>))
        || IsClosedGeneric(serviceType, typeof(ICollectionLinkPolicy<>))
        || IsClosedGeneric(serviceType, typeof(IResourceAssembler<,>));

    /// <summary>Concrete policy self-registrations exist so one instance can back several contracts.</summary>
    private static bool IsHalPolicyImplementation(Type serviceType) =>
        !serviceType.IsInterface
        && serviceType.Namespace?.StartsWith("Explore.API.Hateoas", StringComparison.Ordinal) == true;

    private static bool IsClosedGeneric(Type type, Type openGeneric) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == openGeneric;

    private static string Describe(Type type) => type.IsGenericType
        ? $"{type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)]}<{string.Join(", ", type.GetGenericArguments().Select(argument => argument.Name))}>"
        : type.Name;

    private static string DescribeImplementation(ServiceDescriptor descriptor) => descriptor switch
    {
        { ImplementationType: not null } => descriptor.ImplementationType.Name,
        { ImplementationInstance: not null } => $"instance:{descriptor.ImplementationInstance.GetType().Name}",
        { ImplementationFactory: not null } => "factory",
        _ => "unknown",
    };

    private static void Report(IReadOnlyCollection<string> failures)
    {
        if (failures.Count == 0)
        {
            return;
        }

        Console.WriteLine($"HAL registration graph failures ({failures.Count}):");
        foreach (var failure in failures)
        {
            Console.WriteLine($"  - {failure}");
        }
    }
}
