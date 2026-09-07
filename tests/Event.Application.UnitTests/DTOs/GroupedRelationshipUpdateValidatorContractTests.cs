using Explore.Application.DTOs.EventCategories;
using Explore.Application.DTOs.EventCategories.Validators;
using Explore.Application.DTOs.EventSessionLanguage;
using Explore.Application.DTOs.EventSessionLanguage.Validators;
using Explore.Application.DTOs.EventSessionSpeaker;
using Explore.Application.DTOs.EventSessionSpeaker.Validators;
using Explore.Application.DTOs.EventTags;
using Explore.Application.DTOs.EventTags.Validators;
using FluentValidation.Results;

namespace Event.Application.UnitTests.DTOs;

public sealed class GroupedRelationshipUpdateValidatorContractTests
{
    [Test]
    public async Task EmptyWrappersAreRejectedAcrossRelationshipFamilies()
    {
        ValidationResult[] results =
        [
            await new UpdateEventCategoriesDtoValidator()
                .ValidateAsync(new UpdateEventCategoriesDto()),
            await new UpdateEventSessionLanguageDtoValidator()
                .ValidateAsync(new UpdateEventSessionLanguageDto()),
            await new UpdateEventSessionSpeakerDtoValidator()
                .ValidateAsync(new UpdateEventSessionSpeakerDto()),
            await new UpdateEventTagsDtoValidator()
                .ValidateAsync(new UpdateEventTagsDto())
        ];

        await Assert.That(results.All(result => !result.IsValid)).IsTrue();
    }

    [Test]
    public async Task OneCompleteGroupIsValidAcrossRelationshipFamilies()
    {
        ValidationResult[] results =
        [
            await new UpdateEventCategoriesDtoValidator().ValidateAsync(
                new UpdateEventCategoriesDto
                {
                    Category = new UpdateEventCategoriesCategoryDto
                    {
                        CategoryId = Guid.CreateVersion7()
                    }
                }),
            await new UpdateEventSessionLanguageDtoValidator().ValidateAsync(
                new UpdateEventSessionLanguageDto
                {
                    Language = new UpdateEventSessionLanguageLanguageDto
                    {
                        LanguageId = 2
                    }
                }),
            await new UpdateEventSessionSpeakerDtoValidator().ValidateAsync(
                new UpdateEventSessionSpeakerDto
                {
                    Actor = new UpdateEventSessionSpeakerActorDto
                    {
                        ActorId = Guid.CreateVersion7()
                    }
                }),
            await new UpdateEventTagsDtoValidator().ValidateAsync(
                new UpdateEventTagsDto
                {
                    Tag = new UpdateEventTagsTagDto
                    {
                        TagId = Guid.CreateVersion7()
                    }
                })
        ];

        await Assert.That(results.All(result => result.IsValid)).IsTrue();
    }

    [Test]
    public async Task EmptyIdentifiersAreRejectedAtTheirNestedContractPaths()
    {
        (ValidationResult Result, string Property)[]
            results =
        [
            (await new UpdateEventCategoriesDtoValidator().ValidateAsync(
                new UpdateEventCategoriesDto
                {
                    Event = new UpdateEventCategoriesEventDto()
                }),
                nameof(UpdateEventCategoriesEventDto.EventId)),
            (await new UpdateEventSessionLanguageDtoValidator().ValidateAsync(
                new UpdateEventSessionLanguageDto
                {
                    Session = new UpdateEventSessionLanguageSessionDto()
                }),
                nameof(UpdateEventSessionLanguageSessionDto.EventSessionId)),
            (await new UpdateEventSessionSpeakerDtoValidator().ValidateAsync(
                new UpdateEventSessionSpeakerDto
                {
                    Session = new UpdateEventSessionSpeakerSessionDto()
                }),
                nameof(UpdateEventSessionSpeakerSessionDto.EventSessionId)),
            (await new UpdateEventTagsDtoValidator().ValidateAsync(
                new UpdateEventTagsDto
                {
                    Tag = new UpdateEventTagsTagDto()
                }),
                nameof(UpdateEventTagsTagDto.TagId))
        ];

        foreach ((ValidationResult result, string property) in results)
        {
            await Assert.That(result.IsValid).IsFalse();
            await Assert.That(result.Errors.Any(error =>
                    error.PropertyName.EndsWith(
                        property,
                        StringComparison.Ordinal)))
                .IsTrue();
        }
    }
}
