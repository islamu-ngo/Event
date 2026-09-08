using System.Text.Json;
using System.Collections;
using System.Reflection;
using Explore.Application.DTOs.CustomPropertyDefinition;
using Explore.Application.DTOs.CustomPropertyProjection;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.EventCustomProperty;
using Explore.Application.DTOs.EventSessionCustomProperty;
using Explore.Application.DTOs.EventSessionTemplate;
using Explore.Application.DTOs.EventTemplate;

namespace Event.Application.UnitTests.DTOs;

public sealed class PublishedCollectionSnapshotTests
{
    [Test]
    public async Task CustomPropertyDefinitionCollections_SnapshotAssignedLists()
    {
        var createOptions = new List<CreateCustomPropertyOptionDto> { CustomPropertyOption() };
        var responseOptions = new List<CustomPropertyOptionDto>
        {
            new()
            {
                Namespace = "tenant.test",
                Key = "option",
                DisplayName = "Option",
                Value = "option"
            }
        };
        var patchItems = new List<CreateCustomPropertyOptionDto> { CustomPropertyOption() };
        var create = new CreateCustomPropertyDefinitionDto
        {
            Namespace = "tenant.test",
            Key = "shared",
            DisplayName = "Shared",
            Options = createOptions
        };
        var response = new CustomPropertyDefinitionDto
        {
            Namespace = "tenant.test",
            Key = "shared",
            DisplayName = "Shared",
            Options = responseOptions
        };
        var patch = new UpdateCustomPropertyDefinitionOptionsDto { Items = patchItems };

        createOptions.Clear();
        responseOptions.Clear();
        patchItems.Clear();

        await AssertSnapshot(create.Options);
        await AssertSnapshot(response.Options);
        await AssertSnapshot(patch.Items!);
    }

    [Test]
    public async Task CustomPropertyProjectionCollections_SnapshotAssignedLists()
    {
        var optionIds = new List<Guid> { Guid.NewGuid() };
        var criterion = new CustomPropertyFilterCriterion
        {
            Namespace = "tenant.test",
            Key = "projection",
            OptionIds = optionIds
        };

        optionIds.Clear();

        await AssertSnapshot(criterion.OptionIds!);
    }

    [Test]
    public async Task EventCustomPropertyCollections_SnapshotAssignedLists()
    {
        var createOptions = new List<CreateEventCustomPropertyOptionDto> { new() };
        var responseOptions = new List<EventCustomPropertyOptionDto> { new() };
        var patchItems = new List<CreateEventCustomPropertyOptionDto> { new() };
        var values = new List<SetEventCustomPropertyValueDto> { new() };
        var create = new CreateEventCustomPropertyDefinitionDto { Options = createOptions };
        var response = new EventCustomPropertyDefinitionDto { Options = responseOptions };
        var patch = new UpdateEventCustomPropertyDefinitionOptionsDto { Items = patchItems };
        var multiValue = new SetEventCustomPropertyMultiValuesDto { Values = values };

        createOptions.Clear();
        responseOptions.Clear();
        patchItems.Clear();
        values.Clear();

        await AssertSnapshot(create.Options);
        await AssertSnapshot(response.Options);
        await AssertSnapshot(patch.Items!);
        await AssertSnapshot(multiValue.Values);
    }

    [Test]
    public async Task EventSessionCustomPropertyCollections_SnapshotAssignedLists()
    {
        var createOptions = new List<CreateEventSessionCustomPropertyOptionDto> { new() };
        var responseOptions = new List<EventSessionCustomPropertyOptionDto> { new() };
        var patchItems = new List<CreateEventSessionCustomPropertyOptionDto> { new() };
        var values = new List<SetEventSessionCustomPropertyValueDto> { new() };
        var create = new CreateEventSessionCustomPropertyDefinitionDto { Options = createOptions };
        var response = new EventSessionCustomPropertyDefinitionDto { Options = responseOptions };
        var patch = new UpdateEventSessionCustomPropertyDefinitionOptionsDto { Items = patchItems };
        var multiValue = new SetEventSessionCustomPropertyMultiValuesDto { Values = values };

        createOptions.Clear();
        responseOptions.Clear();
        patchItems.Clear();
        values.Clear();

        await AssertSnapshot(create.Options);
        await AssertSnapshot(response.Options);
        await AssertSnapshot(patch.Items!);
        await AssertSnapshot(multiValue.Values);
    }

    [Test]
    public async Task EventTemplateCollections_SnapshotAssignedLists()
    {
        var createOptions = new List<CreateEventTemplateOptionDto> { new() };
        var responseOptions = new List<EventTemplateOptionDto> { new() };
        var createDefinitions = new List<CreateEventTemplateDefinitionDto> { new() };
        var responseDefinitions = new List<EventTemplateDefinitionDto> { new() };
        var patchItems = new List<CreateEventTemplateDefinitionDto> { new() };
        var createDefinition = new CreateEventTemplateDefinitionDto { Options = createOptions };
        var responseDefinition = new EventTemplateDefinitionDto { Options = responseOptions };
        var create = new CreateEventTemplateDto { Definitions = createDefinitions };
        var response = new EventTemplateDto { Definitions = responseDefinitions };
        var patch = new UpdateEventTemplateDefinitionsDto { Items = patchItems };

        createOptions.Clear();
        responseOptions.Clear();
        createDefinitions.Clear();
        responseDefinitions.Clear();
        patchItems.Clear();

        await AssertSnapshot(createDefinition.Options);
        await AssertSnapshot(responseDefinition.Options);
        await AssertSnapshot(create.Definitions);
        await AssertSnapshot(response.Definitions);
        await AssertSnapshot(patch.Items!);
    }

    [Test]
    public async Task EventSessionTemplateCollections_SnapshotAssignedLists()
    {
        var createOptions = new List<CreateEventSessionTemplateOptionDto> { new() };
        var responseOptions = new List<EventSessionTemplateOptionDto> { new() };
        var createDefinitions = new List<CreateEventSessionTemplateDefinitionDto> { new() };
        var responseDefinitions = new List<EventSessionTemplateDefinitionDto> { new() };
        var patchItems = new List<CreateEventSessionTemplateDefinitionDto> { new() };
        var createDefinition = new CreateEventSessionTemplateDefinitionDto { Options = createOptions };
        var responseDefinition = new EventSessionTemplateDefinitionDto { Options = responseOptions };
        var create = new CreateEventSessionTemplateDto { Definitions = createDefinitions };
        var response = new EventSessionTemplateDto { Definitions = responseDefinitions };
        var patch = new UpdateEventSessionTemplateDefinitionsDto { Items = patchItems };

        createOptions.Clear();
        responseOptions.Clear();
        createDefinitions.Clear();
        responseDefinitions.Clear();
        patchItems.Clear();

        await AssertSnapshot(createDefinition.Options);
        await AssertSnapshot(responseDefinition.Options);
        await AssertSnapshot(create.Definitions);
        await AssertSnapshot(response.Definitions);
        await AssertSnapshot(patch.Items!);
    }

    [Test]
    public async Task NullablePatchCollection_PreservesOmittedNullAndArrayJsonSemantics()
    {
        var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var omitted = JsonSerializer.Deserialize<UpdateEventTemplateDefinitionsDto>("{}", serializerOptions);
        var explicitNull = JsonSerializer.Deserialize<UpdateEventTemplateDefinitionsDto>("{\"items\":null}", serializerOptions);
        var populated = JsonSerializer.Deserialize<UpdateEventTemplateDefinitionsDto>("{\"items\":[{}]}", serializerOptions);

        await Assert.That(omitted!.Items).IsNull();
        await Assert.That(explicitNull!.Items).IsNull();
        await Assert.That(populated!.Items!.Count).IsEqualTo(1);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(populated, serializerOptions));
        await Assert.That(document.RootElement.TryGetProperty("items", out var items)).IsTrue();
        await Assert.That(items.GetArrayLength()).IsEqualTo(1);
    }

    private static CreateCustomPropertyOptionDto CustomPropertyOption() => new()
    {
        Namespace = "tenant.test",
        Key = "option",
        DisplayName = "Option",
        Value = "option"
    };

    private static async Task AssertSnapshot<T>(IReadOnlyList<T> snapshot)
    {
        await Assert.That(snapshot.Count).IsEqualTo(1);
        var mutableView = (IList<T>)snapshot;
        await Assert.That(() => mutableView.Add(snapshot[0])).Throws<NotSupportedException>();
    }
}

public sealed class EventFamilyPublishedCollectionSnapshotTests
{
    private static readonly HashSet<string> OwnedNamespaces = new(StringComparer.Ordinal)
    {
        "Explore.Application.DTOs.Agenda",
        "Explore.Application.DTOs.CategoryType",
        "Explore.Application.DTOs.Event",
        "Explore.Application.DTOs.EventAggregateView",
        "Explore.Application.DTOs.EventProgram",
        "Explore.Application.DTOs.EventSeries",
        "Explore.Application.DTOs.EventSession",
    };

    [Test]
    public async Task CollectionInitAccessorsCopyEveryCallerOwnedInput()
    {
        var failures = new List<string>();
        PropertyInfo[] properties = typeof(CreateEventDto).Assembly
            .GetTypes()
            .Where(type => type.IsPublic && type.Namespace is not null && OwnedNamespaces.Contains(type.Namespace))
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Where(property => IsReadOnlyList(property.PropertyType) || IsReadOnlyDictionary(property.PropertyType))
            .OrderBy(property => property.DeclaringType!.FullName, StringComparer.Ordinal)
            .ThenBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();

        foreach (PropertyInfo property in properties)
        {
            object owner = Activator.CreateInstance(property.DeclaringType!)!;
            ICollection source = CreateMutableSource(property.PropertyType);
            AddValue(source, property.PropertyType, 1);
            property.SetValue(owner, source);

            object? published = property.GetValue(owner);
            int originalCount = Count(published);
            AddValue(source, property.PropertyType, 2);

            if (Count(published) != originalCount)
                failures.Add($"{property.DeclaringType!.FullName}.{property.Name}");
        }

        await Assert.That(properties).IsNotEmpty();
        await Assert.That(failures).IsEmpty();
    }

    private static bool IsReadOnlyList(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>);

    private static bool IsReadOnlyDictionary(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>);

    private static ICollection CreateMutableSource(Type publishedType)
    {
        Type[] arguments = publishedType.GetGenericArguments();
        Type mutableType = IsReadOnlyList(publishedType)
            ? typeof(List<>).MakeGenericType(arguments)
            : typeof(Dictionary<,>).MakeGenericType(arguments);
        return (ICollection)Activator.CreateInstance(mutableType)!;
    }

    private static void AddValue(ICollection source, Type publishedType, int ordinal)
    {
        Type[] arguments = publishedType.GetGenericArguments();
        if (source is IList list)
        {
            list.Add(CreateValue(arguments[0], ordinal));
            return;
        }

        var dictionary = (IDictionary)source;
        dictionary.Add(CreateValue(arguments[0], ordinal), CreateValue(arguments[1], ordinal));
    }

    private static object? CreateValue(Type type, int ordinal)
    {
        if (type == typeof(string))
            return $"value-{ordinal}";
        if (type == typeof(Guid))
            return Guid.CreateVersion7();
        if (type == typeof(int))
            return ordinal;

        return Activator.CreateInstance(type);
    }

    private static int Count(object? collection) =>
        collection is null ? -1 : ((IEnumerable)collection).Cast<object?>().Count();
}
