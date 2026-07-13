using LivingMessiah.ShabbatPdf.Core.Extraction;
using LivingMessiah.ShabbatPdf.Core.Models;
using LivingMessiah.ShabbatPdf.Core.Options;
using LivingMessiah.ShabbatPdf.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LivingMessiah.ShabbatPdf.Core.Pipeline;

/// <summary>
/// Orchestrates extract → anchors → teaching PDF slice → markdown → local/blob write.
/// </summary>
public sealed class ParsePipeline : IParsePipeline
{
    private readonly IPdfPageSource _pageSource;
    private readonly AnchorLocator _anchorLocator;
    private readonly MarkdownBuilder _markdownBuilder;
    private readonly ParseOptions _options;
    private readonly BlobOptions _blobOptions;
    private readonly IBlobStore? _blobStore;
    private readonly ILogger<ParsePipeline> _logger;

    public ParsePipeline(
        IPdfPageSource pageSource,
        AnchorLocator anchorLocator,
        MarkdownBuilder markdownBuilder,
        IOptions<ParseOptions>? options = null,
        IOptions<BlobOptions>? blobOptions = null,
        IBlobStore? blobStore = null,
        ILogger<ParsePipeline>? logger = null)
    {
        _pageSource = pageSource ?? throw new ArgumentNullException(nameof(pageSource));
        _anchorLocator = anchorLocator ?? throw new ArgumentNullException(nameof(anchorLocator));
        _markdownBuilder = markdownBuilder ?? throw new ArgumentNullException(nameof(markdownBuilder));
        _options = options?.Value ?? new ParseOptions();
        _blobOptions = blobOptions?.Value ?? new BlobOptions();
        _blobStore = blobStore;
        _logger = logger ?? NullLogger<ParsePipeline>.Instance;
    }

    public async Task<ParseResult> RunAsync(ParseRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? tempPdfPath = null;

        try
        {
            var hasLocal = !string.IsNullOrWhiteSpace(request.LocalInputPath);
            var hasStream = request.PdfStream is not null;
            var blobMode = request.BlobMode;

            if (!hasLocal && !hasStream && !blobMode)
            {
                return Fail(
                    ParseErrorCodes.SourceNotFound,
                    "No PDF source: set LocalInputPath, PdfStream, or BlobMode with SourceName.");
            }

            if (blobMode && _blobStore is null)
            {
                return Fail(
                    ParseErrorCodes.Unexpected,
                    "BlobMode requires IBlobStore (configure Blob:ConnectionString).");
            }

            var sourceName = string.IsNullOrWhiteSpace(request.SourceName)
                ? Path.GetFileName(request.LocalInputPath ?? "document.pdf")
                : Path.GetFileName(request.SourceName);

            // Validate blob names (no path traversal)
            if (blobMode && (sourceName.Contains("..") || sourceName.Contains('/') || sourceName.Contains('\\')))
            {
                return Fail(ParseErrorCodes.InvalidName, $"Invalid blob name: '{sourceName}'.");
            }

            var nameMeta = FilenameParser.Parse(sourceName);
            if (!nameMeta.IsStandardPattern)
            {
                _logger.LogWarning(
                    "Non-standard PDF name '{SourceName}'; citation will be 'unknown'.",
                    sourceName);
            }

            if (request.RequireStandardBlobName && !nameMeta.IsStandardPattern)
            {
                return Fail(
                    ParseErrorCodes.InvalidName,
                    $"Blob name must match YYYY-MM-DD-Citation.pdf: '{sourceName}'.");
            }

            var mdBlobName = nameMeta.MarkdownFileName;
            var teachingBlobName = nameMeta.TeachingPdfFileName;
            var localOutputPath = ResolveLocalOutputPath(request, nameMeta);
            var localTeachingPath = ResolveLocalTeachingPdfPath(request, nameMeta, localOutputPath);
            var destUriPreview = blobMode && _blobStore is not null
                ? (request.TeachingOnly
                    ? _blobStore.GetBlobUri(_blobOptions.SourceContainer, teachingBlobName)
                    : _blobStore.GetBlobUri(_blobOptions.DestinationContainer, mdBlobName))
                : (request.TeachingOnly ? localTeachingPath : localOutputPath);

            // Skip-if-exists early exit:
            // - TeachingOnly: skip when teaching PDF already exists
            // - Full run: skip when Markdown destination already exists
            //   (teaching skip for full runs is still handled later per-artifact)
            if (request.SkipIfDestinationExists)
            {
                if (request.TeachingOnly)
                {
                    if (blobMode && _blobStore is not null)
                    {
                        if (await _blobStore.ExistsAsync(
                                _blobOptions.SourceContainer, teachingBlobName, ct).ConfigureAwait(false))
                        {
                            var uri = _blobStore.GetBlobUri(_blobOptions.SourceContainer, teachingBlobName);
                            _logger.LogInformation("Skip existing teaching blob: {Uri}", uri);
                            return new ParseResult(
                                true,
                                "Skipped: teaching PDF already exists.",
                                TeachingPdfUri: uri);
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(localTeachingPath) && File.Exists(localTeachingPath))
                    {
                        _logger.LogInformation("Skip existing teaching PDF: {Path}", localTeachingPath);
                        return new ParseResult(
                            true,
                            "Skipped: teaching PDF already exists.",
                            TeachingPdfUri: localTeachingPath);
                    }
                }
                else if (blobMode && _blobStore is not null)
                {
                    if (await _blobStore.ExistsAsync(
                            _blobOptions.DestinationContainer, mdBlobName, ct).ConfigureAwait(false))
                    {
                        var uri = _blobStore.GetBlobUri(_blobOptions.DestinationContainer, mdBlobName);
                        _logger.LogInformation("Skip existing blob: {Uri}", uri);
                        return new ParseResult(true, "Skipped: destination already exists.", DestinationUri: uri);
                    }
                }
                else if (!string.IsNullOrWhiteSpace(localOutputPath) && File.Exists(localOutputPath))
                {
                    _logger.LogInformation("Skip existing output: {OutputPath}", localOutputPath);
                    return new ParseResult(true, "Skipped: destination already exists.", DestinationUri: localOutputPath);
                }
            }

            if (blobMode
                && request.EnsureDestinationContainer
                && !request.TeachingOnly
                && _blobStore is not null)
            {
                _logger.LogInformation(
                    "Ensuring destination container exists: {Container}",
                    _blobOptions.DestinationContainer);
                await _blobStore.EnsureContainerExistsAsync(
                    _blobOptions.DestinationContainer, ct).ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();

            // Resolve PDF path (local, temp download, or stream)
            string? extractPath = null;
            Stream? extractStream = null;

            if (hasLocal)
            {
                if (!File.Exists(request.LocalInputPath!))
                {
                    return Fail(
                        ParseErrorCodes.SourceNotFound,
                        $"PDF file not found: {request.LocalInputPath}");
                }

                extractPath = request.LocalInputPath;
            }
            else if (blobMode)
            {
                tempPdfPath = CreateTempPdfPath(sourceName);
                _logger.LogInformation(
                    "Downloading {Container}/{Blob} to temp file",
                    _blobOptions.SourceContainer,
                    sourceName);

                try
                {
                    await _blobStore!.DownloadToFileAsync(
                        _blobOptions.SourceContainer,
                        sourceName,
                        tempPdfPath,
                        ct).ConfigureAwait(false);
                }
                catch (FileNotFoundException ex)
                {
                    return Fail(ParseErrorCodes.SourceNotFound, ex.Message);
                }

                extractPath = tempPdfPath;
            }
            else
            {
                extractStream = request.PdfStream;
            }

            IReadOnlyList<PdfPageText> pages;
            if (extractPath is not null)
            {
                _logger.LogInformation("Extracting pages from {Path}", extractPath);
                pages = _pageSource.ExtractPages(extractPath);
            }
            else
            {
                _logger.LogInformation("Extracting pages from stream ({SourceName})", sourceName);
                pages = _pageSource.ExtractPages(extractStream!);
            }

            var anchors = _anchorLocator.Locate(pages);
            _logger.LogInformation(
                "Anchors start={Start} end={End} content={ContentStart}-{ContentEnd} introSkip=[{Intro}] endMethod={Method}",
                anchors.StartAnchorPage,
                anchors.EndAnchorPage,
                anchors.ContentStartPage,
                anchors.ContentEndPage,
                string.Join(',', anchors.IntroSkippedPages),
                anchors.EndMatchMethod);

            if (request.DryRun)
            {
                string? dryMarkdown = null;
                if (!request.TeachingOnly)
                {
                    var drySlice = ContentSlicer.Slice(pages, anchors);
                    dryMarkdown = _markdownBuilder.Build(drySlice, sourceName);
                }

                _logger.LogInformation(
                    "Dry-run OK pages={Start}-{End} teachingOnly={TeachingOnly} chars={Chars}",
                    anchors.ContentStartPage,
                    anchors.ContentEndPage,
                    request.TeachingOnly,
                    dryMarkdown?.Length ?? 0);

                return new ParseResult(
                    Success: true,
                    Message: "Dry-run succeeded.",
                    Markdown: dryMarkdown,
                    Anchors: anchors,
                    DestinationUri: destUriPreview);
            }

            // --- Teaching PDF (page-range slice) ---
            var teachingExport = await TryExportTeachingPdfAsync(
                    request,
                    extractPath,
                    extractStream,
                    anchors,
                    localTeachingPath,
                    teachingBlobName,
                    ct)
                .ConfigureAwait(false);

            if (!teachingExport.Success)
            {
                return Fail(teachingExport.ErrorCode!, teachingExport.ErrorMessage!);
            }

            var teachingPdfUri = teachingExport.Uri;

            if (request.TeachingOnly)
            {
                _logger.LogInformation(
                    "OK teaching-only {Source} pages={Start}-{End} anchors={AStart}/{AEnd} introSkip={Intro} end={Method} teaching={Teaching}",
                    sourceName,
                    anchors.ContentStartPage,
                    anchors.ContentEndPage,
                    anchors.StartAnchorPage,
                    anchors.EndAnchorPage,
                    string.Join(',', anchors.IntroSkippedPages),
                    anchors.EndMatchMethod,
                    teachingPdfUri ?? "(none)");

                return new ParseResult(
                    Success: true,
                    Message: "OK (teaching-only)",
                    Anchors: anchors,
                    TeachingPdfUri: teachingPdfUri);
            }

            var slice = ContentSlicer.Slice(pages, anchors);
            var markdown = _markdownBuilder.Build(slice, sourceName);

            string? destinationUri = null;

            // Local Markdown write
            if (!string.IsNullOrWhiteSpace(localOutputPath) && !blobMode)
            {
                if (!request.Overwrite && File.Exists(localOutputPath))
                {
                    return Fail(
                        ParseErrorCodes.UploadFailed,
                        $"Output exists and overwrite is disabled: {localOutputPath}");
                }

                var directory = Path.GetDirectoryName(localOutputPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllTextAsync(localOutputPath, markdown, ct).ConfigureAwait(false);
                destinationUri = localOutputPath;
            }

            // Blob Markdown upload
            if (blobMode && _blobStore is not null)
            {
                try
                {
                    await _blobStore.UploadTextAsync(
                        _blobOptions.DestinationContainer,
                        mdBlobName,
                        markdown,
                        request.Overwrite,
                        ct).ConfigureAwait(false);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("Container not found", StringComparison.OrdinalIgnoreCase))
                {
                    return Fail(
                        ParseErrorCodes.ContainerNotFound,
                        ex.Message);
                }

                destinationUri = _blobStore.GetBlobUri(_blobOptions.DestinationContainer, mdBlobName);
            }

            if (destinationUri is null && string.IsNullOrWhiteSpace(localOutputPath))
            {
                return new ParseResult(
                    Success: true,
                    Message: "Parsed successfully (no output path).",
                    Markdown: markdown,
                    Anchors: anchors,
                    TeachingPdfUri: teachingPdfUri);
            }

            _logger.LogInformation(
                "OK {Source} pages={Start}-{End} anchors={AStart}/{AEnd} introSkip={Intro} end={Method} chars={Chars} teaching={Teaching} -> {Output}",
                sourceName,
                anchors.ContentStartPage,
                anchors.ContentEndPage,
                anchors.StartAnchorPage,
                anchors.EndAnchorPage,
                string.Join(',', anchors.IntroSkippedPages),
                anchors.EndMatchMethod,
                markdown.Length,
                teachingPdfUri ?? "(none)",
                destinationUri);

            return new ParseResult(
                Success: true,
                Message: "OK",
                Markdown: markdown,
                Anchors: anchors,
                DestinationUri: destinationUri,
                TeachingPdfUri: teachingPdfUri);
        }
        catch (AnchorException ex)
        {
            _logger.LogError(ex, "Anchor failure: {Code}", ex.Code);
            return Fail(ex.Code, ex.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "I/O failure");
            return Fail(ParseErrorCodes.IoError, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected parse failure");
            return Fail(ParseErrorCodes.Unexpected, ex.Message);
        }
        finally
        {
            if (tempPdfPath is not null)
            {
                try
                {
                    if (File.Exists(tempPdfPath))
                    {
                        File.Delete(tempPdfPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete temp PDF {Path}", tempPdfPath);
                }
            }
        }
    }

    private static string CreateTempPdfPath(string sourceName)
    {
        var safe = string.Concat(
            Path.GetFileName(sourceName)
                .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        if (string.IsNullOrWhiteSpace(safe))
        {
            safe = "source.pdf";
        }

        var dir = Path.Combine(Path.GetTempPath(), "lmm-parse-pdf");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{Guid.NewGuid():N}-{safe}");
    }

    private static string? ResolveLocalOutputPath(ParseRequest request, FilenameParseResult nameMeta)
    {
        if (!string.IsNullOrWhiteSpace(request.LocalOutputPath))
        {
            return Path.GetFullPath(request.LocalOutputPath);
        }

        if (!request.BlobMode && !string.IsNullOrWhiteSpace(request.LocalInputPath))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(request.LocalInputPath)) ?? ".";
            return Path.Combine(dir, nameMeta.MarkdownFileName);
        }

        return null;
    }

    /// <summary>
    /// Teaching PDF sits next to the Markdown file (local mode).
    /// </summary>
    private static string? ResolveLocalTeachingPdfPath(
        ParseRequest request,
        FilenameParseResult nameMeta,
        string? localOutputPath)
    {
        if (request.BlobMode)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(localOutputPath))
        {
            var dir = Path.GetDirectoryName(localOutputPath) ?? ".";
            return Path.Combine(dir, nameMeta.TeachingPdfFileName);
        }

        if (!string.IsNullOrWhiteSpace(request.LocalInputPath))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(request.LocalInputPath)) ?? ".";
            return Path.Combine(dir, nameMeta.TeachingPdfFileName);
        }

        return null;
    }

    private async Task<(bool Success, string? Uri, string? ErrorCode, string? ErrorMessage)> TryExportTeachingPdfAsync(
        ParseRequest request,
        string? extractPath,
        Stream? extractStream,
        AnchorResult anchors,
        string? localTeachingPath,
        string teachingBlobName,
        CancellationToken ct)
    {
        var blobMode = request.BlobMode;
        var start = anchors.ContentStartPage;
        var end = anchors.ContentEndPage;

        // Skip existing teaching artifact only (MD may still be written).
        if (request.SkipIfDestinationExists)
        {
            if (blobMode && _blobStore is not null)
            {
                if (await _blobStore.ExistsAsync(
                        _blobOptions.SourceContainer, teachingBlobName, ct).ConfigureAwait(false))
                {
                    var uri = _blobStore.GetBlobUri(_blobOptions.SourceContainer, teachingBlobName);
                    _logger.LogInformation("Skip existing teaching blob: {Uri}", uri);
                    return (true, uri, null, null);
                }
            }
            else if (!string.IsNullOrWhiteSpace(localTeachingPath) && File.Exists(localTeachingPath))
            {
                _logger.LogInformation("Skip existing teaching PDF: {Path}", localTeachingPath);
                return (true, localTeachingPath, null, null);
            }
        }

        byte[] teachingBytes;
        try
        {
            if (extractPath is not null)
            {
                teachingBytes = TeachingPdfWriter.WritePageRangeToBytes(extractPath, start, end);
            }
            else if (extractStream is not null)
            {
                teachingBytes = TeachingPdfWriter.WritePageRangeToBytes(extractStream, start, end);
            }
            else
            {
                return (
                    false,
                    null,
                    ParseErrorCodes.Unexpected,
                    "Cannot export teaching PDF: no PDF path or stream available.");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException)
        {
            _logger.LogError(ex, "Teaching PDF slice failed");
            return (false, null, ParseErrorCodes.IoError, ex.Message);
        }

        string? teachingUri = null;

        // Local write (next to .md)
        if (!string.IsNullOrWhiteSpace(localTeachingPath) && !blobMode)
        {
            if (!request.Overwrite && File.Exists(localTeachingPath))
            {
                return (
                    false,
                    null,
                    ParseErrorCodes.UploadFailed,
                    $"Teaching PDF exists and overwrite is disabled: {localTeachingPath}");
            }

            var directory = Path.GetDirectoryName(localTeachingPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllBytesAsync(localTeachingPath, teachingBytes, ct).ConfigureAwait(false);
            teachingUri = localTeachingPath;
            _logger.LogInformation(
                "Wrote teaching PDF pages={Start}-{End} -> {Path}",
                start,
                end,
                localTeachingPath);
        }

        // Azure: upload to source container (same as full agenda)
        if (blobMode && _blobStore is not null)
        {
            try
            {
                await _blobStore.UploadBinaryAsync(
                        _blobOptions.SourceContainer,
                        teachingBlobName,
                        teachingBytes,
                        contentType: "application/pdf",
                        request.Overwrite,
                        ct)
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Container not found", StringComparison.OrdinalIgnoreCase))
            {
                return (false, null, ParseErrorCodes.ContainerNotFound, ex.Message);
            }
            catch (IOException ex)
            {
                return (false, null, ParseErrorCodes.UploadFailed, ex.Message);
            }

            teachingUri = _blobStore.GetBlobUri(_blobOptions.SourceContainer, teachingBlobName);
            _logger.LogInformation(
                "Uploaded teaching PDF pages={Start}-{End} -> {Uri}",
                start,
                end,
                teachingUri);
        }

        // Blob mode may still want a local copy when LocalOutputPath is set — not used today.
        // Stream-only runs with no local/blob destination: return bytes path as null but success
        // so MD can still proceed (no place to put teaching file).
        if (teachingUri is null)
        {
            _logger.LogInformation(
                "Teaching PDF built in memory ({Bytes} bytes, pages {Start}-{End}) but no local/blob destination.",
                teachingBytes.Length,
                start,
                end);
        }

        return (true, teachingUri, null, null);
    }

    private static ParseResult Fail(string code, string message) =>
        new(Success: false, Message: $"{code}: {message}");
}
