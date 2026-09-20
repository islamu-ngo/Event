using Explore.Application.DTOs.Geocoding;
using Explore.Application.Features.Geocoding.Handlers.Commands;
using Explore.Application.Features.Geocoding.Requests.Commands;
using FluentValidation;

namespace Event.Application.UnitTests.Features.Locations.Queries;

public sealed class LocationUnicodeInputContractTests
{
    [Test]
    [Arguments("a\0b")]
    [Arguments("a\uD800b")]
    [Arguments("a\uFDD0b")]
    [Arguments("a\U0010FFFFb")]
    public async Task InvalidSearchIsAValidationFailureBeforeDependenciesAreUsed(string text)
    {
        var handler = new CreateAddressSuggestionsCommandHandler(null!, null!, null!, null!, null!);
        var request = new CreateAddressSuggestionsCommand(Guid.CreateVersion7(),
            new AddressSuggestionsRequestDto { SearchText = text, Limit = 5 });

        await Assert.That(async () => await handler.ExecuteAsync(request, CancellationToken.None))
            .Throws<ValidationException>();
    }
}
