using System.IdentityModel.Tokens.Jwt;

namespace Event.Web.BffHosting.Security;

public static class EventBffTokenSafety
{
    public static bool IsTokenForwardable(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(token))
            {
                return true;
            }

            var jwt = handler.ReadJwtToken(token);
            return jwt.ValidTo == DateTime.MinValue || jwt.ValidTo > DateTime.UtcNow;
        }
        catch
        {
            return false;
        }
    }
}
