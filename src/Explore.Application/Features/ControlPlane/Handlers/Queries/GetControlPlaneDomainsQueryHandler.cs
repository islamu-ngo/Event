using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.Features.ControlPlane.Requests.Queries;
using MediatR;
using Microsoft.Extensions.Configuration;

namespace Explore.Application.Features.ControlPlane.Handlers.Queries;

public sealed class GetControlPlaneDomainsQueryHandler(
    IInstanceGovernanceSettingService governanceSettingService,
    IConfiguration configuration)
    : IRequestHandler<GetControlPlaneDomainsQuery, ControlPlaneDomainOverviewDto>
{
    public async Task<ControlPlaneDomainOverviewDto> Handle(
        GetControlPlaneDomainsQuery request,
        CancellationToken cancellationToken)
    {
        _ = request;
        _ = cancellationToken;

        var governanceSettings = await governanceSettingService.ReadSettingsAsync();
        var domains = governanceSettings.Domains;

        var publicOrigin = FirstConfiguredValue(
            configuration["PublicBaseUrl"],
            configuration["App:PublicBaseUrl"]);
        var adminOrigin = FirstConfiguredValue(
            configuration["ControlPlane:PublicOrigin"],
            configuration["Bff:PublicOrigin"],
            configuration["CONTROL_PLANE_PUBLIC_ORIGIN"]);

        var instanceBaseDomain = NormalizeHost(domains.InstanceBaseDomain);
        var publicHost = HostFromOrigin(publicOrigin) ?? instanceBaseDomain;
        var adminHost = HostFromOrigin(adminOrigin) ?? NormalizeHost(domains.AdminHost);
        var wildcardTenantHost = string.IsNullOrWhiteSpace(instanceBaseDomain)
            ? null
            : $"*.{instanceBaseDomain}";

        return new ControlPlaneDomainOverviewDto
        {
            PublicOrigin = publicOrigin,
            PublicPlatformHost = publicHost,
            InstanceBaseDomain = instanceBaseDomain,
            WildcardTenantHost = wildcardTenantHost,
            AdminOrigin = adminOrigin,
            AdminHost = adminHost,
            AllowTenantCustomDomains = domains.AllowTenantCustomDomains,
            LockTenantSubdomain = domains.LockTenantSubdomain,
            LockTenantCustomDomain = domains.LockTenantCustomDomain,
            DnsRecords = BuildDnsRecords(publicHost, instanceBaseDomain, wildcardTenantHost, adminHost, domains.AllowTenantCustomDomains),
            Warnings = BuildWarnings(publicHost, instanceBaseDomain, wildcardTenantHost, adminHost)
        };
    }

    private static IReadOnlyList<ControlPlaneDnsRecordDto> BuildDnsRecords(
        string? publicHost,
        string? instanceBaseDomain,
        string? wildcardTenantHost,
        string? adminHost,
        bool allowTenantCustomDomains)
    {
        var publicTarget = publicHost ?? instanceBaseDomain ?? "reverse proxy / public Event host";

        return
        [
            new()
            {
                Purpose = "Public platform",
                RecordType = "A/AAAA or CNAME",
                Name = publicHost ?? "(configure public origin or instance base domain)",
                Target = "reverse proxy / public Event host",
                Required = true,
                Status = publicHost is null ? "missing_configuration" : "configured",
                Guidance = "Routes the main public Event platform host to the deployment reverse proxy."
            },
            new()
            {
                Purpose = "Tenant subdomains",
                RecordType = "Wildcard A/AAAA or CNAME",
                Name = wildcardTenantHost ?? "(configure instance base domain)",
                Target = "reverse proxy / public Event host",
                Required = true,
                Status = wildcardTenantHost is null ? "missing_configuration" : "configured",
                Guidance = "Enables tenant subdomain routing such as tenant.example.org in multi-tenant mode."
            },
            new()
            {
                Purpose = "Control plane",
                RecordType = "A/AAAA or CNAME",
                Name = adminHost ?? "(optional dedicated admin host)",
                Target = "control-plane BFF host or shared reverse proxy",
                Required = false,
                Status = adminHost is null ? "optional_not_configured" : "configured",
                Guidance = "Provides a dedicated operator address such as admin.example.org when configured."
            },
            new()
            {
                Purpose = "Custom tenant domains",
                RecordType = "Customer-owned CNAME",
                Name = "customer-owned tenant host",
                Target = publicTarget,
                Required = false,
                Status = ResolveCustomDomainStatus(allowTenantCustomDomains, publicHost),
                Guidance = "Customer domains should CNAME to the public platform host or reverse-proxy target."
            }
        ];
    }

    private static IReadOnlyList<ControlPlaneWarningDto> BuildWarnings(
        string? publicHost,
        string? instanceBaseDomain,
        string? wildcardTenantHost,
        string? adminHost)
    {
        var warnings = new List<ControlPlaneWarningDto>();

        if (string.IsNullOrWhiteSpace(publicHost))
        {
            warnings.Add(new ControlPlaneWarningDto
            {
                Code = "public_platform_host_missing",
                Severity = "warning",
                Message = "No public platform host can be derived from configuration or instance domain settings."
            });
        }

        if (string.IsNullOrWhiteSpace(instanceBaseDomain) || string.IsNullOrWhiteSpace(wildcardTenantHost))
        {
            warnings.Add(new ControlPlaneWarningDto
            {
                Code = "wildcard_tenant_domain_missing",
                Severity = "warning",
                Message = "Tenant subdomain routing needs an instance base domain and matching wildcard DNS record."
            });
        }

        if (string.IsNullOrWhiteSpace(adminHost))
        {
            warnings.Add(new ControlPlaneWarningDto
            {
                Code = "control_plane_host_not_configured",
                Severity = "info",
                Message = "Dedicated control-plane host access is not configured; embedded administration remains available."
            });
        }

        return warnings;
    }

    private static string ResolveCustomDomainStatus(bool allowTenantCustomDomains, string? publicHost)
    {
        if (!allowTenantCustomDomains)
        {
            return "disabled";
        }

        return string.IsNullOrWhiteSpace(publicHost) ? "needs_public_target" : "available";
    }

    private static string? HostFromOrigin(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
        {
            return null;
        }

        if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return NormalizeHost(uri.Host);
        }

        return NormalizeHost(origin);
    }

    private static string? FirstConfiguredValue(params string?[] values) =>
        values.Select(NullIfWhiteSpace).FirstOrDefault(value => value is not null);

    private static string? NormalizeHost(string? value)
    {
        var normalized = NullIfWhiteSpace(value)?
            .Trim()
            .TrimEnd('/')
            .ToLowerInvariant();

        if (normalized is null)
        {
            return null;
        }

        if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
        {
            normalized = uri.Host;
        }

        return normalized.TrimStart('.').TrimEnd('.');
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
