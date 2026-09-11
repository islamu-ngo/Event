using System.Text.Json;
using Explore.Application.Mappings;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Application.UnitTests.Profiles;

public sealed class CustomPropertyMapperTests
{
    private static readonly Guid DefinitionId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000011");
    private static readonly Guid OptionId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000012");

    [Test]
    public async Task Category_ProjectsOnlyParentNameAndPreservesMissingParent()
    {
        var parent = new Category { MasterCode = "PARENT", FullName = "Parent", Tenant = null! };
        parent.Parent = parent;
        var source = new Category
        {
            Id = DefinitionId, ConcurrencyStamp = OptionId, MasterCode = "LECTURE", FullName = "Lecture",
            ParentId = OptionId, Parent = parent, TenantId = DefinitionId, Tenant = null!
        };
        var detail = CustomPropertyMapper.ToDetail(source);
        var list = CustomPropertyMapper.ToListItem(source);
        parent.FullName = "Changed";
        source.Parent = null;
        await Assert.That(detail.ParentFullName).IsEqualTo("Parent");
        await Assert.That(list.ParentFullName).IsEqualTo("Parent");
        await Assert.That(detail.ConcurrencyStamp).IsEqualTo(OptionId);
        await Assert.That(detail.MasterCode).IsEqualTo("LECTURE");
        await Assert.That(detail.TenantId).IsEqualTo(DefinitionId);
        await Assert.That(CustomPropertyMapper.ToDetail(source).ParentFullName).IsNull();
        await Assert.That(CustomPropertyMapper.ToListItem(source).ParentFullName).IsNull();
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(list));
        await Assert.That(json.RootElement.TryGetProperty("TenantId", out _)).IsFalse();
        await Assert.That(json.RootElement.TryGetProperty("Parent", out _)).IsFalse();
    }

    [Test]
    public async Task GenericDefinition_PreservesValidationFlagsAndEmptyOptions()
    {
        var source = new CustomPropertyDefinition
        {
            Id = DefinitionId, ConcurrencyStamp = OptionId, TenantId = DefinitionId,
            Namespace = "tenant.community", Key = "notes", DisplayName = "Notes", Description = "",
            PropertyType = PropertyType.Text, ExposureLevel = ExposureLevel.OrganizerOnly,
            IsRequired = true, IsMulti = true, IsActive = true, SortOrder = 9,
            IsSearchable = true, IsFilterable = true, IsExportable = true, IsModerationRelevant = true,
            IsAnalyticsRelevant = true, IsSystemOwned = true, DefaultTextValue = "default",
            MinLength = 2, MaxLength = 40, RegexPattern = "^[a-z]+$", AllowedUrlSchemes = "https",
            MinNumber = -3m, MaxNumber = 12m,
            MinDateTime = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            MaxDateTime = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero)
        };
        var detail = CustomPropertyMapper.ToDetail(source);
        var list = CustomPropertyMapper.ToListItem(source);
        await Assert.That(detail.Options.Count).IsEqualTo(0);
        await Assert.That(list.OptionCount).IsEqualTo(0);
        await Assert.That(detail.Description).IsEqualTo("");
        await Assert.That(detail.DefaultTextValue).IsEqualTo("default");
        await Assert.That(detail.MinLength).IsEqualTo(2);
        await Assert.That(detail.MaxLength).IsEqualTo(40);
        await Assert.That(detail.RegexPattern).IsEqualTo("^[a-z]+$");
        await Assert.That(detail.AllowedUrlSchemes).IsEqualTo("https");
        await Assert.That(detail.MinNumber).IsEqualTo(-3m);
        await Assert.That(detail.MaxNumber).IsEqualTo(12m);
        await Assert.That(detail.MinDateTime).IsEqualTo(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));
        await Assert.That(detail.MaxDateTime).IsEqualTo(new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero));
        await Assert.That(detail.DefaultOptionId).IsNull();
        await Assert.That(list.IsRequired && list.IsMulti && list.IsActive && list.IsSearchable && list.IsFilterable
            && list.IsExportable && list.IsModerationRelevant && list.IsAnalyticsRelevant && list.IsSystemOwned).IsTrue();
        await Assert.That(list.SortOrder).IsEqualTo(9);
        await Assert.That(detail.ConcurrencyStamp).IsEqualTo(OptionId);
        await Assert.That(detail.PropertyType).IsEqualTo(PropertyType.Text);
        await Assert.That(detail.ExposureLevel).IsEqualTo(ExposureLevel.OrganizerOnly);
    }

    [Test]
    public async Task Values_PreserveEveryPayloadKindAndDoNotDiscloseTenantOrAudit()
    {
        var time = new DateTimeOffset(2026, 9, 11, 9, 0, 0, TimeSpan.FromHours(2));
        var eventValue = CustomPropertyMapper.ToValue(new EventCustomPropertyValue
        {
            Id = OptionId, EventCustomPropertyDefinitionId = DefinitionId, EventId = OptionId, Ordinal = 3,
            TextValue = "", NumberValue = 12.50m, BooleanValue = false, DateTimeValue = time, OptionId = DefinitionId,
            TenantId = DefinitionId, CreatedBy = DefinitionId
        });
        var sessionValue = CustomPropertyMapper.ToValue(new EventSessionCustomPropertyValue
        {
            Id = OptionId, EventSessionCustomPropertyDefinitionId = DefinitionId, EventSessionId = OptionId, Ordinal = 3,
            TextValue = "", NumberValue = 12.50m, BooleanValue = false, DateTimeValue = time, OptionId = DefinitionId,
            TenantId = DefinitionId, CreatedBy = DefinitionId
        });
        await Assert.That(eventValue).IsEqualTo(new Explore.Application.DTOs.EventCustomProperty.EventCustomPropertyValueDto
        {
            Id = OptionId, EventCustomPropertyDefinitionId = DefinitionId, EventId = OptionId, Ordinal = 3,
            TextValue = "", NumberValue = 12.50m, BooleanValue = false,
            DateTimeValue = new DateTimeOffset(2026, 9, 11, 9, 0, 0, TimeSpan.FromHours(2)), OptionId = DefinitionId
        });
        await Assert.That(sessionValue).IsEqualTo(new Explore.Application.DTOs.EventSessionCustomProperty.EventSessionCustomPropertyValueDto
        {
            Id = OptionId, EventSessionCustomPropertyDefinitionId = DefinitionId, EventSessionId = OptionId, Ordinal = 3,
            TextValue = "", NumberValue = 12.50m, BooleanValue = false,
            DateTimeValue = new DateTimeOffset(2026, 9, 11, 9, 0, 0, TimeSpan.FromHours(2)), OptionId = DefinitionId
        });
        await Assert.That(CustomPropertyMapper.ToValue(new EventCustomPropertyValue()).TextValue).IsNull();
        await Assert.That(CustomPropertyMapper.ToValue(new EventSessionCustomPropertyValue()).NumberValue).IsNull();
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(eventValue));
        await Assert.That(json.RootElement.TryGetProperty("TenantId", out _)).IsFalse();
        await Assert.That(json.RootElement.TryGetProperty("CreatedBy", out _)).IsFalse();
    }

    [Test]
    public async Task EventDefinition_SnapshotsOptionsAndBoundsCyclicNavigation()
    {
        var definition = new EventCustomPropertyDefinition
        {
            Id = DefinitionId, Namespace = "tenant.community", Key = "language", DisplayName = "Language",
            PropertyType = PropertyType.Option, ExposureLevel = ExposureLevel.OrganizerOnly,
            DefaultOptionId = OptionId, SourceTemplateVersion = 7, IsMulti = true,
            CreatedBy = Guid.NewGuid(), IsDeleted = true
        };
        var option = new EventCustomPropertyOption
        {
            Id = OptionId, EventCustomPropertyDefinitionId = DefinitionId, Definition = definition,
            Namespace = "tenant.community", Key = "ar", DisplayName = "Arabic", Value = "ar", IsActive = true,
            SourceTemplateVersion = 4
        };
        option.ParentOption = option;
        definition.AddOption(option);
        definition.DefaultOption = option;
        var detail = CustomPropertyMapper.ToDetail(definition);
        var list = CustomPropertyMapper.ToListItem(definition);
        option.DisplayName = "Changed";
        definition.AddOption(new EventCustomPropertyOption
        {
            EventCustomPropertyDefinitionId = DefinitionId,
            Namespace = "tenant.community", Key = "en", DisplayName = "English", Value = "en"
        });

        await Assert.That(detail.Options.Count).IsEqualTo(1);
        await Assert.That(detail.Options[0].DisplayName).IsEqualTo("Arabic");
        await Assert.That(detail.DefaultOptionId).IsEqualTo(OptionId);
        await Assert.That(detail.SourceTemplateVersion).IsEqualTo(7);
        await Assert.That(detail.IsMulti).IsTrue();
        await Assert.That(list.OptionCount).IsEqualTo(1);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(detail));
        await Assert.That(json.RootElement.TryGetProperty("CreatedBy", out _)).IsFalse();
        await Assert.That(json.RootElement.TryGetProperty("Values", out _)).IsFalse();
        await Assert.That(json.RootElement.GetProperty("Options")[0].TryGetProperty("Definition", out _)).IsFalse();
        await Assert.That(json.RootElement.GetProperty("Options")[0].TryGetProperty("SourceTemplateVersion", out _)).IsFalse();
    }

    [Test]
    public async Task SessionDefinition_SnapshotsOptionsAndPreservesNullableDefaults()
    {
        var definition = new EventSessionCustomPropertyDefinition
        {
            Id = DefinitionId, Namespace = "tenant.community", Key = "language", DisplayName = "Language",
            PropertyType = PropertyType.Option, DefaultTextValue = "", DefaultNumberValue = 0,
            DefaultBooleanValue = false, DefaultOptionId = OptionId
        };
        var option = new EventSessionCustomPropertyOption
        {
            Id = OptionId, EventSessionCustomPropertyDefinitionId = DefinitionId, Definition = definition,
            Namespace = "tenant.community", Key = "ar", DisplayName = "Arabic", Value = "ar"
        };
        definition.AddOption(option);
        var detail = CustomPropertyMapper.ToDetail(definition);
        var list = CustomPropertyMapper.ToListItem(definition);
        option.Value = "changed";

        await Assert.That(detail.Options.Single().Value).IsEqualTo("ar");
        await Assert.That(detail.DefaultTextValue).IsEqualTo("");
        await Assert.That(detail.DefaultNumberValue).IsEqualTo(0m);
        await Assert.That(detail.DefaultBooleanValue).IsEqualTo(false);
        await Assert.That(detail.DefaultDateTimeValue).IsNull();
        await Assert.That(detail.SourceTemplateId).IsNull();
        await Assert.That(list.OptionCount).IsEqualTo(1);
    }

    [Test]
    public async Task EventTemplate_SnapshotsNestedDefinitionsAndOptions()
    {
        var template = new EventTemplate
        {
            TemplateKey = "conference", DisplayName = "Conference", Version = 3,
            CreatedAt = new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc)
        };
        var definition = new EventTemplateCustomPropertyDefinition
        {
            Id = DefinitionId, EventTemplate = template,
            Namespace = "tenant.community", Key = "language", DisplayName = "Language"
        };
        var option = new EventTemplateCustomPropertyOption
        {
            Id = OptionId, Definition = definition, Namespace = "tenant.community", Key = "ar", DisplayName = "Arabic", Value = "ar"
        };
        definition.ReplaceOptions([option]);
        template.ReplaceDefinitions([definition]);
        var detail = CustomPropertyMapper.ToDetail(template);
        var list = CustomPropertyMapper.ToListItem(template);
        definition.DisplayName = "Changed";
        option.Value = "changed";
        template.ReplaceDefinitions([]);

        await Assert.That(detail.Definitions.Single().DisplayName).IsEqualTo("Language");
        await Assert.That(detail.Definitions.Single().Options.Single().Value).IsEqualTo("ar");
        await Assert.That(list.DefinitionCount).IsEqualTo(1);
        await Assert.That(detail.Version).IsEqualTo(3);
        await Assert.That(detail.UpdatedAt).IsNull();
        await Assert.That(detail.CreatedBy).IsNull();
        await Assert.That(detail.UpdatedBy).IsNull();
    }

    [Test]
    public async Task SessionTemplate_SnapshotsNestedDefinitionsAndAuditConversion()
    {
        var author = Guid.Parse("018e4e5c-7f00-7000-8000-000000000013");
        var template = new EventSessionTemplate
        {
            SessionTemplateKey = "talk", DisplayName = "Talk", CreatedBy = author,
            CreatedAt = new DateTime(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc)
        };
        var definition = new EventSessionTemplateCustomPropertyDefinition
        {
            Id = DefinitionId, EventSessionTemplate = template,
            Namespace = "tenant.community", Key = "language", DisplayName = "Language"
        };
        definition.ReplaceOptions([new EventSessionTemplateCustomPropertyOption
        {
            Id = OptionId, Definition = definition, Namespace = "tenant.community", Key = "ar", DisplayName = "Arabic", Value = "ar"
        }]);
        template.ReplaceDefinitions([definition]);
        var detail = CustomPropertyMapper.ToDetail(template);
        var list = CustomPropertyMapper.ToListItem(template);
        definition.ReplaceOptions([]);
        template.ReplaceDefinitions([]);

        await Assert.That(detail.Definitions.Single().Options.Single().Value).IsEqualTo("ar");
        await Assert.That(list.DefinitionCount).IsEqualTo(1);
        await Assert.That(detail.CreatedBy).IsEqualTo("018e4e5c-7f00-7000-8000-000000000013");
        await Assert.That(detail.CreatedAt).IsEqualTo(new DateTimeOffset(2026, 9, 11, 10, 0, 0, TimeSpan.Zero));
    }
}
