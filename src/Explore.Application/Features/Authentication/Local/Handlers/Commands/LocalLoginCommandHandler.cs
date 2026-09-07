using Explore.Application.Authentication;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Features.Authentication.Local.Validators;
using Explore.Application.DTOs.User;
using Explore.Application.Features.Authentication.Local.Requests.Commands;
using Explore.Application.Features.Users.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.Authentication.Local.Handlers.Commands;

public sealed class LocalLoginCommandHandler(
    ILocalIdentityAuthService authService,
    IAuthenticationProviderDispatcher providerDispatcher,
    ISender sender)
    : IRequestHandler<LocalLoginCommand, LocalAuthResponseDto>
{
    public async Task<LocalAuthResponseDto> Handle(
        LocalLoginCommand request,
        CancellationToken cancellationToken)
    {
        var validation = await new LocalAuthRequestDtoValidator()
            .ValidateAsync(request.Request, cancellationToken)
            .ConfigureAwait(false);
        if (!validation.IsValid)
        {
            return LocalAuthResponseDto.Failed("invalid_request");
        }

        if (await providerDispatcher.GetActivePrimaryProviderAsync(cancellationToken)
                .ConfigureAwait(false)
            != AuthenticationProviderKind.Local)
        {
            return LocalAuthResponseDto.Failed("provider_inactive");
        }

        LocalAuthResponseDto authentication = await authService
            .AuthenticateAsync(request.Request, cancellationToken)
            .ConfigureAwait(false);
        if (!authentication.Success)
        {
            return authentication;
        }

        var synchronization = await sender.Send(
            LocalIdentitySyncCommandFactory.Create(authentication),
            cancellationToken).ConfigureAwait(false);
        return synchronization.IsSuccess
            ? authentication
            : LocalAuthResponseDto.Failed("user_sync_failed");
    }
}

internal static class LocalIdentitySyncCommandFactory
{
    internal static SyncUserCommand Create(LocalAuthResponseDto authentication)
    {
        if (!authentication.Success)
        {
            throw new ArgumentException(
                "Only successful local authentication can be synchronized.",
                nameof(authentication));
        }

        Guid userId = authentication.UserId!.Value;
        return new SyncUserCommand
        {
            AccountKey = new ProviderAccountKey(
                AuthenticationProviderKind.Local,
                userId.ToString("D")),
            UserDto = new UserDto
            {
                Id = userId,
                Email = authentication.Email!,
                FirstName = authentication.FirstName!,
                LastName = authentication.LastName!,
                AuthProvider = AuthenticationProviderKind.Local.ToAuthenticationProviderCode(),
                AuthProviderId = userId.ToString("D"),
                EmailVerified = authentication.EmailVerified
            }
        };
    }
}
