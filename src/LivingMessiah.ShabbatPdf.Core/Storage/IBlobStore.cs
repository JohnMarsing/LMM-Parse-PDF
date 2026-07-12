namespace LivingMessiah.ShabbatPdf.Core.Storage;

/// <summary>
/// Blob download/upload used by the parse pipeline.
/// Azure implementation lands in a later PR.
/// </summary>
public interface IBlobStore
{
    Task DownloadToFileAsync(
        string container,
        string blobName,
        string localPath,
        CancellationToken ct = default);

    Task UploadTextAsync(
        string container,
        string blobName,
        string content,
        bool overwrite,
        CancellationToken ct = default);

    /// <summary>
    /// Upload binary content (e.g. a teaching PDF) with the given content type.
    /// </summary>
    Task UploadBinaryAsync(
        string container,
        string blobName,
        byte[] content,
        string contentType,
        bool overwrite,
        CancellationToken ct = default);

    Task<bool> ExistsAsync(
        string container,
        string blobName,
        CancellationToken ct = default);

    Task EnsureContainerExistsAsync(
        string container,
        CancellationToken ct = default);

    string GetBlobUri(string container, string blobName);
}
