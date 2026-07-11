using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using LivingMessiah.ShabbatPdf.Core.Options;

namespace LivingMessiah.ShabbatPdf.Core.Storage;

/// <summary>
/// Azure Blob Storage implementation of <see cref="IBlobStore"/>.
/// CLI: connection string. Azure-hosted: Managed Identity + service URI.
/// </summary>
public sealed class AzureBlobStore : IBlobStore
{
    private readonly BlobServiceClient _client;

    public AzureBlobStore(BlobServiceClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public AzureBlobStore(BlobOptions options)
        : this(CreateClient(options))
    {
    }

    public static BlobServiceClient CreateClient(BlobOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.UseDefaultAzureCredential)
        {
            if (string.IsNullOrWhiteSpace(options.ServiceUri))
            {
                throw new InvalidOperationException(
                    "Blob:ServiceUri is required when Blob:UseDefaultAzureCredential is true.");
            }

            return new BlobServiceClient(
                new Uri(options.ServiceUri.TrimEnd('/')),
                new DefaultAzureCredential());
        }

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException(
                "Blob:ConnectionString is required (or set UseDefaultAzureCredential + ServiceUri). " +
                "Use User Secrets: dotnet user-secrets set \"Blob:ConnectionString\" \"...\" --project src/LivingMessiah.ShabbatPdf.Cli");
        }

        return new BlobServiceClient(options.ConnectionString);
    }

    public async Task DownloadToFileAsync(
        string container,
        string blobName,
        string localPath,
        CancellationToken ct = default)
    {
        ValidateNames(container, blobName);
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);

        var directory = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var blob = _client.GetBlobContainerClient(container).GetBlobClient(blobName);

        try
        {
            await blob.DownloadToAsync(localPath, ct).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new FileNotFoundException(
                $"Blob not found: {container}/{blobName}",
                ex);
        }
    }

    public async Task UploadTextAsync(
        string container,
        string blobName,
        string content,
        bool overwrite,
        CancellationToken ct = default)
    {
        ValidateNames(container, blobName);
        ArgumentNullException.ThrowIfNull(content);

        var blob = _client.GetBlobContainerClient(container).GetBlobClient(blobName);
        var options = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType = "text/markdown; charset=utf-8"
            },
            Conditions = overwrite
                ? null
                : new BlobRequestConditions { IfNoneMatch = new ETag("*") }
        };

        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        try
        {
            await blob.UploadAsync(stream, options, ct).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new InvalidOperationException(
                $"Container not found: '{container}'. Create it or pass --ensure-container.",
                ex);
        }
        catch (RequestFailedException ex) when (ex.Status == 412 && !overwrite)
        {
            throw new IOException($"Blob already exists and overwrite is disabled: {container}/{blobName}", ex);
        }
    }

    public async Task<bool> ExistsAsync(
        string container,
        string blobName,
        CancellationToken ct = default)
    {
        ValidateNames(container, blobName);
        var blob = _client.GetBlobContainerClient(container).GetBlobClient(blobName);
        return await blob.ExistsAsync(ct).ConfigureAwait(false);
    }

    public async Task EnsureContainerExistsAsync(
        string container,
        CancellationToken ct = default)
    {
        ValidateContainer(container);
        var containerClient = _client.GetBlobContainerClient(container);
        await containerClient.CreateIfNotExistsAsync(
            publicAccessType: PublicAccessType.None,
            cancellationToken: ct).ConfigureAwait(false);
    }

    public string GetBlobUri(string container, string blobName)
    {
        ValidateNames(container, blobName);
        return _client.GetBlobContainerClient(container).GetBlobClient(blobName).Uri.ToString();
    }

    private static void ValidateNames(string container, string blobName)
    {
        ValidateContainer(container);
        if (string.IsNullOrWhiteSpace(blobName)
            || blobName.Contains("..", StringComparison.Ordinal)
            || blobName.Contains('/')
            || blobName.Contains('\\'))
        {
            throw new ArgumentException($"Invalid blob name: '{blobName}'", nameof(blobName));
        }
    }

    private static void ValidateContainer(string container)
    {
        if (string.IsNullOrWhiteSpace(container)
            || container.Contains("..", StringComparison.Ordinal)
            || container.Contains('/')
            || container.Contains('\\'))
        {
            throw new ArgumentException($"Invalid container name: '{container}'", nameof(container));
        }
    }
}
