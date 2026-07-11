using System.CommandLine;
using LivingMessiah.ShabbatPdf.Core.Extraction;
using LivingMessiah.ShabbatPdf.Core.Models;
using LivingMessiah.ShabbatPdf.Core.Options;
using LivingMessiah.ShabbatPdf.Core.Pipeline;
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
    Description = "Path for the Markdown file. Default: same folder as the PDF with .md extension."
};

var dryRunOption = new Option<bool>("--dry-run")
{
    Description = "Parse and build Markdown without writing the output file.",
    DefaultValueFactory = _ => false
};

var skipExistingOption = new Option<bool>("--skip-existing")
{
    Description = "If the output file already exists, exit successfully without reprocessing.",
    DefaultValueFactory = _ => false
};

var root = new RootCommand(
    "Parse Living Messiah Shabbat agenda PDFs to Markdown (local files). Azure blob mode comes in a later release.")
{
    inputOption,
    outputOption,
    dryRunOption,
    skipExistingOption
};

root.SetAction(async (parseResult, cancellationToken) =>
{
    var input = parseResult.GetValue(inputOption);
    var output = parseResult.GetValue(outputOption);
    var dryRun = parseResult.GetValue(dryRunOption);
    var skipExisting = parseResult.GetValue(skipExistingOption);

    if (input is null)
    {
        Console.Error.WriteLine("Error: --input is required for local mode.");
        Console.Error.WriteLine("Example:");
        Console.Error.WriteLine(
            "  dotnet run --project src/LivingMessiah.ShabbatPdf.Cli -- --input agenda.pdf --output agenda.md");
        return ExitValidation;
    }

    if (!input.Exists)
    {
        Console.Error.WriteLine($"Error: PDF not found: {input.FullName}");
        return ExitIo;
    }

    // Do not pass CLI args into the host (System.CommandLine already owns them).
    var host = Host.CreateApplicationBuilder(Array.Empty<string>());
    host.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
    host.Services.Configure<ParseOptions>(host.Configuration.GetSection(ParseOptions.SectionName));
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
    host.Services.AddSingleton<IParsePipeline, ParsePipeline>();

    using var app = host.Build();
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Cli");
    var pipeline = app.Services.GetRequiredService<IParsePipeline>();
    var parseOptions = app.Services.GetRequiredService<IOptions<ParseOptions>>().Value;

    var request = new ParseRequest(
        SourceName: input.Name,
        LocalInputPath: input.FullName,
        LocalOutputPath: output?.FullName,
        Overwrite: parseOptions.Overwrite,
        SkipIfDestinationExists: skipExisting,
        DryRun: dryRun,
        // Local CLI allows ad-hoc names (warning only).
        RequireStandardBlobName: false);

    var result = await pipeline.RunAsync(request, cancellationToken).ConfigureAwait(false);

    if (result.Success)
    {
        var anchors = result.Anchors;
        if (anchors is not null)
        {
            Console.WriteLine(
                $"OK {input.Name} pages={anchors.ContentStartPage}-{anchors.ContentEndPage} " +
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
        || message.StartsWith("UploadFailed", StringComparison.Ordinal))
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
