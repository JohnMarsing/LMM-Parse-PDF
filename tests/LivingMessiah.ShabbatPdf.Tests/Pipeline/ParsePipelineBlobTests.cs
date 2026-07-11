using LivingMessiah.ShabbatPdf.Core.Extraction;
using LivingMessiah.ShabbatPdf.Core.Models;
using LivingMessiah.ShabbatPdf.Core.Options;
using LivingMessiah.ShabbatPdf.Core.Pipeline;
using LivingMessiah.ShabbatPdf.Tests.Storage;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace LivingMessiah.ShabbatPdf.Tests.Pipeline;

public class ParsePipelineBlobTests
{
    private const string SourceContainer = "shabbat-service";
    private const string DestContainer = "shabbat-service-md";
    private const string PdfName = "2026-07-04-Lev-16.pdf";

    [Fact]
    public async Task RunAsync_BlobMode_DownloadsTemp_UploadsMarkdown()
    {
        var store = new InMemoryBlobStore();
        await store.EnsureContainerExistsAsync(SourceContainer);
        await store.EnsureContainerExistsAsync(DestContainer);
        store.Seed(SourceContainer, PdfName, CreateAgendaPdf());

        var pipeline = CreatePipeline(store);
        var result = await pipeline.RunAsync(new ParseRequest(
            SourceName: PdfName,
            BlobMode: true,
            RequireStandardBlobName: true));

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Anchors);
        Assert.Equal(3, result.Anchors!.ContentStartPage);
        Assert.Contains("2026-07-04-Lev-16.md", result.DestinationUri);
        Assert.True(store.Blobs.ContainsKey($"{DestContainer}/2026-07-04-Lev-16.md"));

        var md = System.Text.Encoding.UTF8.GetString(
            store.Blobs[$"{DestContainer}/2026-07-04-Lev-16.md"]);
        Assert.Contains("Jude 6 teaching text", md);
        Assert.Contains("source_pdf: 2026-07-04-Lev-16.pdf", md);
    }

    [Fact]
    public async Task RunAsync_BlobMode_MissingSource_ReturnsSourceNotFound()
    {
        var store = new InMemoryBlobStore();
        await store.EnsureContainerExistsAsync(SourceContainer);
        await store.EnsureContainerExistsAsync(DestContainer);

        var pipeline = CreatePipeline(store);
        var result = await pipeline.RunAsync(new ParseRequest(
            SourceName: PdfName,
            BlobMode: true));

        Assert.False(result.Success);
        Assert.StartsWith(ParseErrorCodes.SourceNotFound, result.Message);
    }

    [Fact]
    public async Task RunAsync_BlobMode_EnsureContainer_CreatesDestination()
    {
        var store = new InMemoryBlobStore();
        await store.EnsureContainerExistsAsync(SourceContainer);
        // destination not created yet
        store.Seed(SourceContainer, PdfName, CreateAgendaPdf());

        var pipeline = CreatePipeline(store);
        var result = await pipeline.RunAsync(new ParseRequest(
            SourceName: PdfName,
            BlobMode: true,
            EnsureDestinationContainer: true));

        Assert.True(result.Success, result.Message);
        Assert.True(store.Blobs.ContainsKey($"{DestContainer}/2026-07-04-Lev-16.md"));
    }

    [Fact]
    public async Task RunAsync_BlobMode_SkipExisting_DoesNotReupload()
    {
        var store = new InMemoryBlobStore();
        await store.EnsureContainerExistsAsync(SourceContainer);
        await store.EnsureContainerExistsAsync(DestContainer);
        store.Seed(SourceContainer, PdfName, CreateAgendaPdf());
        store.Seed(DestContainer, "2026-07-04-Lev-16.md", System.Text.Encoding.UTF8.GetBytes("keep me"));

        var pipeline = CreatePipeline(store);
        var result = await pipeline.RunAsync(new ParseRequest(
            SourceName: PdfName,
            BlobMode: true,
            SkipIfDestinationExists: true));

        Assert.True(result.Success, result.Message);
        var md = System.Text.Encoding.UTF8.GetString(
            store.Blobs[$"{DestContainer}/2026-07-04-Lev-16.md"]);
        Assert.Equal("keep me", md);
    }

    [Fact]
    public async Task RunAsync_BlobMode_InvalidName_FailsWhenRequired()
    {
        var store = new InMemoryBlobStore();
        await store.EnsureContainerExistsAsync(SourceContainer);
        await store.EnsureContainerExistsAsync(DestContainer);
        store.Seed(SourceContainer, "notes.pdf", CreateAgendaPdf());

        var pipeline = CreatePipeline(store);
        var result = await pipeline.RunAsync(new ParseRequest(
            SourceName: "notes.pdf",
            BlobMode: true,
            RequireStandardBlobName: true));

        Assert.False(result.Success);
        Assert.StartsWith(ParseErrorCodes.InvalidName, result.Message);
    }

    private static ParsePipeline CreatePipeline(InMemoryBlobStore store)
    {
        var parseOpts = Options.Create(new ParseOptions());
        var blobOpts = Options.Create(new BlobOptions
        {
            SourceContainer = SourceContainer,
            DestinationContainer = DestContainer
        });

        return new ParsePipeline(
            new PdfPigPageSource(parseOpts.Value),
            new AnchorLocator(parseOpts.Value),
            new MarkdownBuilder(),
            parseOpts,
            blobOpts,
            store);
    }

    private static byte[] CreateAgendaPdf()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        var p1 = builder.AddPage(612, 792);
        p1.AddText("Welcome", 24, new PdfPoint(72, 700), font);
        p1.AddText("Bienvenido", 24, new PdfPoint(72, 400), font);

        var p2 = builder.AddPage(612, 792);
        p2.AddText("Fair Use Policy and Legal Disclaimer", 14, new PdfPoint(72, 700), font);

        var p3 = builder.AddPage(612, 792);
        p3.AddText("Jude 6 teaching text", 14, new PdfPoint(72, 700), font);

        var p4 = builder.AddPage(612, 792);
        p4.AddText("The Avinu Prayer", 24, new PdfPoint(72, 700), font);

        return builder.Build();
    }
}
