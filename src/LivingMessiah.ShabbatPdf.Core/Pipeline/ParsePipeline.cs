using LivingMessiah.ShabbatPdf.Core.Extraction;
using LivingMessiah.ShabbatPdf.Core.Models;
using LivingMessiah.ShabbatPdf.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LivingMessiah.ShabbatPdf.Core.Pipeline;

/// <summary>
/// Orchestrates extract → anchors → markdown → optional local write.
/// Azure blob I/O is added in a later PR.
/// </summary>
public sealed class ParsePipeline : IParsePipeline
{
    private readonly IPdfPageSource _pageSource;
    private readonly AnchorLocator _anchorLocator;
    private readonly MarkdownBuilder _markdownBuilder;
    private readonly ParseOptions _options;
    private readonly ILogger<ParsePipeline> _logger;

    public ParsePipeline(
        IPdfPageSource pageSource,
        AnchorLocator anchorLocator,
        MarkdownBuilder markdownBuilder,
        IOptions<ParseOptions>? options = null,
        ILogger<ParsePipeline>? logger = null)
    {
        _pageSource = pageSource ?? throw new ArgumentNullException(nameof(pageSource));
        _anchorLocator = anchorLocator ?? throw new ArgumentNullException(nameof(anchorLocator));
        _markdownBuilder = markdownBuilder ?? throw new ArgumentNullException(nameof(markdownBuilder));
        _options = options?.Value ?? new ParseOptions();
        _logger = logger ?? NullLogger<ParsePipeline>.Instance;
    }

    public async Task<ParseResult> RunAsync(ParseRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            if (string.IsNullOrWhiteSpace(request.LocalInputPath)
                && request.PdfStream is null)
            {
                return Fail(
                    ParseErrorCodes.SourceNotFound,
                    "No PDF source: set LocalInputPath or PdfStream.");
            }

            var sourceName = string.IsNullOrWhiteSpace(request.SourceName)
                ? Path.GetFileName(request.LocalInputPath ?? "document.pdf")
                : Path.GetFileName(request.SourceName);

            var nameMeta = FilenameParser.Parse(sourceName);
            if (!nameMeta.IsStandardPattern)
            {
                _logger.LogWarning(
                    "Non-standard PDF name '{SourceName}'; citation will be 'unknown'.",
                    sourceName);
            }

            // Local mode is lenient; blob mode (later) can require the standard pattern.
            if (request.RequireStandardBlobName && !nameMeta.IsStandardPattern)
            {
                return Fail(
                    ParseErrorCodes.InvalidName,
                    $"Blob name must match YYYY-MM-DD-Citation.pdf: '{sourceName}'.");
            }

            var outputPath = ResolveOutputPath(request, nameMeta);

            if (request.SkipIfDestinationExists
                && !string.IsNullOrWhiteSpace(outputPath)
                && File.Exists(outputPath))
            {
                _logger.LogInformation("Skip existing output: {OutputPath}", outputPath);
                return new ParseResult(
                    Success: true,
                    Message: "Skipped: destination already exists.",
                    DestinationUri: outputPath);
            }

            if (!request.Overwrite
                && !string.IsNullOrWhiteSpace(outputPath)
                && File.Exists(outputPath)
                && !request.DryRun)
            {
                return Fail(
                    ParseErrorCodes.UploadFailed,
                    $"Output exists and overwrite is disabled: {outputPath}");
            }

            ct.ThrowIfCancellationRequested();

            IReadOnlyList<PdfPageText> pages;
            if (!string.IsNullOrWhiteSpace(request.LocalInputPath))
            {
                if (!File.Exists(request.LocalInputPath))
                {
                    return Fail(
                        ParseErrorCodes.SourceNotFound,
                        $"PDF file not found: {request.LocalInputPath}");
                }

                _logger.LogInformation("Extracting pages from {Path}", request.LocalInputPath);
                pages = _pageSource.ExtractPages(request.LocalInputPath);
            }
            else
            {
                _logger.LogInformation("Extracting pages from stream ({SourceName})", sourceName);
                pages = _pageSource.ExtractPages(request.PdfStream!);
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

            var slice = ContentSlicer.Slice(pages, anchors);
            var markdown = _markdownBuilder.Build(slice, sourceName);

            if (request.DryRun)
            {
                _logger.LogInformation(
                    "Dry-run OK pages={Start}-{End} chars={Chars}",
                    anchors.ContentStartPage,
                    anchors.ContentEndPage,
                    markdown.Length);

                return new ParseResult(
                    Success: true,
                    Message: "Dry-run succeeded.",
                    Markdown: markdown,
                    Anchors: anchors,
                    DestinationUri: outputPath);
            }

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                return new ParseResult(
                    Success: true,
                    Message: "Parsed successfully (no output path).",
                    Markdown: markdown,
                    Anchors: anchors);
            }

            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(outputPath, markdown, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "OK {Source} pages={Start}-{End} anchors={AStart}/{AEnd} introSkip={Intro} end={Method} chars={Chars} -> {Output}",
                sourceName,
                anchors.ContentStartPage,
                anchors.ContentEndPage,
                anchors.StartAnchorPage,
                anchors.EndAnchorPage,
                string.Join(',', anchors.IntroSkippedPages),
                anchors.EndMatchMethod,
                markdown.Length,
                outputPath);

            return new ParseResult(
                Success: true,
                Message: "OK",
                Markdown: markdown,
                Anchors: anchors,
                DestinationUri: outputPath);
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
    }

    private static string? ResolveOutputPath(ParseRequest request, FilenameParseResult nameMeta)
    {
        if (!string.IsNullOrWhiteSpace(request.LocalOutputPath))
        {
            return Path.GetFullPath(request.LocalOutputPath);
        }

        if (!string.IsNullOrWhiteSpace(request.LocalInputPath))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(request.LocalInputPath)) ?? ".";
            return Path.Combine(dir, nameMeta.MarkdownFileName);
        }

        return null;
    }

    private static ParseResult Fail(string code, string message) =>
        new(Success: false, Message: $"{code}: {message}");
}
