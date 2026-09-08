namespace Explore.Application.DTOs.EventAspects.Validators;

using System;
using FluentValidation;

/// <summary>
/// Validator for Tech aspect create/update operations.
/// </summary>
public class CreateUpdateTechAspectDtoValidator : AbstractValidator<CreateUpdateTechAspectDto>
{
    public CreateUpdateTechAspectDtoValidator()
    {
        // GitHub repo URL validation
        RuleFor(x => x.GithubRepoUrl)
            .MaximumLength(500)
            .Must(url => string.IsNullOrEmpty(url) || Uri.TryCreate(url, UriKind.Absolute, out var result) &&
                        (result.Scheme == Uri.UriSchemeHttp || result.Scheme == Uri.UriSchemeHttps))
            .When(x => !string.IsNullOrEmpty(x.GithubRepoUrl))
            .WithMessage("GitHub repository URL must be a valid HTTP/HTTPS URL.");

        // Hackathon track validation
        RuleFor(x => x.HackathonTrack)
            .MaximumLength(200)
            .When(x => !string.IsNullOrEmpty(x.HackathonTrack));

        // Skill level validation
        RuleFor(x => x.SkillLevel)
            .IsInEnum()
            .WithMessage("Invalid skill level.");

        // Tech stack tags validation
        RuleFor(x => x.TechStackTags)
            .MaximumLength(1000)
            .When(x => !string.IsNullOrEmpty(x.TechStackTags));

        // Max team size validation
        RuleFor(x => x.MaxTeamSize)
            .InclusiveBetween(1, 100)
            .When(x => x.MaxTeamSize.HasValue)
            .WithMessage("Max team size must be between 1 and 100.");

        // Prize pool validation
        RuleFor(x => x.PrizePool)
            .GreaterThanOrEqualTo(0)
            .When(x => x.PrizePool.HasValue)
            .WithMessage("Prize pool cannot be negative.");

        // Prize currency required when prize pool is set
        RuleFor(x => x.PrizeCurrencyCode)
            .NotEmpty()
            .When(x => x.PrizePool.HasValue && x.PrizePool > 0)
            .WithMessage("Currency code is required when prize pool is specified.");

        // Currency code format validation
        RuleFor(x => x.PrizeCurrencyCode)
            .Length(3)
            .Matches("^[A-Z]{3}$")
            .When(x => !string.IsNullOrEmpty(x.PrizeCurrencyCode))
            .WithMessage("Currency code must be a 3-letter ISO code (e.g., USD, EUR).");
    }
}

public sealed class UpdateEventTechAspectDtoValidator : AbstractValidator<UpdateEventTechAspectDto>
{
    public UpdateEventTechAspectDtoValidator()
    {
        RuleFor(dto => dto)
            .Must(dto => dto.Repository is not null ||
                dto.Classification is not null ||
                dto.Participation is not null ||
                dto.Prize is not null)
            .WithMessage("At least one Tech aspect update group must be provided.");
        RuleFor(dto => dto.Repository)
            .Must(group => group is null || group.GithubRepoUrl.HasValue)
            .WithMessage("The repository group must contain GithubRepoUrl.");
        RuleFor(dto => dto.Classification)
            .Must(group => group is null ||
                group.HackathonTrack.HasValue ||
                group.SkillLevel.HasValue ||
                group.TechStackTags.HasValue)
            .WithMessage("The classification group must contain at least one supplied field.");
        RuleFor(dto => dto.Participation)
            .Must(group => group is null ||
                group.RequiresLaptop.HasValue ||
                group.IsCodingCompetition.HasValue)
            .WithMessage("The participation group must contain at least one supplied field.");
        RuleFor(dto => dto.Prize)
            .Must(group => group is null ||
                group.MaxTeamSize.HasValue ||
                group.PrizePool.HasValue ||
                group.PrizeCurrencyCode.HasValue)
            .WithMessage("The prize group must contain at least one supplied field.");
    }
}
