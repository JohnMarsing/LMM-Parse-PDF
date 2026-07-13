using System.Text.Json;
using Azure.Messaging.EventGrid;
using Azure.Messaging.EventGrid.SystemEvents;
using LivingMessiah.ShabbatPdf.Core.Models;
using LivingMessiah.ShabbatPdf.Core.Options;
using LivingMessiah.ShabbatPdf.Core.Pipeline;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LivingMessiah.ShabbatPdf.Functions;

/// <summary>
/// Thin host: Event Grid (blob created) → <see cref="IParsePipeline"/>.
/// Flex Consumption does not support classic polled blob triggers; Event Grid is required.
/// </summary>
public sealed class ProcessShabbatPdfFunction
{
    private readonly IParsePipeline _pipeline;
    private readonly ParseOptions _parseOptions;
    private readonly BlobOptions _blobOptions;
    private readonly ILogger<ProcessShabbatPdfFunction> _logger;

    public ProcessShabbatPdfFunction(
        IParsePipeline pipeline,
        IOptions<ParseOptions> parseOptions,
        IOptions<BlobOptions> blobOptions,
        ILogger<ProcessShabbatPdfFunction> logger)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _parseOptions = parseOptions?.Value ?? throw new ArgumentNullException(nameof(parseOptions));
        _blobOptions = blobOptions?.Value ?? throw new ArgumentNullException(nameof(blobOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Fires on Microsoft.Storage.BlobCreated for the source container (Event Grid subscription).
    /// </summary>
    [Function(nameof(ProcessShabbatPdf))]
    public async Task ProcessShabbatPdf(
        [EventGridTrigger] EventGridEvent eventGridEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(eventGridEvent);

        _logger.LogInformation(
            "EventGrid event Type={Type} Subject={Subject} Id={Id}",
            eventGridEvent.EventType,
            eventGridEvent.Subject,
            eventGridEvent.Id);

        if (!TryGetBlobName(eventGridEvent, out var sourceName, out var container))
        {
            _logger.LogWarning(
                "Could not parse blob name from event. Subject={Subject} Data={Data}",
                eventGridEvent.Subject,
                eventGridEvent.Data?.ToString());
            return;
        }

        if (!string.IsNullOrEmpty(container)
            && !string.Equals(container, _blobOptions.SourceContainer, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Skipping blob in container {Container} (expected {Expected}): {Name}",
                container,
                _blobOptions.SourceContainer,
                sourceName);
            return;
        }

        if (!ShabbatBlobTriggerFilter.ShouldProcess(sourceName))
        {
            _logger.LogInformation("Skipping blob (filter): {Name}", sourceName);
            return;
        }

        _logger.LogInformation("Processing agenda blob {Name}", sourceName);

        // BlobMode downloads source once, writes teaching PDF + Markdown.
        var request = new ParseRequest(
            SourceName: sourceName,
            BlobMode: true,
            Overwrite: _parseOptions.Overwrite,
            SkipIfDestinationExists: false,
            DryRun: false,
            RequireStandardBlobName: _parseOptions.RequireStandardBlobName,
            EnsureDestinationContainer: false,
            TeachingOnly: false);

        var result = await _pipeline.RunAsync(request, cancellationToken).ConfigureAwait(false);

        if (result.Success)
        {
            _logger.LogInformation(
                "OK {Name} teaching={Teaching} md={Md} pages={Start}-{End}",
                sourceName,
                result.TeachingPdfUri ?? "(none)",
                result.DestinationUri ?? "(none)",
                result.Anchors?.ContentStartPage,
                result.Anchors?.ContentEndPage);
            return;
        }

        if (IsNonRetriable(result.Message))
        {
            _logger.LogError(
                "Parse failed (non-retriable) for {Name}: {Message}",
                sourceName,
                result.Message);
            return;
        }

        _logger.LogError(
            "Parse failed (retriable) for {Name}: {Message}",
            sourceName,
            result.Message);
        throw new InvalidOperationException(result.Message);
    }

    private static bool TryGetBlobName(
        EventGridEvent eventGridEvent,
        out string blobName,
        out string? container)
    {
        blobName = string.Empty;
        container = null;

        // Preferred: typed system event
        if (eventGridEvent.TryGetSystemEventData(out object? systemEvent)
            && systemEvent is StorageBlobCreatedEventData created
            && !string.IsNullOrWhiteSpace(created.Url))
        {
            return TryParseBlobUrl(created.Url, out blobName, out container);
        }

        // Fallback: raw JSON data.url
        try
        {
            if (eventGridEvent.Data is not null)
            {
                using var doc = JsonDocument.Parse(eventGridEvent.Data.ToString());
                if (doc.RootElement.TryGetProperty("url", out var urlProp)
                    && urlProp.ValueKind == JsonValueKind.String)
                {
                    var url = urlProp.GetString();
                    if (!string.IsNullOrWhiteSpace(url)
                        && TryParseBlobUrl(url, out blobName, out container))
                    {
                        return true;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // fall through to subject parse
        }

        // Subject: /blobServices/default/containers/{container}/blobs/{name}
        var subject = eventGridEvent.Subject ?? string.Empty;
        const string marker = "/blobs/";
        var idx = subject.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            blobName = Uri.UnescapeDataString(subject[(idx + marker.Length)..]);
            const string containers = "/containers/";
            var cIdx = subject.IndexOf(containers, StringComparison.OrdinalIgnoreCase);
            if (cIdx >= 0)
            {
                var start = cIdx + containers.Length;
                var end = subject.IndexOf('/', start);
                if (end > start)
                {
                    container = subject[start..end];
                }
            }

            return !string.IsNullOrWhiteSpace(blobName);
        }

        return false;
    }

    private static bool TryParseBlobUrl(string url, out string blobName, out string? container)
    {
        blobName = string.Empty;
        container = null;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        // /shabbat-service/2026-07-18-Lev-18.pdf
        var parts = uri.AbsolutePath.Trim('/').Split('/', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        container = Uri.UnescapeDataString(parts[0]);
        blobName = Uri.UnescapeDataString(parts[1]);
        return !string.IsNullOrWhiteSpace(blobName);
    }

    private static bool IsNonRetriable(string message) =>
        message.StartsWith(ParseErrorCodes.InvalidName, StringComparison.Ordinal)
        || message.StartsWith("AnchorNotFound", StringComparison.Ordinal)
        || message.StartsWith("EmptySlice", StringComparison.Ordinal)
        || message.StartsWith("StartAnchorNotFound", StringComparison.Ordinal)
        || message.StartsWith("EndAnchorNotFound", StringComparison.Ordinal);
}
