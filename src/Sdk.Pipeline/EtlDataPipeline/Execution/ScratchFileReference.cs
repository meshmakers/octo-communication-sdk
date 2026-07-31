using System.Text.Json.Serialization;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Execution;

/// <summary>
/// The data-context marker a node writes in place of a large base64 blob: it carries
/// only the opaque scratch <see cref="Token"/> plus lightweight metadata. A consuming
/// node resolves the bytes by streaming from <see cref="IPipelineScratchSpace"/> with the
/// token. Kept tiny on purpose — this is what travels through the JSON data context.
/// </summary>
public sealed record ScratchFileReference
{
    /// <summary>Opaque scratch token (see <see cref="IPipelineScratchSpace.CreateFile"/>).</summary>
    [JsonPropertyName("scratchFileToken")]
    public string? Token { get; init; }

    /// <summary>Optional original file name (for naming the downstream artifact).</summary>
    [JsonPropertyName("fileName")]
    public string? FileName { get; init; }

    /// <summary>Optional MIME content type of the referenced bytes.</summary>
    [JsonPropertyName("contentType")]
    public string? ContentType { get; init; }

    /// <summary>Length of the referenced bytes.</summary>
    [JsonPropertyName("length")]
    public long Length { get; init; }
}

/// <summary>
/// Helpers to write and detect a <see cref="ScratchFileReference"/> in the data context.
/// A path holding a scratch reference is a JSON object with a non-empty
/// <c>scratchFileToken</c>; a path holding inline content is a plain base64 string — so
/// <see cref="TryRead"/> lets a node transparently accept either shape.
/// </summary>
public static class ScratchFileRef
{
    /// <summary>Writes a scratch reference marker to <paramref name="path"/>.</summary>
    public static void Write(IDataContext dataContext, string path, string token, long length,
        string? fileName = null, string? contentType = null)
    {
        dataContext.Set(path, new ScratchFileReference
        {
            Token = token,
            Length = length,
            FileName = fileName,
            ContentType = contentType
        });
    }

    /// <summary>
    /// Reads a scratch reference at <paramref name="path"/>. Returns false when the path is
    /// absent or holds anything other than an object with a non-empty <c>scratchFileToken</c>
    /// (e.g. a base64 string), so callers can fall back to inline handling.
    /// </summary>
    public static bool TryRead(IDataContext dataContext, string path, out ScratchFileReference reference)
    {
        reference = null!;
        if (dataContext.GetKind(path) != DataKind.Object)
        {
            return false;
        }

        if (!dataContext.TryGet<ScratchFileReference>(path, out var value) ||
            string.IsNullOrEmpty(value?.Token))
        {
            return false;
        }

        reference = value;
        return true;
    }
}
