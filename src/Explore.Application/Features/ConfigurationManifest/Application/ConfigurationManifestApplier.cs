using Explore.Application.Features.ConfigurationManifest.Compilation;
using Explore.Application.Features.ConfigurationManifest.Ingestion;
using Explore.Application.Responses;

namespace Explore.Application.Features.ConfigurationManifest.Application;

/// <summary>
/// Parsed caller results support validation only. Bootstrap always originates from
/// the actual post-migration deployment source and its distinct nonhuman owner.
/// </summary>
public sealed class ConfigurationManifestApplier(
    IConfigurationManifestBootstrapOwnerFactory owner,
    IConfigurationManifestBootstrapExecutor bootstrap) : IConfigurationManifestApplier
{
    public Task<BaseCommandResponse<Guid>?> ApplyOriginalStartupAsync(CancellationToken cancellationToken) =>
        bootstrap.ApplyOriginalStartupAsync(cancellationToken);

    public async Task<BaseCommandResponse<Guid>> ApplyAsync(
        ConfigurationManifestReadResult source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Mode != ConfigurationManifestMode.ValidateOnly)
            throw new UnauthorizedAccessException("Parsed manifest input cannot originate a bootstrap mutation.");
        Guid id = Guid.CreateVersion7();
        ConfigurationManifestApplyPlan plan;
        try { plan = ConfigurationManifestCompiler.Compile(source, id, DateTime.UtcNow); }
        catch (ConfigurationManifestCompilationException exception)
        {
            return BaseCommandResponse.Failure<Guid>(exception.FailureCode, exception.Message, [exception.FailureCode], id);
        }
        var result = await owner.ValidateAsync(plan, cancellationToken);
        if (!result.IsValid)
        {
            var first = result.Errors[0];
            return BaseCommandResponse.Failure<Guid>(first.Code, first.Message,
                result.Errors.Select(error => $"{error.Code}:{error.Key}").ToList(), id);
        }
        return BaseCommandResponse.Success(id, "Configuration manifest validated.");
    }
}
