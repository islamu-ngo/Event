// Reads auth state from a custom request header (X-Test-Auth) to avoid shared mutable static state.
// No header = anonymous (NoResult). Header present = authenticated with encoded claims.

using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Event.Api.IntegrationTests.Fixtures;

public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>
    /// Custom header name that carries serialized claims for test authentication.
    /// </summary>
    public const string AuthHeaderName = "X-Test-Auth";

    public const string SchemeName = "TestScheme";

    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // No custom header = anonymous/unauthenticated request
        if (!Request.Headers.TryGetValue(AuthHeaderName, out var headerValue) ||
            string.IsNullOrEmpty(headerValue.ToString()))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        try
        {
            // Decode claims from Base64-encoded JSON
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(headerValue.ToString()));
            var claimDtos = JsonSerializer.Deserialize<List<TestClaimDto>>(json);

            if (claimDtos is null or { Count: 0 })
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = claimDtos.Select(c => new Claim(c.Type, c.Value)).ToList();
            var identity = new ClaimsIdentity(claims, "Test");
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
        catch (Exception ex)
        {
            return Task.FromResult(AuthenticateResult.Fail($"Failed to parse test auth header: {ex.Message}"));
        }
    }

    #region Static Helper Methods (generate header values for HttpClient)

    /// <summary>
    /// Creates the X-Test-Auth header value for an authenticated regular user.
    /// </summary>
    public static string CreateAuthHeaderValue(Guid userId, string name = "Test User")
    {
        var claims = new List<TestClaimDto>
        {
            new(ClaimTypes.Name, name),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new("sub", userId.ToString()),
        };
        return EncodeClaims(claims);
    }

    /// <summary>
    /// Creates the X-Test-Auth header value for a user with additional claims.
    /// </summary>
    public static string CreateAuthHeaderValue(Guid userId, string name, params (string Type, string Value)[] additionalClaims)
    {
        var claims = new List<TestClaimDto>
        {
            new(ClaimTypes.Name, name),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new("sub", userId.ToString()),
        };
        claims.AddRange(additionalClaims.Select(c => new TestClaimDto(c.Type, c.Value)));
        return EncodeClaims(claims);
    }

    /// <summary>
    /// Creates the X-Test-Auth header value for an instance admin.
    /// </summary>
    public static string CreateInstanceAdminHeaderValue(Guid userId, string name = "Instance Admin")
    {
        return CreateAuthHeaderValue(userId, name, ("explore:admin:instance", "true"));
    }

    /// <summary>
    /// Creates the X-Test-Auth header value for a tenant admin.
    /// </summary>
    public static string CreateTenantAdminHeaderValue(Guid userId, Guid tenantId, string name = "Tenant Admin")
    {
        return CreateAuthHeaderValue(userId, name, ("explore:admin:tenant", tenantId.ToString()));
    }

    private static string EncodeClaims(List<TestClaimDto> claims)
    {
        var json = JsonSerializer.Serialize(claims);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    #endregion

    /// <summary>
    /// Minimal DTO for serializing claims in the test auth header.
    /// </summary>
    public record TestClaimDto(string Type, string Value);
}
