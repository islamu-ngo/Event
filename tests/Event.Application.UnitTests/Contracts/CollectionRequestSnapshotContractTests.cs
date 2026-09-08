using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Explore.Application.Features.Events.Requests.Queries;

namespace Event.Application.UnitTests.Contracts;

public sealed class CollectionRequestSnapshotContractTests
{
    private static readonly string[] ExpectedRequestTypes =
    [
        "Explore.Application.Features.EventCustomProperties.Requests.Commands.SetEventCustomPropertyMultiValuesCommand",
        "Explore.Application.Features.EventReporting.Requests.Queries.GetModerationReportQueueRequest",
        "Explore.Application.Features.EventSessionCustomProperties.Requests.Commands.SetEventSessionCustomPropertyMultiValuesCommand",
        "Explore.Application.Features.EventSessions.Requests.Queries.GetEventSessionListRequest",
        "Explore.Application.Features.Events.Requests.Queries.GetEventListRequest",
        "Explore.Application.Features.Footer.Requests.Commands.ReorderFooterLinkGroupsCommand",
        "Explore.Application.Features.Notifications.Requests.Commands.UpdateCurrentUserNotificationPreferenceMatrixCommand",
        "Explore.Application.Features.Notifications.Requests.Commands.UpdateGroupNotificationPreferenceMatrixCommand",
        "Explore.Application.Features.Notifications.Requests.Commands.UpdateOrganizationNotificationPreferenceMatrixCommand",
        "Explore.Application.Features.Permissions.Requests.Queries.GetAssignablePermissionsRequest",
        "Explore.Application.Features.RegistrationOrders.Requests.Commands.CreateRegistrationOrderWithHoldCommand",
        "Explore.Application.Features.Roles.Requests.Commands.CreateCustomRoleCommand",
        "Explore.Application.Features.Roles.Requests.Commands.UpdateRolePermissionsCommand",
        "Explore.Application.Features.Settings.Requests.Commands.UpdateSettingBatchCommand",
        "Explore.Application.Features.Settings.Requests.Queries.ResolveSettingGroupQuery",
        "Explore.Application.Features.TenantOnboarding.Requests.Commands.SaveTenantOnboardingStepCommand",
        "Explore.Application.Features.Tenants.Requests.Commands.ReorderTenantNavLinks.ReorderTenantNavLinksCommand",
        "Explore.Application.Features.Webhooks.Requests.Commands.CreateWebhookEndpointCommand"
    ];

    [Test]
    public async Task CollectionProperties_DefensivelySnapshotMutableCallerInputs()
    {
        CollectionContract[] contracts = GetContracts();

        string[] actualRequestTypes = contracts
            .Select(contract => contract.RequestType.FullName!)
            .Distinct()
            .ToArray();
        if (!ExpectedRequestTypes.SequenceEqual(actualRequestTypes))
        {
            throw new InvalidOperationException("The collection-bearing request inventory does not match the expected sequence.");
        }

        await Assert.That(contracts.Length).IsEqualTo(37);

        foreach (CollectionContract contract in contracts)
        {
            object request = RuntimeHelpers.GetUninitializedObject(contract.RequestType);
            MutableInput input = CreateMutableInput(contract.Property.PropertyType);
            contract.Property.SetValue(request, input.Value);
            object? stored = contract.Property.GetValue(request);
            object?[] expected = Snapshot(stored);

            input.Mutate();

            object?[] actual = Snapshot(contract.Property.GetValue(request));
            if (!expected.SequenceEqual(actual))
            {
                throw new InvalidOperationException(
                    $"{contract.RequestType.FullName}.{contract.Property.Name} retained a mutable caller alias.");
            }

            AssertReadOnly(stored, contract);
        }
    }

    [Test]
    public async Task CollectionProperties_PreserveSystemTextJsonBindingAndSequences()
    {
        CollectionContract[] contracts = GetContracts();

        foreach (IGrouping<Type, CollectionContract> requestContracts in contracts.GroupBy(contract => contract.RequestType))
        {
            object request = RuntimeHelpers.GetUninitializedObject(requestContracts.Key);
            foreach (CollectionContract contract in requestContracts)
            {
                contract.Property.SetValue(request, CreateMutableInput(contract.Property.PropertyType).Value);
            }

            string json = JsonSerializer.Serialize(request, requestContracts.Key);
            object rebound = JsonSerializer.Deserialize(json, requestContracts.Key)
                ?? throw new InvalidOperationException($"JSON binding returned null for {requestContracts.Key.FullName}.");

            foreach (CollectionContract contract in requestContracts)
            {
                object?[] expected = Snapshot(contract.Property.GetValue(request));
                object?[] actual = Snapshot(contract.Property.GetValue(rebound));
                if (!expected.SequenceEqual(actual))
                {
                    throw new InvalidOperationException(
                        $"{contract.RequestType.FullName}.{contract.Property.Name} changed sequence during JSON binding.");
                }
            }
        }

        await Assert.That(contracts.Select(contract => contract.RequestType).Distinct().Count()).IsEqualTo(18);
        await Assert.That(contracts.Length).IsEqualTo(37);
    }

    private static CollectionContract[] GetContracts()
    {
        Assembly applicationAssembly = typeof(GetEventListRequest).Assembly;
        var expected = ExpectedRequestTypes.ToHashSet(StringComparer.Ordinal);

        return applicationAssembly.GetTypes()
            .Where(type => type.FullName is not null && expected.Contains(type.FullName))
            .SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.PropertyType != typeof(string)
                    && typeof(IEnumerable).IsAssignableFrom(property.PropertyType))
                .Select(property => new CollectionContract(type, property)))
            .OrderBy(contract => contract.RequestType.FullName, StringComparer.Ordinal)
            .ThenBy(contract => contract.Property.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static MutableInput CreateMutableInput(Type contractType)
    {
        if (contractType.IsArray)
        {
            Array array = Array.CreateInstance(contractType.GetElementType()!, 1);
            array.SetValue("before", 0);
            return new MutableInput(array, () => array.SetValue("after", 0));
        }

        Type[] genericArguments = contractType.GetGenericArguments();
        if (genericArguments.Length == 2)
        {
            Type dictionaryType = typeof(Dictionary<,>).MakeGenericType(genericArguments);
            var dictionary = (IDictionary)Activator.CreateInstance(dictionaryType)!;
            dictionary.Add("alpha", "one");
            return new MutableInput(dictionary, () => dictionary.Add("beta", "two"));
        }

        Type elementType = genericArguments[0];
        if (contractType.IsGenericType && contractType.GetGenericTypeDefinition() == typeof(IReadOnlySet<>))
        {
            object set = Activator.CreateInstance(typeof(HashSet<>).MakeGenericType(elementType))!;
            MethodInfo add = set.GetType().GetMethod("Add")!;
            add.Invoke(set, ["alpha"]);
            return new MutableInput(set, () => add.Invoke(set, ["beta"]));
        }

        IList list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType))!;
        list.Add(DefaultValue(elementType));
        return new MutableInput(list, () => list.Add(DefaultValue(elementType)));
    }

    private static object? DefaultValue(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;

    private static object?[] Snapshot(object? collection) => collection switch
    {
        null => [],
        IDictionary dictionary => SnapshotDictionary(dictionary),
        IEnumerable enumerable => enumerable.Cast<object?>().ToArray(),
        _ => throw new InvalidOperationException($"{collection.GetType()} is not enumerable.")
    };

    private static object?[] SnapshotDictionary(IDictionary dictionary)
    {
        var entries = new List<string>();
        foreach (DictionaryEntry entry in dictionary)
        {
            entries.Add($"{entry.Key}={entry.Value}");
        }

        return entries.Order(StringComparer.Ordinal).Cast<object?>().ToArray();
    }

    private static void AssertReadOnly(object? stored, CollectionContract contract)
    {
        bool readOnly = stored switch
        {
            IList list => list.IsReadOnly,
            IDictionary dictionary => dictionary.IsReadOnly,
            _ => IsGenericCollectionReadOnly(stored)
        };

        if (!readOnly)
        {
            throw new InvalidOperationException(
                $"{contract.RequestType.FullName}.{contract.Property.Name} does not expose read-only storage.");
        }
    }

    private static bool IsGenericCollectionReadOnly(object? stored)
    {
        if (stored is null)
        {
            return true;
        }

        Type collectionInterface = stored.GetType().GetInterfaces()
            .Single(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ICollection<>));
        return (bool)collectionInterface.GetProperty(nameof(ICollection<object>.IsReadOnly))!.GetValue(stored)!;
    }

    private sealed record CollectionContract(Type RequestType, PropertyInfo Property);

    private sealed record MutableInput(object Value, Action Mutate);
}
