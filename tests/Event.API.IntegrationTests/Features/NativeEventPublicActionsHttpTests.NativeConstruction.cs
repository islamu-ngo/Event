using System.Collections;
using System.Reflection;
using Explore.Application;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Operations;
using Explore.Domain.Constants;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventPublicActionsHttpTests
{
    [Test]
    [Timeout(600_000)]
    public async Task NativePartitionConstructsEveryImplementationUnderItsOriginalHumanOwner()
    {
        await using var fixture = await ProjectionRoomFixture.CreateAsync();
        var assembly = typeof(ApplicationServicesRegistration).Assembly;
        const BindingFlags members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var catalogType = assembly.GetType("Explore.Application.Operations.NativeOperationCatalog", throwOnError: true)!;
        var guardType = assembly.GetType("Explore.Application.Operations.OperationConstructionGuard", throwOnError: true)!;
        var policyType = assembly.GetType("Explore.Application.Operations.PolicyMutationCommandScope", throwOnError: true)!;
        var moderationType = assembly.GetType("Explore.Application.Operations.ModerationMutationCommandScope", throwOnError: true)!;
        var handlesPolicy = policyType.GetMethod("Handles", BindingFlags.Static | BindingFlags.NonPublic)!;
        var handlesModeration = moderationType.GetMethod("Handles", BindingFlags.Static | BindingFlags.NonPublic)!;
        var selectPolicy = policyType.GetMethod("SelectAsync", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<Func<object, IServiceProvider, CancellationToken, ValueTask<PolicyMutationScope?>>>();
        var enter = guardType.GetMethod("Enter", members)!;
        var exit = guardType.GetMethod("Exit", members)!;
        using var discovery = fixture.CreateScope();
        var catalog = discovery.ServiceProvider.GetRequiredService(catalogType);
        var entries = ((IEnumerable)catalogType.GetProperty("Registrations", members)!.GetValue(catalog)!)
            .Cast<object>().Select(entry =>
            {
                var type = entry.GetType();
                return (
                    Contract: (Type)type.GetProperty("Contract", members)!.GetValue(entry)!,
                    Implementation: (Type)type.GetProperty("Implementation", members)!.GetValue(entry)!,
                    Constructors: (ParameterInfo[][])type.GetProperty("Constructors", members)!.GetValue(entry)!);
            }).ToArray();
        await Assert.That(entries).IsNotEmpty();
        var constructed = new HashSet<Type>();
        foreach (var entry in entries)
        {
            Console.WriteLine($"NATIVE_CONSTRUCTION_ENTRY={entry.Contract.FullName}");
            using var request = fixture.CreateScope();
            await fixture.AuthenticateAsync(request);
            var requestType = entry.Contract.GetGenericArguments()[0];
            var dependencies = entry.Constructors.SelectMany(parameters => parameters)
                .Select(parameter => parameter.ParameterType).ToArray();
            PolicyMutationScope? selection = null;
            if ((bool)handlesPolicy.Invoke(null, [requestType])!)
            {
                object data = ConstructionRequest(requestType);
                selection = await selectPolicy(
                    data, request.ServiceProvider, TestContext.Current!.Execution.CancellationToken);
            }
            else if ((bool)handlesModeration.Invoke(null, [requestType])!
                     || dependencies.Contains(typeof(IInstanceAdministratorCommitAuthority)))
            {
                selection = PolicyMutationScope.Instance;
            }
            else if (dependencies.Contains(typeof(ITenantAdministratorCommitAuthority)))
            {
                selection = new PolicyMutationScope(PlatformDefaults.DefaultTenantId);
            }

            IPolicyMutationExecutionScope? selected = null;
            try
            {
                if (selection is not null)
                    selected = await request.ServiceProvider.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
                        .CreateAsync(selection, TestContext.Current!.Execution.CancellationToken);
                var services = selected?.Services ?? request.ServiceProvider;
                await services.GetRequiredService<IHumanCommitScopePreparation>()
                    .PrepareAsync(TestContext.Current!.Execution.CancellationToken);
                _ = services.GetRequiredService(entry.Contract);
                var guard = services.GetRequiredService(guardType);
                enter.Invoke(guard, [entry.Contract]);
                try
                {
                    _ = services.GetRequiredService(entry.Implementation);
                }
                finally
                {
                    exit.Invoke(guard, [entry.Contract]);
                }
                constructed.Add(entry.Contract);
            }
            finally
            {
                if (selected is not null)
                    await selected.DisposeAsync();
            }
        }
        await Assert.That(constructed.SetEquals(entries.Select(entry => entry.Contract))).IsTrue();
        Console.WriteLine($"NATIVE_CONSTRUCTION_COUNT={constructed.Count}/{entries.Length}");

        object ConstructionRequest(Type type)
        {
            var constructor = type.GetConstructors().OrderBy(candidate => candidate.GetParameters().Length).First();
            object data = constructor.Invoke(constructor.GetParameters().Select(parameter =>
                ConstructionValue(parameter.ParameterType, parameter.Name!)).ToArray());
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .Where(property => property.SetMethod?.IsPublic == true && property.GetIndexParameters().Length == 0))
                property.SetValue(data, ConstructionValue(property.PropertyType, property.Name));
            return data;
        }

        object? ConstructionValue(Type type, string name)
        {
            Type? nullable = Nullable.GetUnderlyingType(type);
            if (nullable is not null)
                return name.Equals("TenantId", StringComparison.OrdinalIgnoreCase) && nullable == typeof(Guid)
                    ? PlatformDefaults.DefaultTenantId : null;
            if (type == typeof(string))
                return "composition.only";
            if (type == typeof(Guid))
                return name.Equals("TenantId", StringComparison.OrdinalIgnoreCase)
                    ? PlatformDefaults.DefaultTenantId : Guid.CreateVersion7();
            if (type.IsEnum)
                return Enum.GetNames(type).Contains("Instance", StringComparer.Ordinal)
                    ? Enum.Parse(type, "Instance") : Enum.GetValues(type).GetValue(0);
            if (type.IsGenericType)
            {
                var definition = type.GetGenericTypeDefinition();
                if (definition == typeof(Dictionary<,>) || definition == typeof(IDictionary<,>)
                    || definition == typeof(IReadOnlyDictionary<,>))
                    return Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(type.GetGenericArguments()));
                if (definition == typeof(IEnumerable<>) || definition == typeof(IReadOnlyList<>)
                    || definition == typeof(IReadOnlyCollection<>))
                    return Array.CreateInstance(type.GetGenericArguments()[0], 0);
            }
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }
    }
}
