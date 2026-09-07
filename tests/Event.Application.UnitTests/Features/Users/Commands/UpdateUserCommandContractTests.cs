namespace Event.Application.UnitTests.Features.Users.Commands;

using Explore.Application.Authorization;
using Explore.Application.DTOs.User;
using Explore.Application.DTOs.User.Validators;
using Explore.Application.Features.Users.Requests.Commands;

public sealed class UpdateUserCommandContractTests
{
    [Test]
    public async Task SecureRequestResourceId_UsesRouteUserId()
    {
        var userId = Guid.CreateVersion7();
        ISecureRequest command = new UpdateUserCommand
        {
            UserId = userId,
            ExpectedConcurrencyStamp = Guid.CreateVersion7(),
            UpdateUserDto = new UpdateUserDto
            {
                Names = new UpdateUserNamesDto
                {
                    FirstName = "Updated",
                    LastName = "User"
                }
            }
        };

        await Assert.That(command.ResourceId).IsEqualTo(userId.ToString());
    }

    [Test]
    public async Task Validator_WhenNamesGroupIsPresent_DoesNotRequireBodyId()
    {
        var validator = new UpdateUserDtoValidator();
        var dto = new UpdateUserDto
        {
            Names = new UpdateUserNamesDto
            {
                FirstName = "Updated",
                LastName = "User"
            }
        };

        var result = await validator.ValidateAsync(dto);

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task Validator_WhenNoGroupsArePresent_Fails()
    {
        var validator = new UpdateUserDtoValidator();

        var result = await validator.ValidateAsync(new UpdateUserDto());

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Select(error => error.ErrorMessage))
            .Contains("At least one of Names or ProfileImage must be provided.");
    }
}
