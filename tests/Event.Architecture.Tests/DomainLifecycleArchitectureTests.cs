using System.Reflection;
using Explore.Application.Features.Events.Handlers.Commands;
using Explore.API.Controllers;
using DomainEvent = Explore.Domain.Event;

namespace Event.Architecture.Tests;

public sealed class DomainLifecycleArchitectureTests
{
    private static readonly string[] GenericMutationMethodNames = ["SetStatus", "RestoreStatus"];

    [Test]
    public async Task LifecycleStatusForeignKeysHavePrivateSetters()
    {
        PropertyInfo eventStatus = typeof(DomainEvent).GetProperty(nameof(DomainEvent.EventStatusId))!;
        PropertyInfo sessionStatus = typeof(Explore.Domain.EventSession)
            .GetProperty(nameof(Explore.Domain.EventSession.EventSessionStatusId))!;

        await Assert.That(eventStatus.GetSetMethod(nonPublic: true)?.IsPrivate).IsTrue();
        await Assert.That(sessionStatus.GetSetMethod(nonPublic: true)?.IsPrivate).IsTrue();
    }

    [Test]
    public async Task DomainEntitiesDoNotExposeGenericLifecycleStatusMutationMethods()
    {
        string[] violations = FindGenericMutationSeams(
            typeof(DomainEvent).Assembly,
            type => type.IsClass && type.Namespace == typeof(DomainEvent).Namespace);

        await Assert.That(violations).IsEmpty()
            .Because("lifecycle state changes must use explicit semantic domain operations");
    }

    [Test]
    public async Task ApplicationAndApiTypesDoNotExposeGenericLifecycleStatusMutationMethods()
    {
        string[] violations =
        [
            .. FindGenericMutationSeams(typeof(PublishEventCommandHandler).Assembly),
            .. FindGenericMutationSeams(typeof(EventController).Assembly)
        ];

        await Assert.That(violations).IsEmpty()
            .Because("production boundaries must expose explicit lifecycle commands rather than generic status mutation");
    }

    private static string[] FindGenericMutationSeams(Assembly assembly, Func<Type, bool>? typeFilter = null) =>
        assembly.GetTypes()
            .Where(type => typeFilter?.Invoke(type) ?? true)
            .SelectMany(type => type.GetMethods(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(method => GenericMutationMethodNames.Contains(method.Name, StringComparer.Ordinal))
                .Select(method => $"{type.FullName}.{method.Name}"))
            .Order(StringComparer.Ordinal)
            .ToArray();
}
