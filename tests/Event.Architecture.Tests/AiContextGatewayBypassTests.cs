using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Explore.API;

using Explore.Application.Features.AiAssistant.Disclosure;
using Explore.Domain;
using NetArchTest.Rules;
using TUnit.Core;
using static Event.Architecture.Tests.AiContextGatewayBypassTests;

namespace Event.Architecture.Tests;

public partial class AiContextGatewayBypassTests
{
    private static readonly Assembly DomainAssembly = typeof(Explore.Domain.Event).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(Explore.Application.ApplicationServicesRegistration).Assembly;
    private static readonly Assembly ApiAssembly = typeof(Program).Assembly;

    private static readonly HashSet<string> PiiEntityTypeNames =
    [
        nameof(UserPii),
        nameof(OrganizationPii),
        nameof(ActorPii),
        nameof(LocationPii),
    ];

    [Test]
    public async Task AiContextGateway_MustHaveAtLeastOneImplementation()
    {
        var implementations = Types.InAssembly(ApplicationAssembly)
            .That().ImplementInterface(typeof(IAiContextGateway))
            .GetTypes();

        var errors = new List<string>();
        if (!implementations.Any())
        {
            errors.Add("No implementation of IAiContextGateway found in Explore.Application assembly.");
        }

        await Assert.That(errors).IsEmpty().Because(string.Join("\n", errors));
    }

    [Test]
    public async Task AiAssistantFeatureTypes_ShouldNotReference_PiiEntitiesDirectly()
    {
        var violations = ScanConstructorParameterTypes(
            ApplicationAssembly,
            namespacePrefix: "Explore.Application.Features.AiAssistant");

        var errors = new List<string>();
        foreach (var (type, parameterType) in violations)
        {
            if (IsPiiEntityReference(parameterType))
            {
                errors.Add(
                    $"{type.FullName} has constructor parameter of PII type " +
                    $"{parameterType.Name}. Route through {nameof(IAiContextGateway)} instead.");
            }
        }

        await Assert.That(errors).IsEmpty().Because(string.Join("\n", errors));
    }

    [Test]
    public async Task McpTypes_ShouldNotReference_PiiEntitiesDirectly()
    {
        var violations = ScanConstructorParameterTypes(
            ApiAssembly,
            namespacePrefix: "Explore.API.Mcp");

        var errors = new List<string>();
        foreach (var (type, parameterType) in violations)
        {
            if (IsPiiEntityReference(parameterType))
            {
                errors.Add(
                    $"{type.FullName} has constructor parameter of PII type " +
                    $"{parameterType.Name}. MCP surfaces must emit only sanitized descriptors.");
            }
        }

        await Assert.That(errors).IsEmpty().Because(string.Join("\n", errors));
    }

    [Test]
    public async Task AiContextGateway_ContractSurface_MustNotChangeUnexpectedly()
    {
        var gatewayMethods = typeof(IAiContextGateway)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name)
            .OrderBy(n => n)
            .ToList();

        var expected = new List<string> { nameof(IAiContextGateway.Sanitize), nameof(IAiContextGateway.SanitizeMany) }
            .OrderBy(n => n)
            .ToList();

        var errors = new List<string>();
        if (!gatewayMethods.SequenceEqual(expected))
        {
            errors.Add(
                "IAiContextGateway surface drift: expected [Sanitize, SanitizeMany], " +
                $"found [{string.Join(", ", gatewayMethods)}].");
        }

        await Assert.That(errors).IsEmpty().Because(string.Join("\n", errors));
    }

    private static List<(Type Type, Type ParameterType)> ScanConstructorParameterTypes(
        Assembly assembly,
        string namespacePrefix)
    {
        var matches = new List<(Type, Type)>();
        foreach (var type in assembly.GetTypes())
        {
            if (type.Namespace is null ||
                !type.Namespace.StartsWith(namespacePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            {
                foreach (var parameter in constructor.GetParameters())
                {
                    matches.Add((type, parameter.ParameterType));
                }
            }
        }

        return matches;
    }

    private static bool IsPiiEntityReference(Type type)
    {
        var name = type.IsGenericType ? type.GetGenericTypeDefinition().Name : type.Name;
        var elementName = type.IsArray && type.HasElementType
            ? type.GetElementType()!.Name
            : name;

        if (PiiEntityTypeNames.Contains(elementName))
        {
            return true;
        }

        if (type.Namespace is null)
        {
            return false;
        }

        if (type.Namespace.StartsWith("Explore.Application.Contracts.Persistence", StringComparison.Ordinal))
        {
            return PiiEntityTypeNames.Any(pii =>
                type.Name.Contains(pii) || (type.IsGenericType && type.GenericTypeArguments.Any(a => a.Name == pii)));
        }

        return false;
    }
}
