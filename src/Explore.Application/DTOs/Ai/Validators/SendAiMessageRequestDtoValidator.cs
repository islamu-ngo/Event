using Explore.Application.DTOs.Ai;
using Explore.Application.Services;
using Explore.Domain.Ai;
using FluentValidation;

namespace Explore.Application.DTOs.Ai.Validators;

public sealed class SendAiMessageRequestDtoValidator : AbstractValidator<SendAiMessageRequestDto>
{
    private const int MaxContentLength = 16_000;
    private const int MaxMediaTypeLength = 128;
    private const int MaxFileNameLength = 255;
    private const int MaxReferenceCount = 20;
    private const int MaxReferenceKindLength = 64;
    private const int MaxReferenceDisplayNameLength = 200;
    private const int MaxReferenceSummaryLength = 500;
    private const int MaxBase64Characters = ((AiMessageImageAttachmentSerializer.MaxImageBytes + 2) / 3) * 4;

    public SendAiMessageRequestDtoValidator()
    {
        RuleFor(request => request.Content)
            .MaximumLength(MaxContentLength);

        RuleFor(request => request)
            .Must(request => !string.IsNullOrWhiteSpace(request.Content) || (request.Images?.Count ?? 0) > 0)
            .WithMessage("AI messages require content or at least one image.");

        RuleFor(request => request.Images)
            .Must(images => (images?.Count ?? 0) <= AiMessageImageAttachmentSerializer.MaxImageCount)
            .WithMessage($"AI messages can include at most {AiMessageImageAttachmentSerializer.MaxImageCount} images.");

        RuleForEach(request => request.Images)
            .ChildRules(image =>
            {
                image.RuleFor(value => value.MediaType)
                    .NotEmpty()
                    .MaximumLength(MaxMediaTypeLength)
                    .Must(SafeRasterContentPolicy.IsBrowserImageMimeType)
                    .WithMessage("AI message images must use JPEG, PNG, GIF, or WebP.");

                image.RuleFor(value => value.Data)
                    .NotEmpty()
                    .Must(BePlainBase64ImageData)
                    .WithMessage("AI message image data must be plain base64 text within the allowed image size.");

                image.RuleFor(value => value.FileName)
                    .MaximumLength(MaxFileNameLength)
                    .When(value => !string.IsNullOrWhiteSpace(value.FileName));

                image.RuleFor(value => value.SizeBytes)
                    .GreaterThan(0)
                    .LessThanOrEqualTo(AiMessageImageAttachmentSerializer.MaxImageBytes)
                    .When(value => value.SizeBytes.HasValue);
            });

        RuleFor(request => request.References)
            .Must(references => (references?.Count ?? 0) <= MaxReferenceCount)
            .WithMessage($"AI messages can include at most {MaxReferenceCount} selected references.");

        RuleForEach(request => request.References)
            .ChildRules(reference =>
            {
                reference.RuleFor(value => value.Kind)
                    .NotEmpty()
                    .MaximumLength(MaxReferenceKindLength)
                    .Must(IsSupportedReferenceKind)
                    .WithMessage("AI selected reference kind must be Event, EventSession, Actor, or Organization.");

                reference.RuleFor(value => value.ReferenceId)
                    .NotEqual(Guid.Empty)
                    .WithMessage("AI selected reference id must be a non-empty identifier.");

                reference.RuleFor(value => value.DisplayName)
                    .NotEmpty()
                    .MaximumLength(MaxReferenceDisplayNameLength);

                reference.RuleFor(value => value.Summary)
                    .MaximumLength(MaxReferenceSummaryLength)
                    .When(value => !string.IsNullOrWhiteSpace(value.Summary));
            });

        RuleFor(request => request.IdempotencyKey)
            .NotEmpty()
            .MaximumLength(128);

        RuleFor(request => request.ModelId)
            .MaximumLength(256)
            .When(request => !string.IsNullOrWhiteSpace(request.ModelId));

        RuleFor(request => request.ActorId)
            .Must(actorId => actorId is null || actorId != Guid.Empty)
            .WithMessage("AI acting actor id must be a non-empty actor identifier.");

        RuleFor(request => request.Mode)
            .MaximumLength(16)
            .Must(AiAssistantInteractionModes.IsValid)
            .WithMessage("AI assistant mode must be 'ask' or 'build'.");
    }

    private static bool IsSupportedReferenceKind(string? kind) =>
        !string.IsNullOrWhiteSpace(kind)
        && Enum.TryParse<AiReferenceKind>(kind.Trim(), ignoreCase: true, out _);

    private static bool BePlainBase64ImageData(string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            return false;
        }

        var value = data.Trim();
        if (value.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            || value.Length > MaxBase64Characters)
        {
            return false;
        }

        return value.Length <= MaxBase64Characters;
    }
}
