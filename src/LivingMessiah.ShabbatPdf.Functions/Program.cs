using LivingMessiah.ShabbatPdf.Core.Extraction;
using LivingMessiah.ShabbatPdf.Core.Options;
using LivingMessiah.ShabbatPdf.Core.Pipeline;
using LivingMessiah.ShabbatPdf.Core.Storage;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// appsettings.json + environment / local.settings Values (Blob__*, Parse__*)
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables();

builder.Services.Configure<ParseOptions>(builder.Configuration.GetSection(ParseOptions.SectionName));
builder.Services.Configure<BlobOptions>(builder.Configuration.GetSection(BlobOptions.SectionName));

builder.Services.AddSingleton<IPdfPageSource>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<ParseOptions>>().Value;
    return new PdfPigPageSource(opts);
});

builder.Services.AddSingleton(sp =>
{
    var opts = sp.GetRequiredService<IOptions<ParseOptions>>().Value;
    return new AnchorLocator(opts);
});

builder.Services.AddSingleton<MarkdownBuilder>();

builder.Services.AddSingleton<IBlobStore>(sp =>
{
    var blobOpts = sp.GetRequiredService<IOptions<BlobOptions>>().Value;
    return new AzureBlobStore(blobOpts);
});

builder.Services.AddSingleton<IParsePipeline, ParsePipeline>();

builder.Build().Run();
