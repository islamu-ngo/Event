using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Features.Authentication.Local.Validators;
using Explore.Application.Features.Authentication.Local.Requests.Commands;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.Authentication.Local.Handlers.Commands;

public sealed class LocalRegisterCommandHandler(
    ILocalIdentityAuthService authService,
    IAuthenticationProviderDispatcher providerDispatcher,
    ISender sender)
    : IRequestHandler<LocalRegisterCommand, LocalRegistrationResponseDto>
{
    public async Task<LocalRegistrationResponseDto> Handle(
        LocalRegisterCommand request,
        CancellationToken cancellationToken)
    {
        var validation = await new LocalRegistrationRequestDtoValidator()
            .ValidateAsync(request.Request, cancellationToken)
            .ConfigureAwait(false);
        if (!validation.IsValid)
        {
            return LocalRegistrationResponseDto.Failed("invalid_request");
        }

        if (await providerDispatcher.GetActivePrimaryProviderAsync(cancellationToken)
                .ConfigureAwait(false)
            != AuthenticationProviderKind.Local)
        {
            return LocalRegistrationResponseDto.Failed("provider_inactive");
        }

        LocalRegistrationResponseDto registration = await authService
            .RegisterAsync(request.Request, cancellationToken)
            .ConfigureAwait(false);
        if (!registration.Success || registration.Authentication is not { } authentication)
        {
            return registration;
        }

        var synchronization = await sender.Send(
            LocalIdentitySyncCommandFactory.Create(authentication),
            cancellationToken).ConfigureAwait(false);
        return synchronization.IsSuccess
            ? registration
            : LocalRegistrationResponseDto.Failed("user_sync_failed");
    }
}
