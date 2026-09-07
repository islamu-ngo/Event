using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

using Event.Web.BffHosting.Security;
namespace Explore.Blazor.Services.Auth;

public interface IBffAccessTokenAssessmentService
{
    BffAccessTokenAssessment Assess(string? accessToken);

    string? ResolveUserId(ClaimsPrincipal? principal);
}

public readonly record struct BffAccessTokenAssessment(bool IsUsable, string Reason);

public sealed class BffAccessTokenAssessmentService : IBffAccessTokenAssessmentService
{
    private static readonly TimeSpan ExpirySafetyWindow = TimeSpan.FromSeconds(30);

    public BffAccessTokenAssessment Assess(string? accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return new BffAccessTokenAssessment(false, "missing_access_token");
        }

        try
        {
            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(accessToken))
            {
                return new BffAccessTokenAssessment(false, "unreadable_access_token");
            }

            var token = handler.ReadJwtToken(accessToken);
            var validToUtc = token.ValidTo;
            if (validToUtc <= DateTime.UtcNow.Add(ExpirySafetyWindow))
            {
                return new BffAccessTokenAssessment(false, "expired_access_token");
            }

            return new BffAccessTokenAssessment(true, "valid_access_token");
        }
        catch (Exception)
        {
            return new BffAccessTokenAssessment(false, "access_token_parse_failed");
        }
    }

    public string? ResolveUserId(ClaimsPrincipal? principal) =>
        principal.TryGetCircuitSubject(out var subject) ? subject.PartitionKey : null;
}
