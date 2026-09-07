using System.Reflection;
using System.Runtime.CompilerServices;
using Explore.Application.Authorization;
using Explore.Application.Features.EventSessions.Requests.Commands;
using MediatR;

namespace Event.Application.UnitTests.Contracts;

public sealed class PublicSetterRequestContractTests
{
    private static readonly string[] LifecycleRequiredSetters =
    [
        "Explore.Application.Features.EventSessions.Requests.Commands.ArchiveEventSessionCommand.Id",
        "Explore.Application.Features.EventSessions.Requests.Commands.ArchiveEventSessionCommand.Request",
        "Explore.Application.Features.EventSessions.Requests.Commands.CancelEventSessionCommand.Id",
        "Explore.Application.Features.EventSessions.Requests.Commands.CancelEventSessionCommand.Request",
        "Explore.Application.Features.EventSessions.Requests.Commands.CompleteEventSessionCommand.Id",
        "Explore.Application.Features.EventSessions.Requests.Commands.CompleteEventSessionCommand.Request",
    ];

    [Test]
    public async Task CompiledRequestPublicSettersAreExactlyLifecycleRequiredMembers()
    {
        var applicationAssembly = typeof(AuthorizeResourceAttribute).Assembly;
        var actual = applicationAssembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false }
                && typeof(IBaseRequest).IsAssignableFrom(type))
            .SelectMany(type => type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(IsPublicMutableProperty)
                .Select(property => $"{property.DeclaringType!.FullName}.{property.Name}"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(actual).IsEquivalentTo(LifecycleRequiredSetters);
    }

    [Test]
    public async Task EveryRetainedPublicSetterIsRequiredByLifecycleInterface()
    {
        var lifecycleProperties = typeof(IEventSessionLifecycleTransitionCommand)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.SetMethod?.IsPublic == true)
            .ToDictionary(property => property.Name, property => property.PropertyType, StringComparer.Ordinal);

        var retainedProperties = new[]
        {
            typeof(ArchiveEventSessionCommand),
            typeof(CancelEventSessionCommand),
            typeof(CompleteEventSessionCommand),
        }
        .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        .Where(IsPublicMutableProperty)
        .ToArray();

        await Assert.That(lifecycleProperties.Keys.Order(StringComparer.Ordinal))
            .IsEquivalentTo(new[] { "Id", "Request" });
        await Assert.That(retainedProperties).Count().IsEqualTo(6);
        await Assert.That(retainedProperties.All(property =>
            lifecycleProperties.TryGetValue(property.Name, out var propertyType)
            && propertyType == property.PropertyType)).IsTrue();
    }

    private static bool IsPublicMutableProperty(PropertyInfo property) =>
        property.SetMethod?.IsPublic == true
        && !property.SetMethod.ReturnParameter
            .GetRequiredCustomModifiers()
            .Contains(typeof(IsExternalInit));
}
