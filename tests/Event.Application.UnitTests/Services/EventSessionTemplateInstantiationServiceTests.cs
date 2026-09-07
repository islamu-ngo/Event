using Explore.Application.Contracts.Services;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Application.UnitTests.Services;

public class EventSessionTemplateInstantiationServiceTests
{
    private readonly EventSessionTemplateInstantiationService _service = new();
    private readonly Guid _eventSessionId = Guid.NewGuid();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _userId = Guid.NewGuid().ToString();

    private EventSessionTemplate CreateSessionTemplate(
        string sessionTemplateKey = "workshop-standard",
        int version = 1,
        params EventSessionTemplateCustomPropertyDefinition[] definitions)
    {
        return new EventSessionTemplate
        {
            Id = Guid.NewGuid(),
            EventTemplateId = Guid.NewGuid(),
            TenantId = _tenantId,
            SessionTemplateKey = sessionTemplateKey,
            DisplayName = "Test Session Template",
            Version = version,
            IsPublished = true,
            IsActive = true
        };
    }

    private EventSessionTemplateCustomPropertyDefinition CreateTemplateDef(
        Guid? id = null,
        string ns = "tenant.community",
        string key = "notes",
        PropertyType type = PropertyType.Text,
        params EventSessionTemplateCustomPropertyOption[] options)
    {
        return new EventSessionTemplateCustomPropertyDefinition
        {
            Id = id ?? Guid.NewGuid(),
            EventSessionTemplateId = Guid.NewGuid(),
            TenantId = _tenantId,
            Namespace = ns,
            Key = key,
            DisplayName = $"Test {key}",
            PropertyType = type,
            IsActive = true,
            ExposureLevel = ExposureLevel.Public,
            SortOrder = 0
        };
    }

    private static EventSessionTemplateCustomPropertyOption CreateTemplateOption(
        Guid defId,
        string ns = "tenant.community",
        string key = "option1",
        string value = "opt1",
        bool isDefault = false,
        Guid? parentOptionId = null)
    {
        return new EventSessionTemplateCustomPropertyOption
        {
            Id = Guid.NewGuid(),
            EventSessionTemplateCustomPropertyDefinitionId = defId,
            Namespace = ns,
            Key = key,
            DisplayName = $"Option {key}",
            Value = value,
            IsDefault = isDefault,
            IsActive = true,
            SortOrder = 0,
            ParentOptionId = parentOptionId
        };
    }

    #region InstantiateFromSessionTemplate

    [Test]
    public async Task InstantiateFromSessionTemplate_WithEmptyTemplate_ReturnsEmptyDefinitions()
    {
        var sessionTemplate = CreateSessionTemplate();

        var result = _service.InstantiateFromSessionTemplate(_eventSessionId, _tenantId, sessionTemplate, _userId);

        await Assert.That(result.Definitions).IsNotNull();
        await Assert.That(result.Definitions.Count).IsEqualTo(0);
    }

    [Test]
    public async Task InstantiateFromSessionTemplate_CopiesDefinitionFields()
    {
        var templateDef = CreateTemplateDef(
            ns: "Tenant.Community",
            key: "Prayer Notes",
            type: PropertyType.Text);
        templateDef.IsRequired = true;
        templateDef.IsMulti = false;
        templateDef.IsSearchable = true;
        templateDef.IsFilterable = true;
        templateDef.Description = "A test description";
        templateDef.MinLength = 5;
        templateDef.MaxLength = 500;

        var sessionTemplate = CreateSessionTemplate();
        var defsField = typeof(EventSessionTemplate).GetField("_definitions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var defsList = (List<EventSessionTemplateCustomPropertyDefinition>)defsField!.GetValue(sessionTemplate)!;
        defsList.Add(templateDef);

        var result = _service.InstantiateFromSessionTemplate(_eventSessionId, _tenantId, sessionTemplate, _userId);

        await Assert.That(result.Definitions.Count).IsEqualTo(1);
        var runtimeDef = result.Definitions[0].Definition;

        await Assert.That(runtimeDef.EventSessionId).IsEqualTo(_eventSessionId);
        await Assert.That(runtimeDef.TenantId).IsEqualTo(_tenantId);
        await Assert.That(runtimeDef.Namespace).IsEqualTo("tenant.community");
        await Assert.That(runtimeDef.Key).IsEqualTo("prayer_notes");
        await Assert.That(runtimeDef.PropertyType).IsEqualTo(PropertyType.Text);
        await Assert.That(runtimeDef.IsRequired).IsTrue();
        await Assert.That(runtimeDef.IsMulti).IsFalse();
        await Assert.That(runtimeDef.IsSearchable).IsTrue();
        await Assert.That(runtimeDef.IsFilterable).IsTrue();
        await Assert.That(runtimeDef.Description).IsEqualTo("A test description");
        await Assert.That(runtimeDef.MinLength).IsEqualTo(5);
        await Assert.That(runtimeDef.MaxLength).IsEqualTo(500);
    }

    [Test]
    public async Task InstantiateFromSessionTemplate_SetsProvenanceFields()
    {
        var templateDef = CreateTemplateDef();
        var sessionTemplate = CreateSessionTemplate(version: 3);
        var defsField = typeof(EventSessionTemplate).GetField("_definitions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        ((List<EventSessionTemplateCustomPropertyDefinition>)defsField!.GetValue(sessionTemplate)!).Add(templateDef);

        var result = _service.InstantiateFromSessionTemplate(_eventSessionId, _tenantId, sessionTemplate, _userId);
        var runtimeDef = result.Definitions[0].Definition;

        await Assert.That(runtimeDef.SourceTemplateId).IsEqualTo(sessionTemplate.Id);
        await Assert.That(runtimeDef.SourceTemplateKey).IsEqualTo(sessionTemplate.SessionTemplateKey);
        await Assert.That(runtimeDef.SourceTemplateVersion).IsEqualTo(3);
        await Assert.That(runtimeDef.SourceTemplateDefinitionId).IsEqualTo(templateDef.Id);
        await Assert.That(runtimeDef.InstantiatedAt).IsNotEqualTo(default(DateTimeOffset));
    }

    [Test]
    public async Task InstantiateFromSessionTemplate_SetsAuditFields()
    {
        var templateDef = CreateTemplateDef();
        var sessionTemplate = CreateSessionTemplate();
        var defsField = typeof(EventSessionTemplate).GetField("_definitions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        ((List<EventSessionTemplateCustomPropertyDefinition>)defsField!.GetValue(sessionTemplate)!).Add(templateDef);

        var userId = Guid.NewGuid();
        var result = _service.InstantiateFromSessionTemplate(_eventSessionId, _tenantId, sessionTemplate, userId.ToString());
        var runtimeDef = result.Definitions[0].Definition;

        await Assert.That(runtimeDef.CreatedBy).IsEqualTo(userId);
        await Assert.That(runtimeDef.UpdatedBy).IsEqualTo(userId);
        await Assert.That(runtimeDef.CreatedAt).IsNotEqualTo(default(DateTime));
    }

    [Test]
    public async Task InstantiateFromSessionTemplate_CopiesOptionsWithNewIds()
    {
        var templateDef = CreateTemplateDef(type: PropertyType.Option);
        var opt1 = CreateTemplateOption(templateDef.Id, key: "opt_a", value: "A");
        var opt2 = CreateTemplateOption(templateDef.Id, key: "opt_b", value: "B");
        var optsField = typeof(EventSessionTemplateCustomPropertyDefinition).GetField("_options", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var optsList = (List<EventSessionTemplateCustomPropertyOption>)optsField!.GetValue(templateDef)!;
        optsList.AddRange([opt1, opt2]);

        var sessionTemplate = CreateSessionTemplate();
        var defsField = typeof(EventSessionTemplate).GetField("_definitions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        ((List<EventSessionTemplateCustomPropertyDefinition>)defsField!.GetValue(sessionTemplate)!).Add(templateDef);

        var result = _service.InstantiateFromSessionTemplate(_eventSessionId, _tenantId, sessionTemplate, _userId);
        var runtimeOptions = result.Definitions[0].Options;

        await Assert.That(runtimeOptions.Count).IsEqualTo(2);

        // IDs should be new (not matching template option IDs)
        await Assert.That(runtimeOptions.All(o => o.Id != opt1.Id && o.Id != opt2.Id)).IsTrue();

        // Values should be preserved
        var values = runtimeOptions.Select(o => o.Value).OrderBy(v => v).ToList();
        await Assert.That(values[0]).IsEqualTo("A");
        await Assert.That(values[1]).IsEqualTo("B");
    }

    [Test]
    public async Task InstantiateFromSessionTemplate_SetsOptionProvenance()
    {
        var templateDef = CreateTemplateDef(type: PropertyType.Option);
        var opt = CreateTemplateOption(templateDef.Id);
        var optsField = typeof(EventSessionTemplateCustomPropertyDefinition).GetField("_options", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        ((List<EventSessionTemplateCustomPropertyOption>)optsField!.GetValue(templateDef)!).Add(opt);

        var sessionTemplate = CreateSessionTemplate(version: 2);
        var defsField = typeof(EventSessionTemplate).GetField("_definitions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        ((List<EventSessionTemplateCustomPropertyDefinition>)defsField!.GetValue(sessionTemplate)!).Add(templateDef);

        var result = _service.InstantiateFromSessionTemplate(_eventSessionId, _tenantId, sessionTemplate, _userId);
        var runtimeOpt = result.Definitions[0].Options[0];

        await Assert.That(runtimeOpt.SourceTemplateOptionId).IsEqualTo(opt.Id);
        await Assert.That(runtimeOpt.SourceTemplateVersion).IsEqualTo(2);
    }

    [Test]
    public async Task InstantiateFromSessionTemplate_RemapsDefaultOptionId()
    {
        var templateDef = CreateTemplateDef(type: PropertyType.Option);
        var opt = CreateTemplateOption(templateDef.Id, isDefault: true);
        var optsField = typeof(EventSessionTemplateCustomPropertyDefinition).GetField("_options", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        ((List<EventSessionTemplateCustomPropertyOption>)optsField!.GetValue(templateDef)!).Add(opt);
        templateDef.DefaultOptionId = opt.Id;

        var sessionTemplate = CreateSessionTemplate();
        var defsField = typeof(EventSessionTemplate).GetField("_definitions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        ((List<EventSessionTemplateCustomPropertyDefinition>)defsField!.GetValue(sessionTemplate)!).Add(templateDef);

        var result = _service.InstantiateFromSessionTemplate(_eventSessionId, _tenantId, sessionTemplate, _userId);
        var runtimeDefaultId = result.Definitions[0].DefaultOptionId;
        var runtimeOptId = result.Definitions[0].Options[0].Id;

        // DefaultOptionId should map to the new runtime option ID
        await Assert.That(runtimeDefaultId).IsEqualTo(runtimeOptId);
        // And should NOT be the original template option ID
        await Assert.That(runtimeDefaultId).IsNotEqualTo(opt.Id);
    }

    [Test]
    public async Task InstantiateFromSessionTemplate_RemapsParentOptionId()
    {
        var templateDef = CreateTemplateDef(type: PropertyType.Option);
        var parentOpt = CreateTemplateOption(templateDef.Id, key: "parent");
        var childOpt = CreateTemplateOption(templateDef.Id, key: "child", parentOptionId: parentOpt.Id);
        var optsField = typeof(EventSessionTemplateCustomPropertyDefinition).GetField("_options", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var optsList = (List<EventSessionTemplateCustomPropertyOption>)optsField!.GetValue(templateDef)!;
        optsList.AddRange([parentOpt, childOpt]);

        var sessionTemplate = CreateSessionTemplate();
        var defsField = typeof(EventSessionTemplate).GetField("_definitions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        ((List<EventSessionTemplateCustomPropertyDefinition>)defsField!.GetValue(sessionTemplate)!).Add(templateDef);

        var result = _service.InstantiateFromSessionTemplate(_eventSessionId, _tenantId, sessionTemplate, _userId);
        var runtimeOptions = result.Definitions[0].Options;
        var runtimeParent = runtimeOptions.First(o => o.Key == "parent");
        var runtimeChild = runtimeOptions.First(o => o.Key == "child");

        // Child's ParentOptionId should point to the new runtime parent ID
        await Assert.That(runtimeChild.ParentOptionId).IsEqualTo(runtimeParent.Id);
        // And NOT to the old template parent ID
        await Assert.That(runtimeChild.ParentOptionId).IsNotEqualTo(parentOpt.Id);
    }

    [Test]
    public async Task InstantiateFromSessionTemplate_CreatesDefaultValueForText()
    {
        var templateDef = CreateTemplateDef(type: PropertyType.Text);
        templateDef.DefaultTextValue = "Hello World";

        var sessionTemplate = CreateSessionTemplate();
        var defsField = typeof(EventSessionTemplate).GetField("_definitions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        ((List<EventSessionTemplateCustomPropertyDefinition>)defsField!.GetValue(sessionTemplate)!).Add(templateDef);

        var result = _service.InstantiateFromSessionTemplate(_eventSessionId, _tenantId, sessionTemplate, _userId);
        var defaultValue = result.Definitions[0].DefaultValue;

        await Assert.That(defaultValue).IsNotNull();
        await Assert.That(defaultValue!.TextValue).IsEqualTo("Hello World");
        await Assert.That(defaultValue.EventSessionId).IsEqualTo(_eventSessionId);
        await Assert.That(defaultValue.TenantId).IsEqualTo(_tenantId);
        await Assert.That(defaultValue.Ordinal).IsEqualTo(0);
    }

    [Test]
    public async Task InstantiateFromSessionTemplate_CreatesDefaultValueForNumber()
    {
        var templateDef = CreateTemplateDef(type: PropertyType.Number);
        templateDef.DefaultNumberValue = 42.5m;

        var sessionTemplate = CreateSessionTemplate();
        var defsField = typeof(EventSessionTemplate).GetField("_definitions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        ((List<EventSessionTemplateCustomPropertyDefinition>)defsField!.GetValue(sessionTemplate)!).Add(templateDef);

        var result = _service.InstantiateFromSessionTemplate(_eventSessionId, _tenantId, sessionTemplate, _userId);
        var defaultValue = result.Definitions[0].DefaultValue;

        await Assert.That(defaultValue).IsNotNull();
        await Assert.That(defaultValue!.NumberValue).IsEqualTo(42.5m);
    }

    [Test]
    public async Task InstantiateFromSessionTemplate_NoDefaultValue_WhenNoDefaultsSet()
    {
        var templateDef = CreateTemplateDef(type: PropertyType.Text);
        // No default values set

        var sessionTemplate = CreateSessionTemplate();
        var defsField = typeof(EventSessionTemplate).GetField("_definitions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        ((List<EventSessionTemplateCustomPropertyDefinition>)defsField!.GetValue(sessionTemplate)!).Add(templateDef);

        var result = _service.InstantiateFromSessionTemplate(_eventSessionId, _tenantId, sessionTemplate, _userId);

        await Assert.That(result.Definitions[0].DefaultValue).IsNull();
    }

    [Test]
    public async Task InstantiateFromSessionTemplate_MultipleDefinitions_AllCopied()
    {
        var def1 = CreateTemplateDef(ns: "tenant.community", key: "notes");
        var def2 = CreateTemplateDef(ns: "tenant.community", key: "capacity");
        def2.PropertyType = PropertyType.Number;

        var sessionTemplate = CreateSessionTemplate();
        var defsField = typeof(EventSessionTemplate).GetField("_definitions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var defsList = (List<EventSessionTemplateCustomPropertyDefinition>)defsField!.GetValue(sessionTemplate)!;
        defsList.AddRange([def1, def2]);

        var result = _service.InstantiateFromSessionTemplate(_eventSessionId, _tenantId, sessionTemplate, _userId);

        await Assert.That(result.Definitions.Count).IsEqualTo(2);
        // Each should have unique IDs
        var ids = result.Definitions.Select(d => d.Definition.Id).Distinct().Count();
        await Assert.That(ids).IsEqualTo(2);
    }

    [Test]
    public async Task InstantiateFromSessionTemplate_NormalizesNamespaceAndKey()
    {
        var templateDef = CreateTemplateDef(ns: "Tenant.Community", key: "Prayer Notes");

        var sessionTemplate = CreateSessionTemplate();
        var defsField = typeof(EventSessionTemplate).GetField("_definitions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        ((List<EventSessionTemplateCustomPropertyDefinition>)defsField!.GetValue(sessionTemplate)!).Add(templateDef);

        var result = _service.InstantiateFromSessionTemplate(_eventSessionId, _tenantId, sessionTemplate, _userId);
        var runtimeDef = result.Definitions[0].Definition;

        await Assert.That(runtimeDef.Namespace).IsEqualTo("tenant.community");
        await Assert.That(runtimeDef.Key).IsEqualTo("prayer_notes");
    }

    #endregion

    #region MatchByProvenance

    [Test]
    public async Task MatchByProvenance_WithSourceIdMatch_ReturnsSourceIdMatchType()
    {
        var templateDefId = Guid.NewGuid();
        var existing = new EventSessionCustomPropertyDefinition
        {
            Id = Guid.NewGuid(),
            EventSessionId = _eventSessionId,
            TenantId = _tenantId,
            Namespace = "tenant.community",
            Key = "notes",
            DisplayName = "Notes",
            PropertyType = PropertyType.Text,
            SourceTemplateDefinitionId = templateDefId,
            InstantiatedAt = DateTimeOffset.UtcNow
        };

        var templateDef = CreateTemplateDef(id: templateDefId, ns: "tenant.community", key: "notes");

        var matches = _service.MatchByProvenance([existing], [templateDef]);

        await Assert.That(matches.Count).IsEqualTo(1);
        await Assert.That(matches[0].MatchType).IsEqualTo(ProvenanceMatchType.SourceId);
        await Assert.That(matches[0].ExistingDefinition).IsEqualTo(existing);
        await Assert.That(matches[0].TemplateDefinition).IsEqualTo(templateDef);
    }

    [Test]
    public async Task MatchByProvenance_WithNamespaceKeyFallback_ReturnsNamespaceKeyMatchType()
    {
        var existing = new EventSessionCustomPropertyDefinition
        {
            Id = Guid.NewGuid(),
            EventSessionId = _eventSessionId,
            TenantId = _tenantId,
            Namespace = "tenant.community",
            Key = "notes",
            DisplayName = "Notes",
            PropertyType = PropertyType.Text,
            SourceTemplateDefinitionId = null, // No source ID
            InstantiatedAt = DateTimeOffset.UtcNow
        };

        var templateDef = CreateTemplateDef(ns: "Tenant.Community", key: "Notes");

        var matches = _service.MatchByProvenance([existing], [templateDef]);

        await Assert.That(matches.Count).IsEqualTo(1);
        await Assert.That(matches[0].MatchType).IsEqualTo(ProvenanceMatchType.NamespaceKey);
    }

    [Test]
    public async Task MatchByProvenance_SourceIdPrioritizedOverNamespaceKey()
    {
        var templateDefId = Guid.NewGuid();

        // Existing def has source ID AND matching namespace/key
        var existing = new EventSessionCustomPropertyDefinition
        {
            Id = Guid.NewGuid(),
            EventSessionId = _eventSessionId,
            TenantId = _tenantId,
            Namespace = "tenant.community",
            Key = "notes",
            DisplayName = "Notes",
            PropertyType = PropertyType.Text,
            SourceTemplateDefinitionId = templateDefId,
            InstantiatedAt = DateTimeOffset.UtcNow
        };

        var templateDef = CreateTemplateDef(id: templateDefId, ns: "tenant.community", key: "notes");

        var matches = _service.MatchByProvenance([existing], [templateDef]);

        // Should match by SourceId, not NamespaceKey
        await Assert.That(matches.Count).IsEqualTo(1);
        await Assert.That(matches[0].MatchType).IsEqualTo(ProvenanceMatchType.SourceId);
    }

    [Test]
    public async Task MatchByProvenance_NoMatch_ReturnsEmptyList()
    {
        var existing = new EventSessionCustomPropertyDefinition
        {
            Id = Guid.NewGuid(),
            EventSessionId = _eventSessionId,
            TenantId = _tenantId,
            Namespace = "tenant.community",
            Key = "notes",
            DisplayName = "Notes",
            PropertyType = PropertyType.Text,
            SourceTemplateDefinitionId = null,
            InstantiatedAt = DateTimeOffset.UtcNow
        };

        var templateDef = CreateTemplateDef(ns: "tenant.other", key: "different_key");

        var matches = _service.MatchByProvenance([existing], [templateDef]);

        await Assert.That(matches.Count).IsEqualTo(0);
    }

    [Test]
    public async Task MatchByProvenance_MixedMatches_SourceIdAndNamespaceKey()
    {
        var templateDefId1 = Guid.NewGuid();

        // First existing: matches by source ID
        var existing1 = new EventSessionCustomPropertyDefinition
        {
            Id = Guid.NewGuid(),
            EventSessionId = _eventSessionId,
            TenantId = _tenantId,
            Namespace = "tenant.community",
            Key = "notes",
            DisplayName = "Notes",
            PropertyType = PropertyType.Text,
            SourceTemplateDefinitionId = templateDefId1,
            InstantiatedAt = DateTimeOffset.UtcNow
        };

        // Second existing: matches by namespace+key (no source ID)
        var existing2 = new EventSessionCustomPropertyDefinition
        {
            Id = Guid.NewGuid(),
            EventSessionId = _eventSessionId,
            TenantId = _tenantId,
            Namespace = "tenant.community",
            Key = "capacity",
            DisplayName = "Capacity",
            PropertyType = PropertyType.Number,
            SourceTemplateDefinitionId = null,
            InstantiatedAt = DateTimeOffset.UtcNow
        };

        var templateDef1 = CreateTemplateDef(id: templateDefId1, ns: "tenant.community", key: "notes");
        var templateDef2 = CreateTemplateDef(ns: "tenant.community", key: "capacity");
        templateDef2.PropertyType = PropertyType.Number;

        var matches = _service.MatchByProvenance([existing1, existing2], [templateDef1, templateDef2]);

        await Assert.That(matches.Count).IsEqualTo(2);

        var sourceIdMatch = matches.First(m => m.MatchType == ProvenanceMatchType.SourceId);
        await Assert.That(sourceIdMatch.ExistingDefinition).IsEqualTo(existing1);

        var nsKeyMatch = matches.First(m => m.MatchType == ProvenanceMatchType.NamespaceKey);
        await Assert.That(nsKeyMatch.ExistingDefinition).IsEqualTo(existing2);
    }

    [Test]
    public async Task MatchByProvenance_DoesNotDoubleMatchTemplateDefinitions()
    {
        var templateDefId = Guid.NewGuid();

        // Two existing defs pointing to the same template def by source ID
        var existing1 = new EventSessionCustomPropertyDefinition
        {
            Id = Guid.NewGuid(),
            EventSessionId = _eventSessionId,
            TenantId = _tenantId,
            Namespace = "tenant.community",
            Key = "notes",
            DisplayName = "Notes",
            PropertyType = PropertyType.Text,
            SourceTemplateDefinitionId = templateDefId,
            InstantiatedAt = DateTimeOffset.UtcNow
        };

        var existing2 = new EventSessionCustomPropertyDefinition
        {
            Id = Guid.NewGuid(),
            EventSessionId = _eventSessionId,
            TenantId = _tenantId,
            Namespace = "tenant.community",
            Key = "notes",
            DisplayName = "Notes duplicate",
            PropertyType = PropertyType.Text,
            SourceTemplateDefinitionId = templateDefId,
            InstantiatedAt = DateTimeOffset.UtcNow
        };

        var templateDef = CreateTemplateDef(id: templateDefId, ns: "tenant.community", key: "notes");

        var matches = _service.MatchByProvenance([existing1, existing2], [templateDef]);

        // Only the first match should be recorded (template def already consumed)
        await Assert.That(matches.Count).IsEqualTo(1);
    }

    #endregion
}
