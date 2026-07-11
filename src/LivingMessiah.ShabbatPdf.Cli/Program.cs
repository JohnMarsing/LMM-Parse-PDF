using System.CommandLine;
using System.Reflection;
using LivingMessiah.ShabbatPdf.Core.Extraction;
using LivingMessiah.ShabbatPdf.Core.Models;
using LivingMessiah.ShabbatPdf.Core.Options;
using LivingMessiah.ShabbatPdf.Core.Pipeline;
using LivingMessiah.ShabbatPdf.Core.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Exit codes: 0 success, 1 validation/anchor, 2 I/O, 3 unexpected
const int ExitOk = 0;
const int ExitValidation = 1;
const int ExitIo = 2;
const int ExitUnexpected = 3;

var inputOption = new Option<FileInfo?>("--input", "-i")
{
    Description = "Path to a local Shabbat agenda PDF."
};

var outputOption = new Option<FileInfo?>("--output", "-o")
{
    Description = "Path for the Markdown file (local mode). Default: same folder as the PDF with .md extension."
};

var blobOption = new Option<string?>("--blob", "-b")
{
    Description = "Blob name in the source container (e.g. 2026-07-04-Lev-16.pdf). Downloads to a temp file, uploads .md to destination."
};

var dryRunOption = new Option<bool>("--dry-run")
{
    Description = "Parse and build Markdown without writing local file or uploading blob.",
    DefaultValueFactory = _ => false
};

var skipExistingOption = new Option<bool>("--skip-existing")
{
    Description = "If the destination already exists, exit successfully without reprocessing.",
    DefaultValueFactory = _ => false
};

var ensureContainerOption = new Option<bool>("--ensure-container")
{
    Description = "Create the destination container if it does not exist (requires create permission).",
    DefaultValueFactory = _ => false
};

var allowNonstandardOption = new Option<bool>("--allow-nonstandard-name")
{
    Description = "In --blob mode, allow PDF names that do not match YYYY-MM-DD-Citation.pdf.",
    DefaultValueFactory = _ => false
};

var root = new RootCommand(
    "Parse Living Messiah Shabbat agenda PDFs to Markdown (local file or Azure blob).")
{
    inputOption,
    outputOption,
    blobOption,
    dryRunOption,
    skipExistingOption,
    ensureContainerOption,
    allowNonstandardOption
};

root.SetAction(async (parseResult, cancellationToken) =>
{
    var input = parseResult.GetValue(inputOption);
    var output = parseResult.GetValue(outputOption);
    var blobName = parseResult.GetValue(blobOption);
    var dryRun = parseResult.GetValue(dryRunOption);
    var skipExisting = parseResult.GetValue(skipExistingOption);
    var ensureContainer = parseResult.GetValue(ensureContainerOption);
    var allowNonstandard = parseResult.GetValue(allowNonstandardOption);

    var blobMode = !string.IsNullOrWhiteSpace(blobName);
    var localMode = input is not null;

    if (blobMode == localMode)
    {
        Console.Error.WriteLine("Error: specify exactly one of --input (local) or --blob (Azure).");
        Console.Error.WriteLine("Examples:");
        Console.Error.WriteLine(
            "  dotnet run --project src/LivingMessiah.ShabbatPdf.Cli -- --input agenda.pdf --output agenda.md");
        Console.Error.WriteLine(
            "  dotnet run --project src/LivingMessiah.ShabbatPdf.Cli -- --blob 2026-07-04-Lev-16.pdf");
        return ExitValidation;
    }

    if (localMode && !input!.Exists)
    {
        Console.Error.WriteLine($"Error: PDF not found: {input.FullName}");
        return ExitIo;
    }

    // Do not pass CLI args into the host (System.CommandLine already owns them).
    var host = Host.CreateApplicationBuilder(Array.Empty<string>());
    host.Configuration
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
        .AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true)
        .AddEnvironmentVariables();

    host.Services.Configure<ParseOptions>(host.Configuration.GetSection(ParseOptions.SectionName));
    host.Services.Configure<BlobOptions>(host.Configuration.GetSection(BlobOptions.SectionName));

    host.Services.AddSingleton<IPdfPageSource>(sp =>
    {
        var opts = sp.GetRequiredService<IOptions<ParseOptions>>().Value;
        return new PdfPigPageSource(opts);
    });
    host.Services.AddSingleton(sp =>
    {
        var opts = sp.GetRequiredService<IOptions<ParseOptions>>().Value;
        return new AnchorLocator(opts);
    });
    host.Services.AddSingleton<MarkdownBuilder>();

    if (blobMode)
    {
        host.Services.AddSingleton<IBlobStore>(sp =>
        {
            var blobOpts = sp.GetRequiredService<IOptions<BlobOptions>>().Value;
            return new AzureBlobStore(blobOpts);
        });
    }

    host.Services.AddSingleton<IParsePipeline, ParsePipeline>();

    using var app = host.Build();
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Cli");
    var pipeline = app.Services.GetRequiredService<IParsePipeline>();
    var parseOptions = app.Services.GetRequiredService<IOptions<ParseOptions>>().Value;

    ParseRequest request;
    string displayName;

    if (blobMode)
    {
        displayName = Path.GetFileName(blobName!);
        request = new ParseRequest(
            SourceName: displayName,
            BlobMode: true,
            EnsureDestinationContainer: ensureContainer,
            Overwrite: parseOptions.Overwrite,
            SkipIfDestinationExists: skipExisting,
            DryRun: dryRun,
            RequireStandardBlobName: !allowNonstandard && parseOptions.RequireStandardBlobName);
    }
    else
    {
        displayName = input!.Name;
        request = new ParseRequest(
            SourceName: input.Name,
            LocalInputPath: input.FullName,
            LocalOutputPath: output?.FullName,
            Overwrite: parseOptions.Overwrite,
            SkipIfDestinationExists: skipExisting,
            DryRun: dryRun,
            RequireStandardBlobName: false,
            BlobMode: false);
    }

    LivingMessiah.ShabbatPdf.Core.Models.ParseResult result;
    try
    {
        result = await pipeline.RunAsync(request, cancellationToken).ConfigureAwait(false);
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("ConnectionString", StringComparison.OrdinalIgnoreCase)
                                               || ex.Message.Contains("ServiceUri", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return ExitIo;
    }

    if (result.Success)
    {
        var anchors = result.Anchors;
        if (anchors is not null)
        {
            Console.WriteLine(
                $"OK {displayName} pages={anchors.ContentStartPage}-{anchors.ContentEndPage} " +
                $"anchors={anchors.StartAnchorPage}/{anchors.EndAnchorPage} " +
                $"introSkip={string.Join(',', anchors.IntroSkippedPages)} " +
                $"end={anchors.EndMatchMethod} " +
                $"chars={result.Markdown?.Length ?? 0} " +
                $"-> {result.DestinationUri ?? "(dry-run)"}");
        }
        else
        {
            Console.WriteLine($"OK {result.Message}");
        }

        return ExitOk;
    }

    Console.Error.WriteLine($"Error: {result.Message}");
    logger.LogError("Parse failed: {Message}", result.Message);

    return MapExitCode(result.Message);
});

return await root.Parse(args).InvokeAsync();

static int MapExitCode(string message)
{
    if (message.StartsWith("SourceNotFound", StringComparison.Ordinal)
        || message.StartsWith("IoError", StringComparison.Ordinal)
        || message.StartsWith("UploadFailed", StringComparison.Ordinal)
        || message.StartsWith("ContainerNotFound", StringComparison.Ordinal))
    {
        return ExitIo;
    }

    if (message.StartsWith("Unexpected", StringComparison.Ordinal))
    {
        return ExitUnexpected;
    }

    // AnchorNotFound, EmptySlice, InvalidName, …
    return ExitValidation;
}
