using LivingMessiah.ShabbatPdf.Core.Storage;

namespace LivingMessiah.ShabbatPdf.Tests.Storage;

/// <summary>
/// Test double for <see cref="IBlobStore"/> — no real Azure calls.
/// </summary>
public sealed class InMemoryBlobStore : IBlobStore
{
    private readonly Dictionary<string, byte[]> _blobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _containers = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, byte[]> Blobs => _blobs;

    public void Seed(string container, string blobName, byte[] content)
    {
        _containers.Add(container);
        _blobs[Key(container, blobName)] = content;
    }

    public Task DownloadToFileAsync(
        string container,
        string blobName,
        string localPath,
        CancellationToken ct = default)
    {
        if (!_blobs.TryGetValue(Key(container, blobName), out var bytes))
        {
            throw new FileNotFoundException($"Blob not found: {container}/{blobName}");
        }

        var dir = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllBytes(localPath, bytes);
        return Task.CompletedTask;
    }

    public Task UploadTextAsync(
        string container,
        string blobName,
        string content,
        bool overwrite,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        return UploadBinaryAsync(
            container,
            blobName,
            System.Text.Encoding.UTF8.GetBytes(content),
            contentType: "text/markdown; charset=utf-8",
            overwrite,
            ct);
    }

    public Task UploadBinaryAsync(
        string container,
        string blobName,
        byte[] content,
        string contentType,
        bool overwrite,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        if (!_containers.Contains(container))
        {
            throw new InvalidOperationException(
                $"Container not found: '{container}'. Create it or pass --ensure-container.");
        }

        var key = Key(container, blobName);
        if (!overwrite && _blobs.ContainsKey(key))
        {
            throw new IOException($"Blob already exists: {container}/{blobName}");
        }

        _blobs[key] = content;
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(
        string container,
        string blobName,
        CancellationToken ct = default) =>
        Task.FromResult(_blobs.ContainsKey(Key(container, blobName)));

    public Task EnsureContainerExistsAsync(
        string container,
        CancellationToken ct = default)
    {
        _containers.Add(container);
        return Task.CompletedTask;
    }

    public string GetBlobUri(string container, string blobName) =>
        $"https://test.blob.core.windows.net/{container}/{blobName}";

    private static string Key(string container, string blobName) =>
        $"{container}/{blobName}";
}
