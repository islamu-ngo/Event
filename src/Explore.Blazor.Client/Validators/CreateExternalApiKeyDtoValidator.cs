using Explore.Blazor.Client.Clients;
using FluentValidation;

namespace Explore.Blazor.Client.Validators;

public class CreateExternalApiKeyDtoValidator : AbstractValidator<CreateExternalApiKeyDto>
{
    private const int NameMaxLength = 200;
    private const int DescriptionMaxLength = 1000;

    public CreateExternalApiKeyDtoValidator()
    {
        RuleFor(x => x.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("API key name is required.")
            .MaximumLength(NameMaxLength).WithMessage("API key name cannot exceed 200 characters.")
            .Must(DoesNotContainControlCharacters).WithMessage("API key name must not contain control characters.");

        RuleFor(x => x.Description)
            .MaximumLength(DescriptionMaxLength).WithMessage("API key description cannot exceed 1000 characters.")
            .Must(DoesNotContainControlCharacters).WithMessage("API key description must not contain control characters.");

        RuleFor(x => x.Scopes)
            .NotEmpty().WithMessage("Select at least one scope.")
            .Must(scopes => scopes is not null && scopes.All(scope => !string.IsNullOrWhiteSpace(scope)))
            .WithMessage("Scopes cannot contain empty values.");
    }

    private static bool DoesNotContainControlCharacters(string? value)
        => value is null || !value.Any(char.IsControl);
}
