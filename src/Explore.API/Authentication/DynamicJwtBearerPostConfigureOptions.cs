using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace Explore.API.Authentication;

public sealed class DynamicJwtBearerPostConfigureOptions : IPostConfigureOptions<JwtBearerOptions>
{
    private readonly DynamicJwtConfigurationService _dynamicConfig;

    public DynamicJwtBearerPostConfigureOptions(DynamicJwtConfigurationService dynamicConfig)
    {
        _dynamicConfig = dynamicConfig;
    }

    public void PostConfigure(string? name, JwtBearerOptions options)
    {
        if (!string.Equals(name, JwtBearerDefaults.AuthenticationScheme, StringComparison.Ordinal))
        {
            return;
        }

        if (_dynamicConfig.ConfigurationManager is not null)
        {
            options.ConfigurationManager = _dynamicConfig.ConfigurationManager;
        }

        var authority = _dynamicConfig.Authority;
        if (!string.IsNullOrWhiteSpace(authority))
        {
            options.Authority = authority;
            options.TokenValidationParameters.ValidIssuer = authority;
            options.TokenValidationParameters.ValidIssuers = new[] { authority };
        }
    }
}
