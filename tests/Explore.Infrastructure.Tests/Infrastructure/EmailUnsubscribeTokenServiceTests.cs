using Explore.Application.Contracts.Services;
using Explore.Domain.Constants;
using Explore.Infrastructure.Mail.Unsubscribe;
using Microsoft.AspNetCore.DataProtection;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class EmailUnsubscribeTokenServiceTests
{
    [Test]
    public async Task ValidateToken_WithGeneratedToken_ReturnsPayload()
    {
        var service = CreateService(out var tempPath);

        try
        {
            var payload = new EmailUnsubscribeTokenPayload(
                Guid.NewGuid(),
                Guid.NewGuid(),
                NotificationPreferenceCategories.RegistrationConfirmations,
                DateTime.UtcNow);

            var token = service.GenerateToken(payload);
            var result = service.ValidateToken(token);

            await Assert.That(result.IsValid).IsTrue();
            await Assert.That(result.Payload).IsNotNull();
            await Assert.That(result.Payload!.TenantId).IsEqualTo(payload.TenantId);
            await Assert.That(result.Payload.UserId).IsEqualTo(payload.UserId);
            await Assert.That(result.Payload.Category).IsEqualTo(NotificationPreferenceCategories.RegistrationConfirmations);
        }
        finally
        {
            Directory.Delete(tempPath, recursive: true);
        }
    }

    [Test]
    public async Task ValidateToken_WithTamperedToken_ReturnsInvalid()
    {
        var service = CreateService(out var tempPath);

        try
        {
            var payload = new EmailUnsubscribeTokenPayload(
                Guid.NewGuid(),
                Guid.NewGuid(),
                NotificationPreferenceCategories.RegistrationConfirmations,
                DateTime.UtcNow);

            var token = service.GenerateToken(payload);
            var tampered = token[..^1] + (token[^1] == 'a' ? 'b' : 'a');

            var result = service.ValidateToken(tampered);

            await Assert.That(result.IsValid).IsFalse();
            await Assert.That(result.Payload).IsNull();
        }
        finally
        {
            Directory.Delete(tempPath, recursive: true);
        }
    }

    [Test]
    public async Task ValidateToken_WithExpiredToken_ReturnsInvalid()
    {
        var service = CreateService(out var tempPath);

        try
        {
            var payload = new EmailUnsubscribeTokenPayload(
                Guid.NewGuid(),
                Guid.NewGuid(),
                NotificationPreferenceCategories.RegistrationConfirmations,
                DateTime.UtcNow);

            var token = service.GenerateToken(payload, TimeSpan.FromMilliseconds(1));
            await Task.Delay(50);

            var result = service.ValidateToken(token);

            await Assert.That(result.IsValid).IsFalse();
            await Assert.That(result.Payload).IsNull();
        }
        finally
        {
            Directory.Delete(tempPath, recursive: true);
        }
    }

    private static EmailUnsubscribeTokenService CreateService(out string tempPath)
    {
        tempPath = Path.Combine(Path.GetTempPath(), $"islamu-unsubscribe-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempPath);
        var provider = DataProtectionProvider.Create(new DirectoryInfo(tempPath));
        return new EmailUnsubscribeTokenService(provider);
    }
}
