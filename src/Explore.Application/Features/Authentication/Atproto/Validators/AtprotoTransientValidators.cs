// ABOUTME: Manually validates closed transient purposes, lowercase digests, tenant bindings and lifetime ceilings.
// ABOUTME: Applies UTF-8 payload bounds and injected time independently of the HTTP adapter.

using System.Text;
using Explore.Domain;
using Explore.Application.Features.Authentication.Atproto.Requests.Commands;
using Explore.Application.Features.Authentication.Atproto.Requests.Queries;
using FluentValidation;

namespace Explore.Application.Features.Authentication.Atproto.Validators;

public sealed class CreateAtprotoTransientCommandValidator : AbstractValidator<CreateAtprotoTransientCommand>
{
    public CreateAtprotoTransientCommandValidator(TimeProvider clock)
    {
        RuleFor(x => x.Purpose).Must(AtprotoTransientValidation.IsOrdinaryPurpose);
        RuleFor(x => x.TokenDigest).Must(AtprotoTransientValidation.IsDigest);
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.ProtectedPayload).Must(payload => !string.IsNullOrWhiteSpace(payload)
            && Encoding.UTF8.GetByteCount(payload) <= AtprotoTransientRecord.MaximumProtectedPayloadBytes);
        RuleFor(x => x).Must(request =>
        {
            long now = clock.GetUtcNow().ToUnixTimeMilliseconds();
            long ceiling = request.Purpose == AtprotoTransientPurpose.OAuthState ? 600_000 : 120_000;
            return request.ExpiresAtUnixMilliseconds > now && request.ExpiresAtUnixMilliseconds <= now + ceiling;
        });
    }
}

public sealed class ReadAtprotoTransientQueryValidator : AbstractValidator<ReadAtprotoTransientQuery>
{
    public ReadAtprotoTransientQueryValidator()
    {
        RuleFor(x => x.Purpose).Must(AtprotoTransientValidation.IsOrdinaryPurpose);
        RuleFor(x => x.TokenDigest).Must(AtprotoTransientValidation.IsDigest);
        RuleFor(x => x.ExpectedTenantId).Must(id => id != Guid.Empty);
        RuleFor(x => x).Must(request => request.Purpose == AtprotoTransientPurpose.OAuthState
            || request.ExpectedTenantId.HasValue);
    }
}

public sealed class ConsumeAtprotoTransientCommandValidator : AbstractValidator<ConsumeAtprotoTransientCommand>
{
    public ConsumeAtprotoTransientCommandValidator()
    {
        RuleFor(x => x.Purpose).Must(AtprotoTransientValidation.IsOrdinaryPurpose);
        RuleFor(x => x.TokenDigest).Must(AtprotoTransientValidation.IsDigest);
        RuleFor(x => x.CandidateId).NotEmpty();
        RuleFor(x => x.ExpectedTenantId).NotEmpty();
    }
}

internal static class AtprotoTransientValidation
{
    public static bool IsOrdinaryPurpose(AtprotoTransientPurpose purpose) =>
        purpose is AtprotoTransientPurpose.OAuthState or AtprotoTransientPurpose.TenantHandoff;
    public static bool IsDigest(string? digest) => digest is { Length: 64 }
        && digest.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
