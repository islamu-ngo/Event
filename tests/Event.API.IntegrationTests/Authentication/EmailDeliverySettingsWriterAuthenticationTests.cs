using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain.Constants;
using Explore.Persistence;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed class EmailDeliverySettingsWriterAuthenticationTests
{
    [Test]
    [Arguments("actor")]
    [Arguments("revision")]
    [Arguments("acknowledgement")]
    [Arguments("token")]
    public async Task DirectStoreDisable_RejectsInvalidEvidenceWithoutPolicyMutation(string invalidPart)
    {
        await using var fixture = await SmtpAuthenticatedFixture.CreateAsync(
            instanceAdministrator: true, tenantAdministrator: false);
        EmailDeliveryDisableConfirmation confirmation = null!;
        await fixture.ExecuteAsync(tenantOwner: false, async services =>
        {
            var context = services.GetRequiredService<ExploreDbContext>();
            await using var transaction = await context.Database.BeginTransactionAsync();
            var snapshot = (await services.GetRequiredService<IEmailDeliveryDisableImpactReader>()
                .ReadAsync(null))!;
            await Assert.That(snapshot.CanDisable).IsTrue();
            var tokens = services.GetRequiredService<IEmailDeliveryDisableTokenService>();
            var issued = tokens.Issue(fixture.Subject.PrincipalId, snapshot);
            await Assert.That(tokens.Matches(issued.Token, fixture.Subject.PrincipalId, snapshot)).IsTrue();
            confirmation = new(
                TenantId: null,
                ActorUserId: invalidPart == "actor" ? Guid.CreateVersion7() : fixture.Subject.PrincipalId,
                ExpectedRevision: snapshot.Revision + (invalidPart == "revision" ? 1 : 0),
                ConfirmationToken: invalidPart == "token" ? "invalid" : issued.Token,
                Acknowledgement: invalidPart == "acknowledgement"
                    ? "DISABLE EMAIL DELIVERY " : EmailDeliveryDisableConfirmation.RequiredAcknowledgement);
        });

        // A distinct selected owner retains the original authenticated subject and lifetime.
        // It never creates another login or tries to reuse a completed final-admission attempt.
        await fixture.ExecuteAsync(tenantOwner: false, async services =>
        {
            var writer = services.GetRequiredService<EmailDeliverySettingsWriter>();
            if (invalidPart == "actor")
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => writer.DisableAsync(confirmation));
            else
                await Assert.That((await writer.DisableAsync(confirmation)).Status)
                    .IsEqualTo(invalidPart == "acknowledgement"
                        ? EmailDeliverySettingsWriteStatus.InvalidConfirmation
                        : EmailDeliverySettingsWriteStatus.ConfirmationConflict);
        });

        await fixture.ExecuteAsync(tenantOwner: false, async services =>
        {
            var context = services.GetRequiredService<ExploreDbContext>();
            await Assert.That((await context.SystemSettings.AsNoTracking().SingleAsync(
                row => row.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled)).Value).IsEqualTo("true");
        });
    }
}
