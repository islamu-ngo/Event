using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Notifications.Requests.Commands;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Notifications.Handlers.Commands;

public sealed class SubscribeCurrentUserWebPushSubscriptionCommandHandler(
    IWebPushSubscriptionRepository repository,
    ITenantContext tenantContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<SubscribeCurrentUserWebPushSubscriptionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        SubscribeCurrentUserWebPushSubscriptionCommand request,
        CancellationToken cancellationToken)
    {
        var validationErrors = Validate(request);
        if (validationErrors.Count > 0)
        {
            return Failure("Web Push subscription validation failed.", validationErrors);
        }

        var userId = currentUserService.UserId;
        if (!currentUserService.IsAuthenticated || !userId.HasValue)
        {
            return Failure("User not authenticated.");
        }

        try
        {
            var subscription = await repository.UpsertAsync(
                tenantContext.TenantId,
                userId.Value,
                request.DeviceIdentifier,
                request.Endpoint,
                request.P256Dh,
                request.Auth,
                request.ExpirationTime,
                DateTime.UtcNow,
                cancellationToken);

            return BaseCommandResponse.Success(subscription.Id, "Web Push subscription saved.");
        }
        catch (ArgumentException ex)
        {
            return Failure(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Failure(ex.Message);
        }
    }

    private static List<string> Validate(SubscribeCurrentUserWebPushSubscriptionCommand request)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.DeviceIdentifier))
            errors.Add("Device identifier is required.");
        else if (request.DeviceIdentifier.Length > 200)
            errors.Add("Device identifier must not exceed 200 characters.");

        if (!Uri.TryCreate(request.Endpoint, UriKind.Absolute, out var endpoint)
            || !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(endpoint.Host)
            || !string.IsNullOrEmpty(endpoint.UserInfo))
        {
            errors.Add("Endpoint must be an absolute HTTPS URL without user information.");
        }
        else if (request.Endpoint.Length > 2000)
        {
            errors.Add("Endpoint must not exceed 2000 characters.");
        }

        if (!HasDecodedLength(request.P256Dh, 65))
            errors.Add("P256DH key must be a 65-byte URL-safe Base64 value.");
        if (!HasDecodedLength(request.Auth, 16))
            errors.Add("Auth secret must be a 16-byte URL-safe Base64 value.");
        if (request.ExpirationTime is DateTime expirationTime && expirationTime <= DateTime.UtcNow)
            errors.Add("Subscription expiration time must be in the future.");

        return errors;
    }

    private static bool HasDecodedLength(string value, int expectedLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            return false;
        }

        try
        {
            var base64 = value.Replace('-', '+').Replace('_', '/');
            base64 += new string('=', (4 - base64.Length % 4) % 4);
            return Convert.FromBase64String(base64).Length == expectedLength;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static BaseCommandResponse<Guid> Failure(string message, List<string>? errors = null) =>
        BaseCommandResponse.Validation<Guid>(errors ?? [message], message);
}
