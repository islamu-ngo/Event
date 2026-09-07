using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Channels;
using System.Threading.RateLimiting;
using Explore.API.Authentication;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Instance;
using Explore.Application.DTOs.Onboarding;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure;
using Explore.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using ApiKeyHashing = Explore.Application.Services.ApiKeyHashing;

namespace Event.Api.IntegrationTests.Fixtures;

public sealed class ExternalApiPhase0WebApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestIssuer = "https://phase0-auth.test";

    private const string JwtSigningKey = "phase0-test-signing-key-12345678901234567890";
    private readonly string _databaseName = $"InMemoryDbForExternalApiPhase0_{Guid.NewGuid():N}";

    public DeploymentMode DeploymentMode { get; init; } = DeploymentMode.MultiTenant;

    public Guid DefaultTenantId { get; init; } = PlatformDefaults.DefaultTenantId;

    public bool EnableAuthContextProbe { get; init; } = true;

    public bool TrustLoopbackProxy { get; init; } = true;

    public bool DisableRateLimitingInTesting { get; init; } = true;

    public int? GlobalRateLimitTokenLimit { get; init; }

    public int? GlobalRateLimitTokensPerPeriod { get; init; }

    public int? GlobalRateLimitReplenishPeriodSeconds { get; init; }

    public bool CustomDomainEnabled { get; init; }
    public IAuthProviderConfigurationService? AuthProviderConfigurationServiceOverride { get; init; }


    public bool AllowTenantCustomDomains { get; init; } = true;

    public bool SubdomainEnabled { get; init; }

    public string InstanceBaseDomain { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, Guid> TenantSlugMappings { get; init; } = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, Guid> TenantDomainMappings { get; init; } = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ApiKeyClientDescriptor> ApiKeyClients { get; init; } = [];

    public IReadOnlyList<PersistedApiKeySeed> PersistedApiKeys { get; init; } = [];

    public ISetupSecretProvider? SetupSecretProviderOverride { get; init; }

    public bool McpEnabled { get; init; }

    public bool McpRuntimeEnabled { get; init; } = true;

    public bool McpRuntimeLegacySseRequested { get; init; }

    public bool McpRuntimeLockTenantMcp { get; init; } = true;

    public bool McpRuntimeLockTenantLegacySse { get; init; } = true;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var inMemoryConfig = new Dictionary<string, string?>
            {
                ["Database:Provider"] = "PostgreSql",
                ["Database:Host"] = "localhost",
                ["Database:Port"] = "5432",
                ["Database:Database"] = "explore_db_test",
                ["Database:Runtime:Username"] = "postgres",
                ["Database:Runtime:Password"] = "postgres",
                ["Database:Runtime:TlsMode"] = "Prefer",
                ["Database:Runtime:TrustServerCertificate"] = "false",
                ["Keycloak:Authority"] = TestIssuer,
                ["Keycloak:Realm"] = "ISLAMU",
                ["Keycloak:Audience"] = "islamu-event-api",
                ["Keycloak:RequireHttpsMetadata"] = "false",
                ["Keycloak:MetadataAddress"] = $"{TestIssuer}/.well-known/openid-configuration",
                ["S3Settings:Region"] = "us-east-1",
                ["S3Settings:BucketName"] = "test-bucket",
                ["S3Settings:AccessKeyId"] = "test-key",
                ["S3Settings:SecretAccessKey"] = "test-secret",
                ["S3Settings:Endpoint"] = "https://s3.example.com",
                ["Deployment:Mode"] = DeploymentMode.ToString(),
                ["Deployment:DefaultTenantId"] = DefaultTenantId.ToString(),
                ["Authentication:ApiKeys:HeaderName"] = "X-API-Key",
                ["Diagnostics:EnableAuthContextProbe"] = EnableAuthContextProbe.ToString(),
                ["ForwardedHeadersTrust:TrustLoopbackProxy"] = TrustLoopbackProxy.ToString(),
                ["ForwardedHeadersTrust:ForwardLimit"] = "1",
                ["RateLimiting:DisableInTesting"] = DisableRateLimitingInTesting.ToString(),
                ["Mcp:Enabled"] = McpEnabled.ToString(),
                ["Mcp:EndpointPath"] = "/mcp",
                ["Mcp:Stateless"] = "true",
                ["Mcp:EnableLegacySse"] = "false"
            };

            if (GlobalRateLimitTokenLimit.HasValue)
            {
                inMemoryConfig["RateLimiting:Global:TokenLimit"] = GlobalRateLimitTokenLimit.Value.ToString();
            }

            if (GlobalRateLimitTokensPerPeriod.HasValue)
            {
                inMemoryConfig["RateLimiting:Global:TokensPerPeriod"] = GlobalRateLimitTokensPerPeriod.Value.ToString();
            }

            if (GlobalRateLimitReplenishPeriodSeconds.HasValue)
            {
                inMemoryConfig["RateLimiting:Global:ReplenishPeriodSeconds"] = GlobalRateLimitReplenishPeriodSeconds.Value.ToString();
            }

            for (var index = 0; index < ApiKeyClients.Count; index++)
            {
                var client = ApiKeyClients[index];
                var prefix = $"Authentication:ApiKeys:Clients:{index}";

                inMemoryConfig[$"{prefix}:KeyId"] = client.KeyId;
                inMemoryConfig[$"{prefix}:TenantId"] = client.TenantId.ToString();
                inMemoryConfig[$"{prefix}:OwnerType"] = client.OwnerType;
                inMemoryConfig[$"{prefix}:OwnerId"] = client.OwnerId;
                inMemoryConfig[$"{prefix}:IsActive"] = client.IsActive.ToString();
                inMemoryConfig[$"{prefix}:ExpiresAtUtc"] = client.ExpiresAtUtc?.ToString("O");
                inMemoryConfig[$"{prefix}:SecretHash"] = client.SecretHash;

                for (var scopeIndex = 0; scopeIndex < client.Scopes.Count; scopeIndex++)
                {
                    inMemoryConfig[$"{prefix}:Scopes:{scopeIndex}"] = client.Scopes[scopeIndex];
                }
            }

            config.AddInMemoryCollection(inMemoryConfig);
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveExploreDbContextRegistrations();

            services.AddInMemoryExploreDbContext(_databaseName);

            // Override Redis with in-memory distributed cache for tests
            services.RemoveAll<Microsoft.Extensions.Caching.Distributed.IDistributedCache>();
            services.AddDistributedMemoryCache();

            using var scope = services.BuildServiceProvider().CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            dbContext.Database.EnsureCreated();

            var completedAt = DateTime.UtcNow;
            var bootstrap = InstanceBootstrapState.CreateInteractivePending(
                Guid.CreateVersion7(),
                DeploymentMode,
                completedAt);
            bootstrap.CompleteInteractive(Guid.CreateVersion7(), completedAt);
            dbContext.InstanceBootstrapStates.Add(bootstrap);
            dbContext.SaveChanges();

            dbContext.SystemSettings.AddRange(
                CreateSystemSetting(GovernanceSettingKeys.Mcp.Enabled, McpRuntimeEnabled, isLocked: McpRuntimeLockTenantMcp),
                CreateSystemSetting(GovernanceSettingKeys.Mcp.EnableLegacySse, McpRuntimeLegacySseRequested, isLocked: McpRuntimeLockTenantLegacySse),
                CreateSystemSetting(GovernanceSettingKeys.TenantDelegation.LockMcp, McpRuntimeLockTenantMcp),
                CreateSystemSetting(GovernanceSettingKeys.TenantDelegation.LockMcpLegacySse, McpRuntimeLockTenantLegacySse));
            dbContext.SaveChanges();

            if (PersistedApiKeys.Count > 0)
            {
                dbContext.ExternalApiKeys.AddRange(PersistedApiKeys.Select(seed => new ExternalApiKey
                {
                    Id = Guid.NewGuid(),
                    TenantId = seed.TenantId,
                    Tenant = null!,
                    Name = seed.Name,
                    KeyId = seed.KeyId,
                    SecretHash = ApiKeyHashing.ComputeHash(seed.Secret),
                    Scopes = string.Join(' ', seed.Scopes),
                    OwnerType = seed.OwnerType,
                    OwnerId = seed.OwnerId,
                    ExternalApiKeyStatusId = (int)seed.Status,
                    ExternalApiKeyStatus = null!,
                    ExternalApiKeyCreditPeriodId = (int)ExternalApiKeyCreditPeriodEnum.None,
                    ExternalApiKeyCreditPeriod = null!,
                    ExpiresAt = seed.ExpiresAtUtc?.UtcDateTime
                }));

                dbContext.SaveChanges();
            }
        });

        builder.ConfigureTestServices(services =>
        {
            TestHostServicePruner.RemoveNoisyHostedServices(services);

            if (SetupSecretProviderOverride is not null)
            {
                services.RemoveAll<ISetupSecretProvider>();
            if (AuthProviderConfigurationServiceOverride is not null)
            {
                services.RemoveAll<IAuthProviderConfigurationService>();
                services.AddSingleton(AuthProviderConfigurationServiceOverride);
            }

                services.AddSingleton(SetupSecretProviderOverride);
            }

            if (!DisableRateLimitingInTesting)
            {
                var tokenLimit = GlobalRateLimitTokenLimit ?? 200;
                var tokensPerPeriod = GlobalRateLimitTokensPerPeriod ?? 40;
                var replenishPeriodSeconds = GlobalRateLimitReplenishPeriodSeconds ?? 10;

                services.PostConfigure<RateLimiterOptions>(options =>
                {
                    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                    options.OnRejected = async (ctx, token) =>
                    {
                        ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                        if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                        {
                            ctx.HttpContext.Response.Headers.RetryAfter =
                                ((int)retryAfter.TotalSeconds).ToString(NumberFormatInfo.InvariantInfo);
                        }

                        ctx.HttpContext.Response.Headers["X-RateLimit-Limit"] =
                            tokenLimit.ToString(NumberFormatInfo.InvariantInfo);
                        ctx.HttpContext.Response.Headers["X-RateLimit-Remaining"] = "0";

                        var problemDetailsService = ctx.HttpContext.RequestServices.GetService<IProblemDetailsService>();
                        if (problemDetailsService is null)
                        {
                            return;
                        }

                        await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
                        {
                            HttpContext = ctx.HttpContext,
                            ProblemDetails = new ProblemDetails
                            {
                                Type = "https://tools.ietf.org/html/rfc6585#section-4",
                                Title = "Too Many Requests",
                                Status = StatusCodes.Status429TooManyRequests,
                                Detail = "Rate limit exceeded. Please retry after the period indicated in the Retry-After header.",
                                Instance = ctx.HttpContext.Request.Path,
                                Extensions =
                                {
                                    ["code"] = "rate_limited"
                                }
                            }
                        });
                    };

                    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                    {
                        var apiKeyId = httpContext.User.GetApiKeyId();
                        if (!string.IsNullOrWhiteSpace(apiKeyId))
                        {
                            return RateLimitPartition.GetTokenBucketLimiter(
                                $"api-key:{apiKeyId}",
                                _ => new TokenBucketRateLimiterOptions
                                {
                                    TokenLimit = tokenLimit,
                                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                                    QueueLimit = 0,
                                    ReplenishmentPeriod = TimeSpan.FromSeconds(replenishPeriodSeconds),
                                    TokensPerPeriod = tokensPerPeriod,
                                    AutoReplenishment = true
                                });
                        }

                        var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                        return RateLimitPartition.GetTokenBucketLimiter(
                            $"anonymous:{remoteIp}",
                            _ => new TokenBucketRateLimiterOptions
                            {
                                TokenLimit = tokenLimit,
                                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                                QueueLimit = 0,
                                ReplenishmentPeriod = TimeSpan.FromSeconds(replenishPeriodSeconds),
                                TokensPerPeriod = tokensPerPeriod,
                                AutoReplenishment = true
                            });
                    });
                });
            }

            services.RemoveAll<ITenantSlugCache>();
            services.AddSingleton<ITenantSlugCache>(new TestTenantSlugCache(TenantSlugMappings, TenantDomainMappings));

            services.RemoveAll<IResolverConfigService>();
            services.AddSingleton<IResolverConfigService>(new TestResolverConfigService(CustomDomainEnabled, AllowTenantCustomDomains, SubdomainEnabled, InstanceBaseDomain));

            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSigningKey));

                options.Authority = TestIssuer;
                options.MetadataAddress = string.Empty;
                options.RequireHttpsMetadata = false;
                options.Configuration = new OpenIdConnectConfiguration
                {
                    Issuer = TestIssuer
                };
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(options.Configuration);
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = TestIssuer,
                    ValidateAudience = true,
                    ValidAudience = "islamu-event-api",
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKey,
                    NameClaimType = ClaimTypes.NameIdentifier
                };
                options.Events = new JwtBearerEvents();
            });
        });
    }

    public string CreateJwt(Guid userId, IEnumerable<Claim>? additionalClaims = null)
    {
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSigningKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new("sub", userId.ToString()),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, "Phase0 User")
        };

        if (additionalClaims is not null)
        {
            claims.AddRange(additionalClaims);
        }

        var token = new JwtSecurityToken(
            issuer: TestIssuer,
            audience: "islamu-event-api",
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public override async ValueTask DisposeAsync()
    {
        try
        {
            await base.DisposeAsync();
        }
        catch (ChannelClosedException ex)
        {
            // Testing host shutdown can race background channels after response assertions complete.
            Console.WriteLine($"Ignoring WebApplicationFactory teardown ChannelClosedException: {ex.Message}");
        }
        catch (NullReferenceException ex)
        {
            // Keep parity with the broader test-host teardown workaround used in legacy fixtures.
            Console.WriteLine($"Ignoring WebApplicationFactory teardown NullReferenceException: {ex.Message}");
        }
        catch (ObjectDisposedException ex)
        {
            // Some providers (like OpenFeature) may already be disposed or in the process of disposing.
            Console.WriteLine($"Ignoring WebApplicationFactory teardown ObjectDisposedException: {ex.Message}");
        }
    }

    private static SystemSetting CreateSystemSetting(string key, bool value, bool isLocked = false)
    {
        return new SystemSetting
        {
            Id = Guid.NewGuid(),
            SettingKey = key,
            Value = value ? "true" : "false",
            ValueType = SettingValueType.Boolean,
            IsLocked = isLocked,
            Category = "Mcp",
            CreatedAt = DateTime.UtcNow
        };
    }

    private sealed class TestResolverConfigService : IResolverConfigService
    {
        private readonly ResolverConfigurationDto _configuration;

        public TestResolverConfigService(bool customDomainEnabled, bool allowTenantCustomDomains, bool subdomainEnabled, string instanceBaseDomain)
        {
            _configuration = new ResolverConfigurationDto
            {
                HeaderEnabled = true,
                PathEnabled = true,
                SubdomainEnabled = subdomainEnabled,
                CustomDomainEnabled = customDomainEnabled,
                AllowTenantCustomDomains = allowTenantCustomDomains,
                InstanceBaseDomain = instanceBaseDomain
            };
        }

        public Task<ResolverConfigurationDto> GetConfigurationAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_configuration);
        }

        public Task ApplyConfigurationAsync(
            PatchResolverConfigurationDto patch,
            ResolverConfigurationDto configuration,
            Guid? actorUserId,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public void InvalidateCache()
        {
        }
    }

    private sealed class TestTenantSlugCache : ITenantSlugCache
    {
        private readonly IReadOnlyDictionary<string, Guid> _slugMappings;
        private readonly IReadOnlyDictionary<string, Guid> _domainMappings;

        public TestTenantSlugCache(IReadOnlyDictionary<string, Guid> slugMappings, IReadOnlyDictionary<string, Guid> domainMappings)
        {
            _slugMappings = slugMappings;
            _domainMappings = domainMappings;
        }

        public Task WarmAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public ValueTask<Guid?> GetTenantIdBySlugAsync(string slug, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(_slugMappings.TryGetValue(slug, out var tenantId) ? (Guid?)tenantId : null);
        }

        public ValueTask<Guid?> GetTenantIdByDomainAsync(string domain, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(_domainMappings.TryGetValue(domain, out var tenantId) ? (Guid?)tenantId : null);
        }
    }

    public sealed class PersistedApiKeySeed
    {
        public string Name { get; init; } = "Phase0 persisted key";

        public string KeyId { get; init; } = string.Empty;

        public string Secret { get; init; } = string.Empty;

        public Guid? TenantId { get; init; }

        public ExternalApiKeyOwnerType OwnerType { get; init; } = ExternalApiKeyOwnerType.User;

        public Guid OwnerId { get; init; }

        public ExternalApiKeyStatusEnum Status { get; init; } = ExternalApiKeyStatusEnum.Active;

        public DateTimeOffset? ExpiresAtUtc { get; init; }

        public IReadOnlyList<string> Scopes { get; init; } = [];
    }
}
