using Explore.Blazor.Client.Validators;

namespace Explore.Blazor.Client.Tests.Validators;

public class CreateExternalApiKeyDtoValidatorTests
{
    private readonly CreateExternalApiKeyDtoValidator _validator = new();

    [Test]
    public async Task Validate_WhenNameContainsControlCharacter_ReturnsError()
    {
        var result = _validator.Validate(new CreateExternalApiKeyDto
        {
            Name = "Ops\nBot",
            Scopes = ["events:read"]
        });

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors).Contains(error => error.ErrorMessage == "API key name must not contain control characters.");
    }

    [Test]
    public async Task Validate_WhenDescriptionExceedsLimit_ReturnsError()
    {
        var result = _validator.Validate(new CreateExternalApiKeyDto
        {
            Name = "Ops Bot",
            Description = new string('a', 1001),
            Scopes = ["events:read"]
        });

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors).Contains(error => error.ErrorMessage == "API key description cannot exceed 1000 characters.");
    }

    [Test]
    public async Task Validate_WhenScopesAreEmpty_ReturnsError()
    {
        var result = _validator.Validate(new CreateExternalApiKeyDto
        {
            Name = "Ops Bot",
            Scopes = []
        });

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors).Contains(error => error.ErrorMessage == "Select at least one scope.");
    }
}
