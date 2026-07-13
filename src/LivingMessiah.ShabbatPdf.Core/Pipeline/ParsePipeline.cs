using LivingMessiah.ShabbatPdf.Core.Extraction;
using LivingMessiah.ShabbatPdf.Core.Models;
using LivingMessiah.ShabbatPdf.Core.Options;
using LivingMessiah.ShabbatPdf.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LivingMessiah.ShabbatPdf.Core.Pipeline;

/// <summary>
/// Orchestrates extract → anchors → teaching PDF slice → Markdown from teaching PDF → local/blob write.
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

        if (request.TeachingOnly && request.FromTeaching)
        {
            return Fail(
                ParseErrorCodes.InvalidName,
                "TeachingOnly and FromTeaching cannot both be true.");
        }

        string? tempPdfPath = null;
        string? tempTeachingPath = null;

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

            // Front-matter / MD names always use the agenda base (teaching suffix stripped by parser).
            var agendaSourceName = nameMeta.SourceFileName;
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
            // - Full run / FromTeaching: skip when Markdown destination already exists
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

            // --- FromTeaching: step 2 only (input is already *-teaching.pdf) ---
            if (request.FromTeaching)
            {
                return await RunFromTeachingAsync(
                        request,
                        sourceName,
                        agendaSourceName,
                        mdBlobName,
                        localOutputPath,
                        destUriPreview,
                        ct)
                    .ConfigureAwait(false);
            }

            // Resolve full-agenda PDF path (local, temp download, or stream)
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

            // Step 1 anchors always run on the full agenda.
            var anchors = _anchorLocator.Locate(pages);
            _logger.LogInformation(
                "Anchors start={Start} end={End} content={ContentStart}-{ContentEnd} introSkip=[{Intro}] endMethod={Method}",
                anchors.StartAnchorPage,
                anchors.EndAnchorPage,
                anchors.ContentStartPage,
                anchors.ContentEndPage,
                string.Join(',', anchors.IntroSkippedPages),
                anchors.EndMatchMethod);

            // Build teaching PDF bytes (needed for MD step 2 and for export).
            byte[] teachingBytes;
            try
            {
                teachingBytes = BuildTeachingBytes(
                    extractPath,
                    extractStream,
                    anchors.ContentStartPage,
                    anchors.ContentEndPage);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException)
            {
                _logger.LogError(ex, "Teaching PDF slice failed");
                return Fail(ParseErrorCodes.IoError, ex.Message);
            }

            if (request.DryRun)
            {
                string? dryMarkdown = null;
                if (!request.TeachingOnly)
                {
                    // Preview MD from teaching bytes (same as production path).
                    dryMarkdown = BuildMarkdownFromTeachingBytes(teachingBytes, agendaSourceName);
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

            // --- Teaching PDF export (step 1 write) ---
            var teachingExport = await TryExportTeachingPdfAsync(
                    request,
                    teachingBytes,
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
            // Prefer export bytes; when skip reused an existing file, load it for step 2.
            var teachingForMarkdown = teachingExport.Bytes ?? teachingBytes;

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

            // If skip reused existing teaching without loading bytes, resolve from path/blob.
            if (teachingExport.SkippedExisting)
            {
                var loaded = await LoadTeachingBytesForMarkdownAsync(
                        request,
                        localTeachingPath,
                        teachingBlobName,
                        teachingForMarkdown,
                        ct)
                    .ConfigureAwait(false);

                if (!loaded.Success)
                {
                    return Fail(loaded.ErrorCode!, loaded.ErrorMessage!);
                }

                teachingForMarkdown = loaded.Bytes!;
                if (loaded.TempPath is not null)
                {
                    tempTeachingPath = loaded.TempPath;
                }
            }

            // --- Step 2: Markdown from teaching PDF (all pages; teaching-relative 1…N) ---
            string markdown;
            try
            {
                markdown = BuildMarkdownFromTeachingBytes(teachingForMarkdown, agendaSourceName);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException)
            {
                _logger.LogError(ex, "Failed to extract Markdown from teaching PDF");
                return Fail(ParseErrorCodes.IoError, ex.Message);
            }

            var writeMd = await WriteMarkdownAsync(
                    request,
                    markdown,
                    localOutputPath,
                    mdBlobName,
                    ct)
                .ConfigureAwait(false);

            if (!writeMd.Success)
            {
                return Fail(writeMd.ErrorCode!, writeMd.ErrorMessage!);
            }

            var destinationUri = writeMd.Uri;

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
            DeleteTempQuietly(tempPdfPath);
            DeleteTempQuietly(tempTeachingPath);
        }
    }

    private async Task<ParseResult> RunFromTeachingAsync(
        ParseRequest request,
        string sourceName,
        string agendaSourceName,
        string mdBlobName,
        string? localOutputPath,
        string? destUriPreview,
        CancellationToken ct)
    {
        string? tempTeachingPath = null;
        try
        {
            byte[] teachingBytes;

            if (!string.IsNullOrWhiteSpace(request.LocalInputPath))
            {
                if (!File.Exists(request.LocalInputPath))
                {
                    return Fail(
                        ParseErrorCodes.SourceNotFound,
                        $"Teaching PDF not found: {request.LocalInputPath}");
                }

                teachingBytes = await File.ReadAllBytesAsync(request.LocalInputPath, ct).ConfigureAwait(false);
            }
            else if (request.BlobMode)
            {
                // Blob name may be agenda or teaching; prefer the teaching blob name from metadata.
                var teachingBlobName = FilenameParser.Parse(sourceName).TeachingPdfFileName;
                // If the operator passed the teaching name as SourceName, use it as-is.
                var blobToDownload = FilenameParser.IsTeachingPdfName(sourceName)
                    ? Path.GetFileName(sourceName)
                    : teachingBlobName;

                tempTeachingPath = CreateTempPdfPath(blobToDownload);
                _logger.LogInformation(
                    "Downloading teaching {Container}/{Blob} for Markdown",
                    _blobOptions.SourceContainer,
                    blobToDownload);

                try
                {
                    await _blobStore!.DownloadToFileAsync(
                        _blobOptions.SourceContainer,
                        blobToDownload,
                        tempTeachingPath,
                        ct).ConfigureAwait(false);
                }
                catch (FileNotFoundException ex)
                {
                    return Fail(ParseErrorCodes.SourceNotFound, ex.Message);
                }

                teachingBytes = await File.ReadAllBytesAsync(tempTeachingPath, ct).ConfigureAwait(false);
            }
            else if (request.PdfStream is not null)
            {
                using var ms = new MemoryStream();
                if (request.PdfStream.CanSeek)
                {
                    request.PdfStream.Position = 0;
                }

                await request.PdfStream.CopyToAsync(ms, ct).ConfigureAwait(false);
                teachingBytes = ms.ToArray();
            }
            else
            {
                return Fail(
                    ParseErrorCodes.SourceNotFound,
                    "FromTeaching requires LocalInputPath, PdfStream, or BlobMode.");
            }

            if (request.DryRun)
            {
                var dryMarkdown = BuildMarkdownFromTeachingBytes(teachingBytes, agendaSourceName);
                return new ParseResult(
                    Success: true,
                    Message: "Dry-run succeeded.",
                    Markdown: dryMarkdown,
                    DestinationUri: destUriPreview);
            }

            var markdown = BuildMarkdownFromTeachingBytes(teachingBytes, agendaSourceName);
            var writeMd = await WriteMarkdownAsync(
                    request,
                    markdown,
                    localOutputPath,
                    mdBlobName,
                    ct)
                .ConfigureAwait(false);

            if (!writeMd.Success)
            {
                return Fail(writeMd.ErrorCode!, writeMd.ErrorMessage!);
            }

            _logger.LogInformation(
                "OK from-teaching {Source} chars={Chars} -> {Output}",
                sourceName,
                markdown.Length,
                writeMd.Uri ?? "(none)");

            return new ParseResult(
                Success: true,
                Message: "OK",
                Markdown: markdown,
                DestinationUri: writeMd.Uri);
        }
        finally
        {
            DeleteTempQuietly(tempTeachingPath);
        }
    }

    private string BuildMarkdownFromTeachingBytes(byte[] teachingBytes, string agendaSourceName)
    {
        using var stream = new MemoryStream(teachingBytes, writable: false);
        var teachingPages = _pageSource.ExtractPages(stream);
        _logger.LogInformation(
            "Markdown from teaching PDF pages=1-{Count} (teaching-relative)",
            teachingPages.Count);
        return _markdownBuilder.Build(teachingPages, agendaSourceName);
    }

    private static byte[] BuildTeachingBytes(
        string? extractPath,
        Stream? extractStream,
        int startPage,
        int endPage)
    {
        if (extractPath is not null)
        {
            return TeachingPdfWriter.WritePageRangeToBytes(extractPath, startPage, endPage);
        }

        if (extractStream is not null)
        {
            return TeachingPdfWriter.WritePageRangeToBytes(extractStream, startPage, endPage);
        }

        throw new InvalidOperationException(
            "Cannot export teaching PDF: no PDF path or stream available.");
    }

    private async Task<(bool Success, string? Uri, string? ErrorCode, string? ErrorMessage)> WriteMarkdownAsync(
        ParseRequest request,
        string markdown,
        string? localOutputPath,
        string mdBlobName,
        CancellationToken ct)
    {
        string? destinationUri = null;
        var blobMode = request.BlobMode;

        // Local Markdown write
        if (!string.IsNullOrWhiteSpace(localOutputPath) && !blobMode)
        {
            if (!request.Overwrite && File.Exists(localOutputPath))
            {
                return (
                    false,
                    null,
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
            catch (InvalidOperationException ex) when (ex.Message.Contains(
                                                           "Container not found",
                                                           StringComparison.OrdinalIgnoreCase))
            {
                return (false, null, ParseErrorCodes.ContainerNotFound, ex.Message);
            }

            destinationUri = _blobStore.GetBlobUri(_blobOptions.DestinationContainer, mdBlobName);
        }

        return (true, destinationUri, null, null);
    }

    private async Task<(bool Success, byte[]? Bytes, string? TempPath, string? ErrorCode, string? ErrorMessage)>
        LoadTeachingBytesForMarkdownAsync(
            ParseRequest request,
            string? localTeachingPath,
            string teachingBlobName,
            byte[]? alreadyHave,
            CancellationToken ct)
    {
        // When we just built teachingBytes in this run, use them (skip only affects re-upload).
        // SkippedExisting with Bytes already loaded: prefer existing file bytes for true reuse.
        if (!string.IsNullOrWhiteSpace(localTeachingPath) && File.Exists(localTeachingPath))
        {
            var bytes = await File.ReadAllBytesAsync(localTeachingPath, ct).ConfigureAwait(false);
            return (true, bytes, null, null, null);
        }

        if (request.BlobMode && _blobStore is not null)
        {
            var temp = CreateTempPdfPath(teachingBlobName);
            try
            {
                await _blobStore.DownloadToFileAsync(
                    _blobOptions.SourceContainer,
                    teachingBlobName,
                    temp,
                    ct).ConfigureAwait(false);
            }
            catch (FileNotFoundException ex)
            {
                return (false, null, null, ParseErrorCodes.SourceNotFound, ex.Message);
            }

            var bytes = await File.ReadAllBytesAsync(temp, ct).ConfigureAwait(false);
            return (true, bytes, temp, null, null);
        }

        if (alreadyHave is { Length: > 0 })
        {
            return (true, alreadyHave, null, null, null);
        }

        return (
            false,
            null,
            null,
            ParseErrorCodes.SourceNotFound,
            "Teaching PDF required for Markdown was not found.");
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

    private void DeleteTempQuietly(string? path)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete temp PDF {Path}", path);
        }
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
    /// When input is already a teaching PDF, the teaching file is the input itself.
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

        if (request.FromTeaching && !string.IsNullOrWhiteSpace(request.LocalInputPath))
        {
            return Path.GetFullPath(request.LocalInputPath);
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

    private async Task<TeachingExportResult> TryExportTeachingPdfAsync(
        ParseRequest request,
        byte[] teachingBytes,
        AnchorResult anchors,
        string? localTeachingPath,
        string teachingBlobName,
        CancellationToken ct)
    {
        var blobMode = request.BlobMode;
        var start = anchors.ContentStartPage;
        var end = anchors.ContentEndPage;

        // Skip existing teaching artifact only (MD may still be written from that file).
        if (request.SkipIfDestinationExists)
        {
            if (blobMode && _blobStore is not null)
            {
                if (await _blobStore.ExistsAsync(
                        _blobOptions.SourceContainer, teachingBlobName, ct).ConfigureAwait(false))
                {
                    var uri = _blobStore.GetBlobUri(_blobOptions.SourceContainer, teachingBlobName);
                    _logger.LogInformation("Skip existing teaching blob: {Uri}", uri);
                    return TeachingExportResult.Skipped(uri);
                }
            }
            else if (!string.IsNullOrWhiteSpace(localTeachingPath) && File.Exists(localTeachingPath))
            {
                _logger.LogInformation("Skip existing teaching PDF: {Path}", localTeachingPath);
                return TeachingExportResult.Skipped(localTeachingPath);
            }
        }

        string? teachingUri = null;

        // Local write (next to .md)
        if (!string.IsNullOrWhiteSpace(localTeachingPath) && !blobMode)
        {
            if (!request.Overwrite && File.Exists(localTeachingPath))
            {
                return TeachingExportResult.Fail(
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
            catch (InvalidOperationException ex) when (ex.Message.Contains(
                                                           "Container not found",
                                                           StringComparison.OrdinalIgnoreCase))
            {
                return TeachingExportResult.Fail(ParseErrorCodes.ContainerNotFound, ex.Message);
            }
            catch (IOException ex)
            {
                return TeachingExportResult.Fail(ParseErrorCodes.UploadFailed, ex.Message);
            }

            teachingUri = _blobStore.GetBlobUri(_blobOptions.SourceContainer, teachingBlobName);
            _logger.LogInformation(
                "Uploaded teaching PDF pages={Start}-{End} -> {Uri}",
                start,
                end,
                teachingUri);
        }

        if (teachingUri is null)
        {
            _logger.LogInformation(
                "Teaching PDF built in memory ({Bytes} bytes, pages {Start}-{End}) but no local/blob destination.",
                teachingBytes.Length,
                start,
                end);
        }

        return TeachingExportResult.Ok(teachingUri, teachingBytes);
    }

    private static ParseResult Fail(string code, string message) =>
        new(Success: false, Message: $"{code}: {message}");

    private sealed class TeachingExportResult
    {
        public bool Success { get; private init; }
        public bool SkippedExisting { get; private init; }
        public string? Uri { get; private init; }
        public byte[]? Bytes { get; private init; }
        public string? ErrorCode { get; private init; }
        public string? ErrorMessage { get; private init; }

        public static TeachingExportResult Ok(string? uri, byte[] bytes) =>
            new() { Success = true, Uri = uri, Bytes = bytes };

        public static TeachingExportResult Skipped(string? uri) =>
            new() { Success = true, SkippedExisting = true, Uri = uri };

        public static TeachingExportResult Fail(string code, string message) =>
            new() { Success = false, ErrorCode = code, ErrorMessage = message };
    }
}
