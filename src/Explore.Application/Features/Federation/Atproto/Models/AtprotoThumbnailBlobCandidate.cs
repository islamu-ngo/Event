namespace Explore.Application.Features.Federation.Atproto.Models;

public sealed record AtprotoThumbnailBlobCandidate(
    string Did,
    string Cid,
    string MimeType,
    long Size);
