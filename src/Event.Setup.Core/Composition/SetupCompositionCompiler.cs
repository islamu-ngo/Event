namespace ISLAMU.Event.Setup.Core.Composition;

using ISLAMU.Wire.Contracts.ConfigurationPortability;

public sealed class SetupCompositionCompiler
{
    private readonly SetupCompositionLimits _limits;

    public SetupCompositionCompiler() : this(SetupCompositionLimits.Default) { }

    public SetupCompositionCompiler(SetupCompositionLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        _limits = limits with { };
    }

    public ValueTask<SetupCompositionResult> CompileAsync(
        SetupCompositionSource source, CancellationToken cancellationToken = default) =>
        CompileAsync(source, SetupCompositionImmediatePublicationCommitBarrier.Instance, cancellationToken);

    public async ValueTask<SetupCompositionResult> CompileAsync(
        SetupCompositionSource source, ISetupCompositionPublicationCommitBarrier publicationCommitBarrier,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(publicationCommitBarrier);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            CompositionMap normalized;
            bool barrierAlreadyApplied = false;
            switch (source)
            {
                case SetupCompositionJsonSource json:
                    normalized = SetupCompositionNormalizer.ParseJson(
                        json.Bytes, _limits, cancellationToken);
                    break;
                case SetupCompositionYamlSource yaml:
                    normalized = SetupCompositionYamlParser.Parse(
                        yaml.Bytes, _limits, cancellationToken);
                    break;
                case SetupCompositionDirectorySource directory:
                    normalized = await SetupCompositionDirectoryReader.ReadAsync(
                        directory, _limits, publicationCommitBarrier, cancellationToken).ConfigureAwait(false);
                    barrierAlreadyApplied = true;
                    break;
                default:
                    throw new SetupCompositionException(SetupCompositionFailureCode.InvalidSource);
            }

            cancellationToken.ThrowIfCancellationRequested();
            byte[] normalizedJson = SetupCompositionNormalizer.WriteJson(normalized, cancellationToken);
            if (normalizedJson.Length > _limits.AggregateSourceBytes)
                throw new SetupCompositionException(SetupCompositionFailureCode.LimitExceeded);
            SetupCompositionResult result = CompileWire(normalized, normalizedJson, cancellationToken);
            if (!barrierAlreadyApplied)
                await publicationCommitBarrier.AwaitPublicationCommitAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException)
        {
            return SetupCompositionResult.Failed(SetupCompositionFailureCode.Cancelled);
        }
        catch (SetupCompositionException exception)
        {
            return SetupCompositionResult.Failed(exception.Code);
        }
        catch (ConfigurationPortabilityContractException exception)
        {
            return SetupCompositionResult.Failed(MapContractFailure(exception.Code));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException or OverflowException)
        {
            return SetupCompositionResult.Failed(SetupCompositionFailureCode.InvalidSource);
        }
    }

    private static SetupCompositionResult CompileWire(
        CompositionMap normalized, byte[] normalizedJson, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!normalized.Entries.TryGetValue("kind", out CompositionNode? kindNode)
            || kindNode is not CompositionScalar { Kind: CompositionScalarKind.String, Value: not null } kind)
            throw new SetupCompositionException(SetupCompositionFailureCode.ContractInvalid);

        if (string.Equals(kind.Value, ConfigurationManifestContractMetadata.Kind, StringComparison.Ordinal))
        {
            ConfigurationManifestV1Alpha2 parsed =
                ConfigurationPortabilityJsonCodec.ParseConfigurationManifest(normalizedJson);
            cancellationToken.ThrowIfCancellationRequested();
            byte[] canonical = ConfigurationPortabilityJsonCodec.SerializeConfigurationManifest(parsed);
            cancellationToken.ThrowIfCancellationRequested();
            ConfigurationManifestV1Alpha2 final =
                ConfigurationPortabilityJsonCodec.ParseConfigurationManifest(canonical);
            byte[] finalBytes = ConfigurationPortabilityJsonCodec.SerializeConfigurationManifest(final);
            return SetupCompositionResult.ManifestSuccess(final, finalBytes);
        }

        if (string.Equals(kind.Value, TenantConfigurationPackageContractMetadata.Kind, StringComparison.Ordinal))
        {
            TenantConfigurationPackageV1Alpha2 parsed =
                ConfigurationPortabilityJsonCodec.ParseTenantConfigurationPackage(normalizedJson);
            cancellationToken.ThrowIfCancellationRequested();
            byte[] canonical = ConfigurationPortabilityJsonCodec.SerializeTenantConfigurationPackage(parsed);
            cancellationToken.ThrowIfCancellationRequested();
            TenantConfigurationPackageV1Alpha2 final =
                ConfigurationPortabilityJsonCodec.ParseTenantConfigurationPackage(canonical);
            byte[] finalBytes = ConfigurationPortabilityJsonCodec.SerializeTenantConfigurationPackage(final);
            return SetupCompositionResult.PackageSuccess(final, finalBytes);
        }

        throw new SetupCompositionException(SetupCompositionFailureCode.ContractInvalid);
    }

    private static SetupCompositionFailureCode MapContractFailure(string code) => code switch
    {
        ConfigurationPortabilityDiagnosticCodes.TooLarge
            or ConfigurationPortabilityDiagnosticCodes.DepthExceeded
            or ConfigurationPortabilityDiagnosticCodes.CountExceeded
            or ConfigurationPortabilityDiagnosticCodes.StringTooLong => SetupCompositionFailureCode.LimitExceeded,
        ConfigurationPortabilityDiagnosticCodes.SensitiveMemberForbidden
            or ConfigurationPortabilityDiagnosticCodes.ScopeInvalid => SetupCompositionFailureCode.ForbiddenAuthority,
        _ => SetupCompositionFailureCode.ContractInvalid
    };
}
