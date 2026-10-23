using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Fixtures;

/// <summary>Owns a native admitted notification execution scope on the host fixture database.</summary>
internal sealed class NotificationOutboxExecutionFixture : IAsyncDisposable
{
    private readonly AsyncServiceScope _scope;

    private NotificationOutboxExecutionFixture(AsyncServiceScope scope, OutboxMessage message)
    {
        _scope = scope;
        Message = message;
    }

    public IServiceProvider Services => _scope.ServiceProvider;
    public OutboxMessage Message { get; }

    public static async Task<NotificationOutboxExecutionFixture> ClaimAndBindAsync(
        IServiceProvider services, Guid messageId, CancellationToken cancellationToken = default)
    {
        NotificationOutboxClaim claim;
        await using (var admissionScope = services.CreateAsyncScope())
        {
            var claims = await admissionScope.ServiceProvider.GetRequiredService<INotificationOutboxAdmission>()
                .ClaimDueAsync(1000, DateTime.UtcNow, cancellationToken);
            claim = claims.Single(value => value.MessageId == messageId);
        }
        var scope = services.CreateAsyncScope();
        try
        {
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(claim.TenantId);
            await scope.ServiceProvider.GetRequiredService<INotificationOutboxExecutionAuthority>()
                .BindAsync(claim, cancellationToken);
            var message = await scope.ServiceProvider.GetRequiredService<IOutboxRepository>()
                .GetClaimedNotificationAsync(messageId, claim.ProcessingLeaseExpiresAt, cancellationToken)
                ?? throw new InvalidOperationException("The native notification claim is unavailable.");
            return new NotificationOutboxExecutionFixture(scope, message);
        }
        catch
        {
            await scope.DisposeAsync();
            throw;
        }
    }

    public ValueTask DisposeAsync() => _scope.DisposeAsync();
}
