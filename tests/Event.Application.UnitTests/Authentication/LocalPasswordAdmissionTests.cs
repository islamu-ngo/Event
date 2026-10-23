using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Features.Authentication.Local.Validators;

namespace Event.Application.UnitTests.Authentication;

public sealed class LocalPasswordAdmissionTests
{
    [Test]
    public async Task LoginAdmitsShortCredentialShapeWhileCreationRejectsIt()
    {
        string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(7));
        var request = new LocalAuthRequestDto(Guid.CreateVersion7().ToString("N"), password);
        await Assert.That((await new LocalAuthRequestDtoValidator().ValidateAsync(request)).IsValid).IsTrue();
        await Assert.That(new LocalPasswordAttribute(newCredential: false).IsValid(password)).IsTrue();
        await Assert.That(new LocalPasswordAttribute().IsValid(password)).IsFalse();
        await Assert.That((await new LocalPasswordChangeRequestDtoValidator().ValidateAsync(new LocalPasswordChangeRequestDto
        {
            CurrentPassword = password,
            NewPassword = password
        })).IsValid).IsFalse();
    }

    [Test]
    [Arguments(15, true)]
    [Arguments(128, true)]
    [Arguments(129, false)]
    public async Task ManualAndHttpAdmissionShareSupplementaryScalarBounds(int count, bool accepted)
    {
        string scalar = char.ConvertFromUtf32(0x1F642);
        string password = string.Concat(Enumerable.Repeat(scalar, count));
        await Assert.That(new LocalPasswordAttribute().IsValid(password)).IsEqualTo(accepted);
        var change = new LocalPasswordChangeRequestDto
        {
            CurrentPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
            NewPassword = password
        };
        await Assert.That((await new LocalPasswordChangeRequestDtoValidator().ValidateAsync(change)).IsValid)
            .IsEqualTo(accepted);
        await Assert.That(Validator.TryValidateObject(change, new ValidationContext(change), [], true))
            .IsEqualTo(accepted);
        var replacement = new LocalCredentialReplacementRequestDto { NewPassword = password };
        await Assert.That(Validator.TryValidateObject(replacement, new ValidationContext(replacement), [], true))
            .IsEqualTo(accepted);
    }
}
