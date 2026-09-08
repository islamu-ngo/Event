using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Event.Benchmarks.Api;

internal sealed class ApiBenchmarkAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string AuthHeaderName = "X-Benchmark-Auth";
    public const string SchemeName = "BenchmarkScheme";

    public static string CreateUserHeaderValue(Guid userId, string name = "Benchmark User")
    {
        var claims = new List<BenchmarkClaim>
        {
            new(ClaimTypes.Name, name),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new("sub", userId.ToString())
        };

        var json = JsonSerializer.Serialize(claims);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(AuthHeaderName, out var headerValue) ||
            string.IsNullOrWhiteSpace(headerValue.ToString()))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(headerValue.ToString()));
            var benchmarkClaims = JsonSerializer.Deserialize<List<BenchmarkClaim>>(json);

            if (benchmarkClaims is null or { Count: 0 })
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = benchmarkClaims.Select(claim => new Claim(claim.Type, claim.Value));
            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
        catch (FormatException ex)
        {
            return Task.FromResult(AuthenticateResult.Fail($"Invalid benchmark auth header: {ex.Message}"));
        }
        catch (JsonException ex)
        {
            return Task.FromResult(AuthenticateResult.Fail($"Invalid benchmark auth payload: {ex.Message}"));
        }
    }

    private sealed record BenchmarkClaim(string Type, string Value);
}
