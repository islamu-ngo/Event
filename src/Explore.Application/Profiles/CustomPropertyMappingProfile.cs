using AutoMapper;
using Explore.Application.DTOs.Category;
using Explore.Application.DTOs.CustomPropertyDefinition;
using Explore.Application.DTOs.EventCustomProperty;
using Explore.Application.DTOs.EventSessionCustomProperty;
using Explore.Application.DTOs.EventSessionTemplate;
using Explore.Application.DTOs.EventTemplate;
using Explore.Domain;

namespace Explore.Application.Profiles;

public class CustomPropertyMappingProfile : Profile
{
    public CustomPropertyMappingProfile()
    {
        CreateMap<Category, CategoryDto>()
            .ForMember(dest => dest.ParentFullName, opt => opt.MapFrom(src => src.Parent != null ? src.Parent.FullName : null));
        CreateMap<Category, CategoryListDto>()
            .ForMember(dest => dest.ParentFullName, opt => opt.MapFrom(src => src.Parent != null ? src.Parent.FullName : null));
        CreateMap<CreateCategoryDto, Category>();

        CreateMap<CustomPropertyOption, CustomPropertyOptionDto>();
        CreateMap<CustomPropertyDefinition, CustomPropertyDefinitionDto>();
        CreateMap<CustomPropertyDefinition, CustomPropertyDefinitionListDto>()
            .ForMember(dest => dest.OptionCount, opt => opt.MapFrom(src => src.Options.Count));
        CreateMap<CreateCustomPropertyDefinitionDto, CustomPropertyDefinition>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.TenantId, opt => opt.Ignore())
            .ForMember(dest => dest.Tenant, opt => opt.Ignore())
            .ForMember(dest => dest.DefaultOptionId, opt => opt.Ignore())
            .ForMember(dest => dest.DefaultOption, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedBy, opt => opt.Ignore())
            .ForMember(dest => dest.IsDeleted, opt => opt.Ignore())
            .ForMember(dest => dest.DeletedAt, opt => opt.Ignore())
            .ForMember(dest => dest.DeletedBy, opt => opt.Ignore())
            .ForMember(dest => dest.Options, opt => opt.Ignore())
            .ForMember(dest => dest.Values, opt => opt.Ignore());

        CreateMap<EventTemplate, EventTemplateDto>();
        CreateMap<EventTemplate, EventTemplateListDto>()
            .ForMember(dest => dest.DefinitionCount, opt => opt.MapFrom(src => src.Definitions.Count));
        CreateMap<CreateEventTemplateDto, EventTemplate>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.TenantId, opt => opt.Ignore())
            .ForMember(dest => dest.Tenant, opt => opt.Ignore())
            .ForMember(dest => dest.EventType, opt => opt.Ignore())
            .ForMember(dest => dest.Definitions, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedBy, opt => opt.Ignore())
            .ForMember(dest => dest.IsDeleted, opt => opt.Ignore())
            .ForMember(dest => dest.DeletedAt, opt => opt.Ignore())
            .ForMember(dest => dest.DeletedBy, opt => opt.Ignore());

        CreateMap<EventTemplateCustomPropertyOption, EventTemplateOptionDto>();
        CreateMap<EventTemplateCustomPropertyDefinition, EventTemplateDefinitionDto>();
        CreateMap<EventTemplateCustomPropertyDefinition, EventTemplateDefinitionListDto>()
            .ForMember(dest => dest.OptionCount, opt => opt.MapFrom(src => src.Options.Count));
        CreateMap<CreateEventTemplateDefinitionDto, EventTemplateCustomPropertyDefinition>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.EventTemplateId, opt => opt.Ignore())
            .ForMember(dest => dest.TenantId, opt => opt.Ignore())
            .ForMember(dest => dest.Tenant, opt => opt.Ignore())
            .ForMember(dest => dest.EventTemplate, opt => opt.Ignore())
            .ForMember(dest => dest.DefaultOptionId, opt => opt.Ignore())
            .ForMember(dest => dest.DefaultOption, opt => opt.Ignore())
            .ForMember(dest => dest.Options, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedBy, opt => opt.Ignore())
            .ForMember(dest => dest.IsDeleted, opt => opt.Ignore())
            .ForMember(dest => dest.DeletedAt, opt => opt.Ignore())
            .ForMember(dest => dest.DeletedBy, opt => opt.Ignore());
        CreateMap<UpdateEventTemplateDefinitionDto, EventTemplateCustomPropertyDefinition>()
            .ForMember(dest => dest.EventTemplateId, opt => opt.Ignore())
            .ForMember(dest => dest.TenantId, opt => opt.Ignore())
            .ForMember(dest => dest.Tenant, opt => opt.Ignore())
            .ForMember(dest => dest.EventTemplate, opt => opt.Ignore())
            .ForMember(dest => dest.DefaultOptionId, opt => opt.Ignore())
            .ForMember(dest => dest.DefaultOption, opt => opt.Ignore())
            .ForMember(dest => dest.Options, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedBy, opt => opt.Ignore())
            .ForMember(dest => dest.IsDeleted, opt => opt.Ignore())
            .ForMember(dest => dest.DeletedAt, opt => opt.Ignore())
            .ForMember(dest => dest.DeletedBy, opt => opt.Ignore());

        CreateMap<EventCustomPropertyOption, EventCustomPropertyOptionDto>();
        CreateMap<EventCustomPropertyDefinition, EventCustomPropertyDefinitionDto>();
        CreateMap<EventCustomPropertyDefinition, EventCustomPropertyDefinitionListDto>()
            .ForMember(dest => dest.OptionCount, opt => opt.MapFrom(src => src.Options.Count));
        CreateMap<CreateEventCustomPropertyDefinitionDto, EventCustomPropertyDefinition>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.TenantId, opt => opt.Ignore())
            .ForMember(dest => dest.Tenant, opt => opt.Ignore())
            .ForMember(dest => dest.Event, opt => opt.Ignore())
            .ForMember(dest => dest.SourceTemplate, opt => opt.Ignore())
            .ForMember(dest => dest.SourceTemplateId, opt => opt.Ignore())
            .ForMember(dest => dest.SourceTemplateKey, opt => opt.Ignore())
            .ForMember(dest => dest.SourceTemplateVersion, opt => opt.Ignore())
            .ForMember(dest => dest.SourceTemplateDefinitionId, opt => opt.Ignore())
            .ForMember(dest => dest.InstantiatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.LastSyncedFromTemplateAt, opt => opt.Ignore())
            .ForMember(dest => dest.DefaultOptionId, opt => opt.Ignore())
            .ForMember(dest => dest.DefaultOption, opt => opt.Ignore())
            .ForMember(dest => dest.Options, opt => opt.Ignore())
            .ForMember(dest => dest.Values, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedBy, opt => opt.Ignore())
            .ForMember(dest => dest.IsDeleted, opt => opt.Ignore())
            .ForMember(dest => dest.DeletedAt, opt => opt.Ignore())
            .ForMember(dest => dest.DeletedBy, opt => opt.Ignore());

        CreateMap<EventCustomPropertyValue, EventCustomPropertyValueDto>();
        CreateMap<SetEventCustomPropertyValueDto, EventCustomPropertyValue>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.TenantId, opt => opt.Ignore())
            .ForMember(dest => dest.Tenant, opt => opt.Ignore())
            .ForMember(dest => dest.Definition, opt => opt.Ignore())
            .ForMember(dest => dest.Event, opt => opt.Ignore())
            .ForMember(dest => dest.Option, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedBy, opt => opt.Ignore())
            .ForMember(dest => dest.IsDeleted, opt => opt.Ignore())
            .ForMember(dest => dest.DeletedAt, opt => opt.Ignore())
            .ForMember(dest => dest.DeletedBy, opt => opt.Ignore());

        CreateMap<EventSessionTemplate, EventSessionTemplateDto>();
        CreateMap<EventSessionTemplate, EventSessionTemplateListDto>()
            .ForMember(dest => dest.DefinitionCount, opt => opt.MapFrom(src => src.Definitions.Count));
        CreateMap<CreateEventSessionTemplateDto, EventSessionTemplate>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.TenantId, opt => opt.Ignore())
            .ForMember(dest => dest.Tenant, opt => opt.Ignore())
            .ForMember(dest => dest.EventTemplate, opt => opt.Ignore())
            .ForMember(dest => dest.Definitions, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedBy, opt => opt.Ignore())
            .ForMember(dest => dest.IsDeleted, opt => opt.Ignore())
            .ForMember(dest => dest.DeletedAt, opt => opt.Ignore())
            .ForMember(dest => dest.DeletedBy, opt => opt.Ignore());

        CreateMap<EventSessionTemplateCustomPropertyOption, EventSessionTemplateOptionDto>();
        CreateMap<EventSessionTemplateCustomPropertyDefinition, EventSessionTemplateDefinitionDto>();
        CreateMap<EventSessionTemplateCustomPropertyDefinition, EventSessionTemplateDefinitionListDto>()
            .ForMember(dest => dest.OptionCount, opt => opt.MapFrom(src => src.Options.Count));
        CreateMap<CreateEventSessionTemplateDefinitionDto, EventSessionTemplateCustomPropertyDefinition>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.EventSessionTemplateId, opt => opt.Ignore())
            .ForMember(dest => dest.TenantId, opt => opt.Ignore())
            .ForMember(dest => dest.Tenant, opt => opt.Ignore())
            .ForMember(dest => dest.EventSessionTemplate, opt => opt.Ignore())
            .ForMember(dest => dest.DefaultOptionId, opt => opt.Ignore())
            .ForMember(dest => dest.DefaultOption, opt => opt.Ignore())
            .ForMember(dest => dest.Options, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedBy, opt => opt.Ignore())
            .ForMember(dest => dest.IsDeleted, opt => opt.Ignore())
            .ForMember(dest => dest.DeletedAt, opt => opt.Ignore())
            .ForMember(dest => dest.DeletedBy, opt => opt.Ignore());
        CreateMap<UpdateEventSessionTemplateDefinitionDto, EventSessionTemplateCustomPropertyDefinition>()
            .ForMember(dest => dest.EventSessionTemplateId, opt => opt.Ignore())
            .ForMember(dest => dest.TenantId, opt => opt.Ignore())
            .ForMember(dest => dest.Tenant, opt => opt.Ignore())
            .ForMember(dest => dest.EventSessionTemplate, opt => opt.Ignore())
            .ForMember(dest => dest.DefaultOptionId, opt => opt.Ignore())
            .ForMember(dest => dest.DefaultOption, opt => opt.Ignore())
            .ForMember(dest => dest.Options, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedBy, opt => opt.Ignore())
            .ForMember(dest => dest.IsDeleted, opt => opt.Ignore())
            .ForMember(dest => dest.DeletedAt, opt => opt.Ignore())
            .ForMember(dest => dest.DeletedBy, opt => opt.Ignore());

        CreateMap<EventSessionCustomPropertyOption, EventSessionCustomPropertyOptionDto>();
        CreateMap<EventSessionCustomPropertyDefinition, EventSessionCustomPropertyDefinitionDto>();
        CreateMap<EventSessionCustomPropertyDefinition, EventSessionCustomPropertyDefinitionListDto>()
            .ForMember(dest => dest.OptionCount, opt => opt.MapFrom(src => src.Options.Count));
        CreateMap<CreateEventSessionCustomPropertyDefinitionDto, EventSessionCustomPropertyDefinition>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.TenantId, opt => opt.Ignore())
            .ForMember(dest => dest.Tenant, opt => opt.Ignore())
            .ForMember(dest => dest.EventSession, opt => opt.Ignore())
            .ForMember(dest => dest.SourceTemplate, opt => opt.Ignore())
            .ForMember(dest => dest.SourceTemplateId, opt => opt.Ignore())
            .ForMember(dest => dest.SourceTemplateKey, opt => opt.Ignore())
            .ForMember(dest => dest.SourceTemplateVersion, opt => opt.Ignore())
            .ForMember(dest => dest.SourceTemplateDefinitionId, opt => opt.Ignore())
            .ForMember(dest => dest.InstantiatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.LastSyncedFromTemplateAt, opt => opt.Ignore())
            .ForMember(dest => dest.DefaultOptionId, opt => opt.Ignore())
            .ForMember(dest => dest.DefaultOption, opt => opt.Ignore())
            .ForMember(dest => dest.Options, opt => opt.Ignore())
            .ForMember(dest => dest.Values, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedBy, opt => opt.Ignore())
            .ForMember(dest => dest.IsDeleted, opt => opt.Ignore())
            .ForMember(dest => dest.DeletedAt, opt => opt.Ignore())
            .ForMember(dest => dest.DeletedBy, opt => opt.Ignore());

        CreateMap<EventSessionCustomPropertyValue, EventSessionCustomPropertyValueDto>();
        CreateMap<SetEventSessionCustomPropertyValueDto, EventSessionCustomPropertyValue>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.TenantId, opt => opt.Ignore())
            .ForMember(dest => dest.Tenant, opt => opt.Ignore())
            .ForMember(dest => dest.Definition, opt => opt.Ignore())
            .ForMember(dest => dest.EventSession, opt => opt.Ignore())
            .ForMember(dest => dest.Option, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedBy, opt => opt.Ignore())
            .ForMember(dest => dest.IsDeleted, opt => opt.Ignore())
            .ForMember(dest => dest.DeletedAt, opt => opt.Ignore())
            .ForMember(dest => dest.DeletedBy, opt => opt.Ignore());
    }
}
