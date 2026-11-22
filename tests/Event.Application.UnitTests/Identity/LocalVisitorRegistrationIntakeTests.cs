using System.Security.Cryptography;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Features.Authentication.Local.Requests.Commands;
using Explore.Application.Features.Authentication.Local.Validators;
using Explore.Application.Contracts.Identity;
using Explore.Application.Responses;

namespace Event.Application.UnitTests.Identity;

public sealed class LocalVisitorRegistrationIntakeTests
{
    [Test]
    public async Task PasswordAndProtectedIntentCannotAppearInRequestOrCommandDiagnostics()
    {
        var request = new LocalVisitorRegistrationRequestDto(Guid.CreateVersion7(),
            $"visitor-{Guid.CreateVersion7():N}", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            null, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        var command = new RegisterLocalVisitorCommand(request,
            Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)));

        // Boolean comparisons keep a failed redaction witness from logging its synthetic credentials.
        await Assert.That(request.ToString() == nameof(LocalVisitorRegistrationRequestDto)).IsTrue();
        await Assert.That(command.ToString() == nameof(RegisterLocalVisitorCommand)).IsTrue();
    }

    [Test]
    public async Task UsernameSignupDoesNotRequireContactEmail()
    {
        var result = await new LocalVisitorRegistrationRequestDtoValidator().ValidateAsync(Valid());
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task ReservationCommandAndResultDiagnosticsOmitBrowserProofAndProtectedClaim()
    {
        var command = new ReserveLocalVisitorEnrollmentCommand(Guid.CreateVersion7(),
            Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)));
        var reservation = new LocalVisitorEnrollmentReservation(command.OperationId,
            Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)), DateTime.UnixEpoch.AddMinutes(15));
        var response = LocalVisitorEnrollmentReservationCommandResponse.Reserved(reservation);
        await Assert.That(command.ToString() == nameof(ReserveLocalVisitorEnrollmentCommand)).IsTrue();
        await Assert.That(reservation.ToString() == nameof(LocalVisitorEnrollmentReservation)).IsTrue();
        await Assert.That(response.ToString() == nameof(LocalVisitorEnrollmentReservationCommandResponse)).IsTrue();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    [Arguments(8)]
    public async Task InvalidClientIntentCannotEnterRegistration(int scenario)
    {
        var valid = Valid();
        var request = scenario switch
        {
            0 => valid with { OperationId = Guid.Empty },
            1 => valid with { OperationId = Guid.NewGuid() },
            2 => valid with { Username = "ambiguous name" },
            3 => valid with { Username = "username@example.test" },
            4 => valid with { Username = "invalid\0name" },
            5 => valid with { Password = string.Empty },
            6 => valid with { EnrollmentIntent = string.Empty },
            7 => valid with { Email = "not-an-email-address" },
            8 => valid with { Username = new string('a', 257) },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        var result = await new LocalVisitorRegistrationRequestDtoValidator().ValidateAsync(request);
        await Assert.That(result.IsValid).IsFalse();
    }

    private static LocalVisitorRegistrationRequestDto Valid() => new(Guid.CreateVersion7(),
        $"visitor-{Guid.CreateVersion7():N}", $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}",
        null, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
}
