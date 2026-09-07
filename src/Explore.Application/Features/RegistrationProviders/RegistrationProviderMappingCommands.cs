using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services.Registration;
using Explore.Application.Responses;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.Enums;
using FluentValidation;
using MediatR;
using System.Security.Cryptography;
using System.Text;

namespace Explore.Application.Features.RegistrationProviders.Commands;

public sealed record ImportRegistrationProviderSchemaRevisionCommand(
    Guid TenantId,
    Guid ConnectionId,
    RegistrationProviderSchemaAuthorityEnum Authority,
    RegistrationEvidenceHash RevisionHash,
    DateTime ObservedAt) : IRequest<BaseCommandResponse<Guid>>;

public sealed record ReplaceDraftRegistrationProviderMappingsCommand(
    Guid TenantId,
    Guid BindingId,
    IReadOnlyList<RegistrationProviderFieldMappingInput> Fields,
    IReadOnlyList<RegistrationProviderOptionMappingInput> Options) : IRequest<BaseCommandResponse<Guid>>;

public sealed record PublishRegistrationProviderBindingCommand(
    Guid TenantId,
    Guid BindingId,
    RegistrationProviderSchemaDriftClass DriftClass,
    DateTime PublishedAt) : IRequest<BaseCommandResponse<Guid>>;

public sealed record RegistrationProviderFieldMappingInput(string PlatformFieldKey, string ProviderFieldKey, bool IsRequired);
public sealed record RegistrationProviderOptionMappingInput(string PlatformFieldKey, string PlatformOptionKey, string ProviderOptionKey);

public sealed class ImportRegistrationProviderSchemaRevisionCommandHandler(IRegistrationProviderRepository repository)
    : IRequestHandler<ImportRegistrationProviderSchemaRevisionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(ImportRegistrationProviderSchemaRevisionCommand request, CancellationToken cancellationToken)
    {
        await new ImportRegistrationProviderSchemaRevisionCommandValidator().ValidateAndThrowAsync(request, cancellationToken);
        if (await repository.GetConnectionAsync(request.TenantId, request.ConnectionId, cancellationToken) is null)
        {
            return RegistrationProviderCommandResponses.Failure(Guid.Empty, "registration_provider_connection_not_found");
        }

        RegistrationProviderSchemaRevision revision = RegistrationProviderSchemaRevision.Create(
            request.TenantId,
            request.ConnectionId,
            request.Authority,
            request.RevisionHash,
            "legacy",
            null,
            "{\"schema\":\"legacy.registration-provider-schema-revision\",\"fields\":[]}",
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(request.RevisionHash.Value))),
            RegistrationProviderDriftClassEnum.NoDrift,
            request.ObservedAt);
        await repository.AddSchemaRevisionAsync(revision, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return RegistrationProviderCommandResponses.Success(revision.Id, "Registration provider schema revision imported.");
    }
}

public sealed class ReplaceDraftRegistrationProviderMappingsCommandHandler(IRegistrationProviderRepository repository)
    : IRequestHandler<ReplaceDraftRegistrationProviderMappingsCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(ReplaceDraftRegistrationProviderMappingsCommand request, CancellationToken cancellationToken)
    {
        await new ReplaceDraftRegistrationProviderMappingsCommandValidator().ValidateAndThrowAsync(request, cancellationToken);
        RegistrationProviderBinding? binding = await repository.GetBindingAsync(request.TenantId, request.BindingId, cancellationToken);
        if (binding is null) return RegistrationProviderCommandResponses.Failure(request.BindingId, "registration_provider_binding_not_found");
        if (binding.StateId != (int)RegistrationProviderBindingStateEnum.Draft)
        {
            return RegistrationProviderCommandResponses.Failure(request.BindingId, "registration_provider_binding_not_draft");
        }

        if (await repository.HasSubmissionForBindingAsync(request.TenantId, request.BindingId, cancellationToken))
        {
            return RegistrationProviderCommandResponses.Failure(request.BindingId, "registration_provider_mapping_pinned");
        }

        if (HasDuplicates(request.Fields.Select(field => Normalize(field.PlatformFieldKey))))
        {
            return RegistrationProviderCommandResponses.Failure(request.BindingId, "registration_provider_duplicate_field_mapping");
        }

        if (HasDuplicates(request.Fields.Select(field => Normalize(field.ProviderFieldKey))))
        {
            return RegistrationProviderCommandResponses.Failure(request.BindingId, "registration_provider_duplicate_provider_field_mapping");
        }

        HashSet<string> platformFields = [.. request.Fields.Select(field => Normalize(field.PlatformFieldKey))];
        if (request.Options.Any(option => !platformFields.Contains(Normalize(option.PlatformFieldKey))))
        {
            return RegistrationProviderCommandResponses.Failure(request.BindingId, "registration_provider_option_field_not_found");
        }

        if (HasDuplicates(request.Options.Select(option => $"{Normalize(option.PlatformFieldKey)}\u001f{Normalize(option.PlatformOptionKey)}")))
        {
            return RegistrationProviderCommandResponses.Failure(request.BindingId, "registration_provider_duplicate_option_mapping");
        }

        List<RegistrationProviderFieldMapping> fields = [.. request.Fields.Select(field =>
            RegistrationProviderFieldMapping.Create(binding, field.PlatformFieldKey, field.ProviderFieldKey, field.IsRequired))];
        Dictionary<string, RegistrationProviderFieldMapping> byPlatformField = fields.ToDictionary(field => field.PlatformFieldKey, StringComparer.Ordinal);
        List<RegistrationProviderOptionMapping> options = [.. request.Options.Select(option =>
            RegistrationProviderOptionMapping.Create(binding, byPlatformField[Normalize(option.PlatformFieldKey)], option.PlatformOptionKey, option.ProviderOptionKey))];
        binding.ReplaceDraftMappings(fields, options);
        await repository.SaveChangesAsync(cancellationToken);
        return RegistrationProviderCommandResponses.Success(binding.Id, "Registration provider mappings replaced.");
    }

    private static bool HasDuplicates(IEnumerable<string> values) =>
        values.GroupBy(value => value, StringComparer.Ordinal).Any(group => group.Count() > 1);

    private static string Normalize(string value) => value.Trim();
}

public sealed class PublishRegistrationProviderBindingCommandHandler(IRegistrationProviderRepository repository)
    : IRequestHandler<PublishRegistrationProviderBindingCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(PublishRegistrationProviderBindingCommand request, CancellationToken cancellationToken)
    {
        await new PublishRegistrationProviderBindingCommandValidator().ValidateAndThrowAsync(request, cancellationToken);
        RegistrationProviderBinding? binding = await repository.GetBindingAsync(request.TenantId, request.BindingId, cancellationToken);
        if (binding is null) return RegistrationProviderCommandResponses.Failure(request.BindingId, "registration_provider_binding_not_found");
        if (SchemaDriftClassifier.BlocksPublication(request.DriftClass))
        {
            return RegistrationProviderCommandResponses.Failure(request.BindingId, "registration_provider_drift_blocks_publication");
        }

        binding.SetDriftClass(ToDomain(request.DriftClass));
        binding.Publish(ComputeMappingRevisionHash(binding), request.PublishedAt);
        await repository.SaveChangesAsync(cancellationToken);
        return RegistrationProviderCommandResponses.Success(binding.Id, "Registration provider binding published.");
    }

    private static RegistrationEvidenceHash ComputeMappingRevisionHash(RegistrationProviderBinding binding)
    {
        StringBuilder canonical = new();
        canonical.Append("registration-provider-mapping-v2\n");
        canonical.Append("form-version:").Append(binding.RegistrationFormVersionId).Append('\n');
        if (binding.Connection is not null)
        {
            canonical.Append("tuple:").Append(binding.Connection.TupleKey.Length).Append(':').Append(binding.Connection.TupleKey).Append('\n');
        }

        canonical.Append("provider-survey:").Append(binding.ProviderSurveyId?.Length ?? 0).Append(':').Append(binding.ProviderSurveyId).Append('\n');
        canonical.Append("provider-survey-revision:").Append(binding.ProviderSurveyRevisionId?.Length ?? 0).Append(':').Append(binding.ProviderSurveyRevisionId).Append('\n');
        foreach (RegistrationProviderFieldMapping field in binding.FieldMappings.Where(field => !field.IsDeleted).OrderBy(field => field.PlatformFieldKey, StringComparer.Ordinal))
        {
            canonical.Append("field:").Append(field.PlatformFieldKey.Length).Append(':').Append(field.PlatformFieldKey)
                .Append(':').Append(field.ProviderFieldKey.Length).Append(':').Append(field.ProviderFieldKey)
                .Append(':').Append(field.IsRequired ? '1' : '0').Append('\n');
        }

        Dictionary<Guid, string> fieldKeys = binding.FieldMappings.ToDictionary(field => field.Id, field => field.PlatformFieldKey);
        foreach (RegistrationProviderOptionMapping option in binding.OptionMappings.Where(option => !option.IsDeleted)
            .OrderBy(option => fieldKeys[option.RegistrationProviderFieldMappingId], StringComparer.Ordinal)
            .ThenBy(option => option.PlatformOptionKey, StringComparer.Ordinal))
        {
            string fieldKey = fieldKeys[option.RegistrationProviderFieldMappingId];
            canonical.Append("option:").Append(fieldKey.Length).Append(':').Append(fieldKey)
                .Append(':').Append(option.PlatformOptionKey.Length).Append(':').Append(option.PlatformOptionKey)
                .Append(':').Append(option.ProviderOptionKey.Length).Append(':').Append(option.ProviderOptionKey).Append('\n');
        }

        return RegistrationEvidenceHash.Create(Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))));
    }

    private static RegistrationProviderDriftClassEnum ToDomain(RegistrationProviderSchemaDriftClass driftClass) => driftClass switch
    {
        RegistrationProviderSchemaDriftClass.NoDrift => RegistrationProviderDriftClassEnum.NoDrift,
        RegistrationProviderSchemaDriftClass.AdditiveOptionalChange => RegistrationProviderDriftClassEnum.AdditiveOptionalChange,
        RegistrationProviderSchemaDriftClass.LabelOnlyChange => RegistrationProviderDriftClassEnum.LabelOnlyChange,
        RegistrationProviderSchemaDriftClass.MappingRequired => RegistrationProviderDriftClassEnum.MappingRequired,
        RegistrationProviderSchemaDriftClass.RequiredFieldRemoved => RegistrationProviderDriftClassEnum.RequiredFieldRemoved,
        RegistrationProviderSchemaDriftClass.TypeChanged => RegistrationProviderDriftClassEnum.TypeChanged,
        RegistrationProviderSchemaDriftClass.OptionSetChanged => RegistrationProviderDriftClassEnum.OptionSetChanged,
        _ => RegistrationProviderDriftClassEnum.UnsupportedChange
    };
}

public sealed class ImportRegistrationProviderSchemaRevisionCommandValidator : AbstractValidator<ImportRegistrationProviderSchemaRevisionCommand>
{
    public ImportRegistrationProviderSchemaRevisionCommandValidator()
    {
        RuleFor(command => command.TenantId).NotEmpty();
        RuleFor(command => command.ConnectionId).NotEmpty();
        RuleFor(command => command.RevisionHash).NotNull();
        RuleFor(command => command.ObservedAt).Must(value => value.Kind == DateTimeKind.Utc && value != default);
    }
}

public sealed class ReplaceDraftRegistrationProviderMappingsCommandValidator : AbstractValidator<ReplaceDraftRegistrationProviderMappingsCommand>
{
    public ReplaceDraftRegistrationProviderMappingsCommandValidator()
    {
        RuleFor(command => command.TenantId).NotEmpty();
        RuleFor(command => command.BindingId).NotEmpty();
        RuleFor(command => command.Fields).NotEmpty();
        RuleForEach(command => command.Fields).ChildRules(field =>
        {
            field.RuleFor(value => value.PlatformFieldKey).NotEmpty().MaximumLength(200);
            field.RuleFor(value => value.ProviderFieldKey).NotEmpty().MaximumLength(200);
        });
        RuleForEach(command => command.Options).ChildRules(option =>
        {
            option.RuleFor(value => value.PlatformFieldKey).NotEmpty().MaximumLength(200);
            option.RuleFor(value => value.PlatformOptionKey).NotEmpty().MaximumLength(200);
            option.RuleFor(value => value.ProviderOptionKey).NotEmpty().MaximumLength(200);
        });
    }
}

public sealed class PublishRegistrationProviderBindingCommandValidator : AbstractValidator<PublishRegistrationProviderBindingCommand>
{
    public PublishRegistrationProviderBindingCommandValidator()
    {
        RuleFor(command => command.TenantId).NotEmpty();
        RuleFor(command => command.BindingId).NotEmpty();
        RuleFor(command => command.PublishedAt).Must(value => value.Kind == DateTimeKind.Utc && value != default);
    }
}

file static class RegistrationProviderCommandResponses
{
    public static BaseCommandResponse<Guid> Success(Guid id, string message) => BaseCommandResponse.Success(id, message);
    public static BaseCommandResponse<Guid> Failure(Guid id, string code) => BaseCommandResponse.Failure<Guid>(code, code, id: id);
}
