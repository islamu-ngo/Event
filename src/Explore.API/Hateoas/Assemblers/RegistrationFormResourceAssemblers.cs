using Explore.API.Hateoas.Policies;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.RegistrationForms;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Assemblers;

public sealed class RegistrationWorkflowResourceAssembler(
    IHateoasLinkGenerator linkGenerator,
    ILinkPolicy<RegistrationWorkflowDto> detailPolicy,
    ICollectionLinkPolicy<RegistrationWorkflowDto> collectionPolicy,
    RegistrationWorkflowLinkPolicy policy,
    IResourceAssembler<RegistrationFormDto, RegistrationFormDto> formAssembler)
    : ResourceAssemblerBase<RegistrationWorkflowDto, RegistrationWorkflowDto>(linkGenerator, detailPolicy, collectionPolicy)
{
    public override async Task<HalResource<RegistrationWorkflowDto>> ToResource(RegistrationWorkflowDto dto, HttpContext httpContext)
    {
        HalResource<RegistrationWorkflowDto> resource = await base.ToResource(dto, httpContext);
        if (IsMinimalResponse(httpContext)) return resource;
        var requirements = new List<HalResource<RegistrationRequirementDto>>(dto.Requirements.Count);
        foreach (RegistrationRequirementDto requirement in dto.Requirements)
        {
            requirements.Add(new HalResource<RegistrationRequirementDto>
            {
                Data = requirement,
                Links = await GenerateLinks(policy.GetRequirementLinks(dto, requirement), httpContext.User, httpContext),
                Embedded = new Dictionary<string, object>
                {
                    ["channels"] = requirement.Channels.Select(channel => new HalResource<RegistrationChannelDto>(channel)).ToArray()
                }
            });
        }
        var forms = new List<HalResource<RegistrationFormDto>>(dto.Forms.Count);
        foreach (RegistrationFormDto form in dto.Forms)
        {
            forms.Add(await formAssembler.ToResource(form, httpContext));
        }
        return new HalResource<RegistrationWorkflowDto>
        {
            Data = resource.Data,
            Links = resource.Links,
            Embedded = new Dictionary<string, object>
            {
                ["requirements"] = requirements,
                ["forms"] = forms
            }
        };
    }
}

public sealed class RegistrationFormResourceAssembler(
    IHateoasLinkGenerator linkGenerator,
    ILinkPolicy<RegistrationFormDto> detailPolicy,
    ICollectionLinkPolicy<RegistrationFormDto> collectionPolicy,
    RegistrationFormLinkPolicy policy)
    : ResourceAssemblerBase<RegistrationFormDto, RegistrationFormDto>(linkGenerator, detailPolicy, collectionPolicy)
{
    public override async Task<HalResource<RegistrationFormDto>> ToResource(RegistrationFormDto dto, HttpContext httpContext)
    {
        HalResource<RegistrationFormDto> resource = await base.ToResource(dto, httpContext);
        if (IsMinimalResponse(httpContext)) return resource;
        var versions = new List<HalResource<RegistrationFormVersionSummaryDto>>(dto.Versions.Count);
        foreach (RegistrationFormVersionSummaryDto version in dto.Versions)
        {
            versions.Add(new HalResource<RegistrationFormVersionSummaryDto>
            {
                Data = version,
                Links = await GenerateLinks(policy.GetVersionLinks(dto, version), httpContext.User, httpContext)
            });
        }
        return new HalResource<RegistrationFormDto>
        {
            Data = resource.Data,
            Links = resource.Links,
            Embedded = new Dictionary<string, object> { ["versions"] = versions }
        };
    }
}

public sealed class RegistrationFormVersionResourceAssembler(
    IHateoasLinkGenerator linkGenerator,
    ILinkPolicy<RegistrationFormVersionDto> detailPolicy,
    ICollectionLinkPolicy<RegistrationFormVersionDto> collectionPolicy,
    RegistrationFormVersionLinkPolicy policy,
    IHateoasAuthorizationEvaluator authorizationEvaluator)
    : ResourceAssemblerBase<RegistrationFormVersionDto, RegistrationFormVersionDto>(linkGenerator, detailPolicy, collectionPolicy)
{
    public override async Task<HalResource<RegistrationFormVersionDto>> ToResource(RegistrationFormVersionDto dto, HttpContext httpContext)
    {
        if (IsMinimalResponse(httpContext)) return new HalResource<RegistrationFormVersionDto>(dto);

        var user = ResolveCapabilityPrincipal(httpContext);
        IReadOnlyList<LinkDefinition> rootDefinitions = await GetDetailLinkDefinitionsAsync(dto, user, httpContext);
        LinkDefinition[][] sectionDefinitions = dto.Sections
            .Select(section => policy.GetSectionLinks(dto, section).ToArray()).ToArray();
        LinkDefinition[][] fieldDefinitions = dto.Sections
            .SelectMany(section => section.Fields.Select(field => policy.GetFieldLinks(dto, section, field).ToArray())).ToArray();
        LinkDefinition[][] optionDefinitions = dto.Sections
            .SelectMany(section => section.Fields.SelectMany(field => field.Options
                .Select(option => policy.GetOptionLinks(dto, section, field, option).ToArray()))).ToArray();
        LinkDefinition[][] ruleDefinitions = dto.Rules
            .Select(rule => policy.GetRuleLinks(dto, rule).ToArray()).ToArray();
        LinkDefinition[] definitions = rootDefinitions
            .Concat(sectionDefinitions.SelectMany(group => group))
            .Concat(fieldDefinitions.SelectMany(group => group))
            .Concat(optionDefinitions.SelectMany(group => group))
            .Concat(ruleDefinitions.SelectMany(group => group))
            .ToArray();
        IReadOnlyList<bool> decisions = await authorizationEvaluator.AreLinksAllowedAsync(definitions, user, httpContext);

        var decisionIndex = rootDefinitions.Count;
        Dictionary<string, HalLink>[] sectionLinks = MaterializeGroups(sectionDefinitions, decisions, ref decisionIndex, httpContext, LinkGenerator);
        Dictionary<string, HalLink>[] fieldLinks = MaterializeGroups(fieldDefinitions, decisions, ref decisionIndex, httpContext, LinkGenerator);
        Dictionary<string, HalLink>[] optionLinks = MaterializeGroups(optionDefinitions, decisions, ref decisionIndex, httpContext, LinkGenerator);
        Dictionary<string, HalLink>[] ruleLinks = MaterializeGroups(ruleDefinitions, decisions, ref decisionIndex, httpContext, LinkGenerator);
        var sectionIndex = 0;
        var fieldIndex = 0;
        var optionIndex = 0;
        return new HalResource<RegistrationFormVersionDto>
        {
            Data = dto,
            Links = Materialize(rootDefinitions, decisions, 0, httpContext, LinkGenerator),
            Embedded = new Dictionary<string, object>
            {
                ["sections"] = dto.Sections.Select(section => new HalResource<RegistrationFormSectionDto>
                {
                    Data = section,
                    Links = sectionLinks[sectionIndex++],
                    Embedded = new Dictionary<string, object>
                    {
                        ["fields"] = section.Fields.Select(field => new HalResource<RegistrationFormFieldDto>
                        {
                            Data = field,
                            Links = fieldLinks[fieldIndex++],
                            Embedded = new Dictionary<string, object>
                            {
                                ["options"] = field.Options.Select(option => new HalResource<RegistrationFormFieldOptionDto>
                                {
                                    Data = option,
                                    Links = optionLinks[optionIndex++]
                                }).ToArray()
                            }
                        }).ToArray()
                    }
                }).ToArray(),
                ["rules"] = dto.Rules.Select((rule, index) => new HalResource<RegistrationFormRuleDto>
                {
                    Data = rule,
                    Links = ruleLinks[index]
                }).ToArray()
            }
        };
    }

    private static Dictionary<string, HalLink>[] MaterializeGroups(
        IReadOnlyList<LinkDefinition>[] groups,
        IReadOnlyList<bool> decisions,
        ref int decisionIndex,
        HttpContext context,
        IHateoasLinkGenerator linkGenerator)
    {
        var links = new Dictionary<string, HalLink>[groups.Length];
        for (var index = 0; index < groups.Length; index++)
        {
            links[index] = Materialize(groups[index], decisions, ref decisionIndex, context, linkGenerator);
        }
        return links;
    }

    private static Dictionary<string, HalLink> Materialize(
        IReadOnlyList<LinkDefinition> definitions,
        IReadOnlyList<bool> decisions,
        int decisionIndex,
        HttpContext context,
        IHateoasLinkGenerator linkGenerator) =>
        Materialize(definitions, decisions, ref decisionIndex, context, linkGenerator);

    private static Dictionary<string, HalLink> Materialize(
        IReadOnlyList<LinkDefinition> definitions,
        IReadOnlyList<bool> decisions,
        ref int decisionIndex,
        HttpContext context,
        IHateoasLinkGenerator linkGenerator)
    {
        var links = new Dictionary<string, HalLink>();
        foreach (LinkDefinition definition in definitions)
        {
            if (decisionIndex < decisions.Count && decisions[decisionIndex]
                && linkGenerator.GenerateLink(definition, context) is { } link)
            {
                links[definition.Rel] = link;
            }
            decisionIndex++;
        }
        return links;
    }
}

public sealed class RegistrationFormPublishPreflightResourceAssembler(
    IHateoasLinkGenerator linkGenerator,
    ILinkPolicy<RegistrationFormPublishPreflightDto> detailPolicy,
    ICollectionLinkPolicy<RegistrationFormPublishPreflightDto> collectionPolicy)
    : ResourceAssemblerBase<RegistrationFormPublishPreflightDto, RegistrationFormPublishPreflightDto>(linkGenerator, detailPolicy, collectionPolicy);

public sealed class OptionalQuestionnaireResourceAssembler(
    IHateoasLinkGenerator linkGenerator,
    ILinkPolicy<OptionalQuestionnaireDto> detailPolicy,
    ICollectionLinkPolicy<OptionalQuestionnaireDto> collectionPolicy)
    : ResourceAssemblerBase<OptionalQuestionnaireDto, OptionalQuestionnaireDto>(
        linkGenerator,
        detailPolicy,
        collectionPolicy);
