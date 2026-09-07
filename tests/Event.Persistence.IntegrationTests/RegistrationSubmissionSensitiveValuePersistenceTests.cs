using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class RegistrationSubmissionSensitiveValuePersistenceTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Category("Runtime")]
    public async Task DataProtectionCiphertextRoundTripsWhilePlaintextIsAbsentFromPostgreSql()
    {
        await fixture.ResetAsync();
        string keyDirectory = Path.Combine(Path.GetTempPath(), $"registration-dp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(keyDirectory);
        try
        {
            IDataProtectionProvider provider = DataProtectionProvider.Create(new DirectoryInfo(keyDirectory),
                configuration => configuration.SetApplicationName("islamu-event-registration-tests"));
            RegistrationSensitiveValueProtector protector = new(provider);
            const string plaintext = "private-attendee-value";
            var protectedValue = protector.Protect(plaintext);
            Guid valueId;

            await using (ExploreDbContext context = fixture.CreateDbContext())
            {
                Tenant tenant = new()
                {
                    FullName = "Sensitive registration test",
                    Slug = $"sensitive-{Guid.NewGuid():N}",
                    TenantStatusId = 2,
                    TenantStatus = null!
                };
                context.Tenants.Add(tenant);
                await context.SaveChangesAsync();
                RegistrationSensitiveAnswerValue value = RegistrationSensitiveAnswerValue.Create(
                    tenant.Id, protectedValue.Ciphertext, protectedValue.KeyVersion,
                    new DateTime(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc));
                valueId = value.Id;
                context.RegistrationSensitiveAnswerValues.Add(value);
                await context.SaveChangesAsync();
            }

            await using ExploreDbContext verification = fixture.CreateDbContext();
            RegistrationSensitiveAnswerValue persisted = await verification.RegistrationSensitiveAnswerValues
                .AsNoTracking().SingleAsync(value => value.Id == valueId);
            string databaseCiphertext = await verification.Database.SqlQueryRaw<string>(
                "SELECT ciphertext AS \"Value\" FROM islamu_event.registration_sensitive_answer_values WHERE id = {0}", valueId)
                .SingleAsync();

            await Assert.That(databaseCiphertext).IsEqualTo(protectedValue.Ciphertext);
            await Assert.That(databaseCiphertext).DoesNotContain(plaintext);
            await Assert.That(persisted.KeyVersion).IsEqualTo(RegistrationSensitiveValueProtector.CurrentKeyVersion);
            await Assert.That(protector.Unprotect(persisted.Ciphertext, persisted.KeyVersion)).IsEqualTo(plaintext);
        }
        finally
        {
            Directory.Delete(keyDirectory, recursive: true);
        }
    }
}
