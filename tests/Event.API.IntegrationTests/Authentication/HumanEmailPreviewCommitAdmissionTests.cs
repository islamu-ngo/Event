using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EmailDispatch;
using Explore.Application.Features.EmailDispatch.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain.Constants;
using Explore.Persistence;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed class HumanEmailPreviewCommitAdmissionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PreviewDoesNotDiscloseConfirmationAfterOriginalCredentialExpiresDuringImpactRead(bool expire)
    {
        SmtpAuthenticatedFixture fixture = null!;
        bool observed = false;
        await using var owner = fixture = await SmtpAuthenticatedFixture.CreateAsync(
            configureServices: services => services.Replace(ServiceDescriptor.Scoped<IEmailDeliveryDisableImpactReader>(
                provider => new AfterImpactRead(new EmailDeliveryDisableImpactReader(
                    provider.GetRequiredService<ExploreDbContext>(), provider), () =>
                {
                    observed = true;
                    if (expire)
                        fixture.Clock.Advance(fixture.Lifetime.ExpiresAt.Add(fixture.Lifetime.ClockSkew)
                            .AddTicks(1) - fixture.Clock.GetUtcNow());
                }))));
        await fixture.ExecuteAsync(tenantOwner: false, async services =>
        {
            var handler = services.GetRequiredService<ICommandHandler<PreviewEmailDeliveryDisableCommand,
                BaseCommandResponse<EmailDeliveryDisablePreviewDto>>>();
            if (expire)
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    handler.ExecuteAsync(new(null), CancellationToken.None));
            else
            {
                var result = await handler.ExecuteAsync(new(null), CancellationToken.None);
                await Assert.That(result.IsSuccess).IsTrue();
                await Assert.That(result.Id!.ConfirmationToken).IsNotNull();
            }
            await Assert.That(observed).IsTrue();
            await Assert.That(services.GetRequiredService<ExploreDbContext>().Database.CurrentTransaction).IsNull();
        });
        await using var observer = fixture.CreateIndependentContext();
        await Assert.That((await observer.SystemSettings.AsNoTracking().SingleAsync(row =>
            row.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled)).Value).IsEqualTo("true");
    }

    /// <summary>The real database impact read finishes before the deterministic credential change.</summary>
    private sealed class AfterImpactRead(IEmailDeliveryDisableImpactReader inner, Action afterRead)
        : IEmailDeliveryDisableImpactReader
    {
        public async Task<EmailDeliveryDisableImpactSnapshot?> ReadAsync(
            Guid? tenantId, CancellationToken cancellationToken = default)
        {
            var result = await inner.ReadAsync(tenantId, cancellationToken);
            afterRead();
            return result;
        }
    }
}
