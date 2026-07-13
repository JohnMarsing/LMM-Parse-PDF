using LivingMessiah.ShabbatPdf.Core.Extraction;
using LivingMessiah.ShabbatPdf.Core.Models;
using LivingMessiah.ShabbatPdf.Core.Options;
using LivingMessiah.ShabbatPdf.Core.Pipeline;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace LivingMessiah.ShabbatPdf.Tests.Pipeline;

public class ParsePipelineTests
{
    [Fact]
    public async Task RunAsync_LocalPdf_WritesMarkdown()
    {
        var pdfPath = Path.Combine(Path.GetTempPath(), $"lmm-pipe-{Guid.NewGuid():N}.pdf");
        var mdPath = Path.ChangeExtension(pdfPath, ".md");
        var teachingPath = Path.Combine(
            Path.GetDirectoryName(mdPath)!,
            "2026-07-04-Lev-16-teaching.pdf");

        try
        {
            File.WriteAllBytes(pdfPath, CreateAgendaPdf());

            var pipeline = CreatePipeline();
            var result = await pipeline.RunAsync(new ParseRequest(
                SourceName: "2026-07-04-Lev-16.pdf",
                LocalInputPath: pdfPath,
                LocalOutputPath: mdPath,
                RequireStandardBlobName: false));

            Assert.True(result.Success, result.Message);
            Assert.True(File.Exists(mdPath));
            Assert.NotNull(result.Anchors);
            Assert.Equal(1, result.Anchors!.StartAnchorPage);
            Assert.Equal(4, result.Anchors.EndAnchorPage);
            Assert.Equal(3, result.Anchors.ContentStartPage); // skip intro page 2
            Assert.Equal(3, result.Anchors.ContentEndPage);
            Assert.Contains("source_pdf: 2026-07-04-Lev-16.pdf", result.Markdown);
            // MD is built from teaching PDF pages (renumbered 1…N), not full-agenda page numbers.
            Assert.Contains("<!-- page 1 -->", result.Markdown);
            Assert.Contains("extracted_pages: 1", result.Markdown);
            Assert.Contains("Jude 6 teaching text", result.Markdown);
            Assert.DoesNotContain("Fair Use", result.Markdown, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<!-- page 3 -->", result.Markdown);

            // Teaching PDF next to .md (content page only)
            Assert.True(File.Exists(teachingPath), "Expected teaching PDF next to Markdown.");
            Assert.Equal(teachingPath, result.TeachingPdfUri);
            using (var teachDoc = PdfDocument.Open(teachingPath))
            {
                Assert.Equal(1, teachDoc.NumberOfPages);
                Assert.Contains("Jude 6 teaching text", teachDoc.GetPage(1).Text);
            }
        }
        finally
        {
            TryDelete(pdfPath);
            TryDelete(mdPath);
            TryDelete(teachingPath);
        }
    }

    [Fact]
    public async Task RunAsync_LocalPdf_TeachingOnly_WritesTeaching_NotMarkdown()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"lmm-pipe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var pdfPath = Path.Combine(dir, "2026-07-04-Lev-16.pdf");
        var mdPath = Path.Combine(dir, "2026-07-04-Lev-16.md");
        var teachingPath = Path.Combine(dir, "2026-07-04-Lev-16-teaching.pdf");

        try
        {
            File.WriteAllBytes(pdfPath, CreateAgendaPdf());

            var pipeline = CreatePipeline();
            var result = await pipeline.RunAsync(new ParseRequest(
                SourceName: "2026-07-04-Lev-16.pdf",
                LocalInputPath: pdfPath,
                LocalOutputPath: mdPath,
                TeachingOnly: true,
                RequireStandardBlobName: false));

            Assert.True(result.Success, result.Message);
            Assert.Null(result.Markdown);
            Assert.Null(result.DestinationUri);
            Assert.True(File.Exists(teachingPath));
            Assert.False(File.Exists(mdPath));
            Assert.Equal(teachingPath, result.TeachingPdfUri);

            using var teachDoc = PdfDocument.Open(teachingPath);
            Assert.Equal(1, teachDoc.NumberOfPages);
            Assert.Contains("Jude 6 teaching text", teachDoc.GetPage(1).Text);
        }
        finally
        {
            TryDelete(pdfPath);
            TryDelete(mdPath);
            TryDelete(teachingPath);
            try { Directory.Delete(dir); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task RunAsync_LocalPdf_SkipExisting_SkipsTeachingWhenPresent()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"lmm-pipe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var pdfPath = Path.Combine(dir, "2026-07-04-Lev-16.pdf");
        var mdPath = Path.Combine(dir, "2026-07-04-Lev-16.md");
        var teachingPath = Path.Combine(dir, "2026-07-04-Lev-16-teaching.pdf");

        try
        {
            File.WriteAllBytes(pdfPath, CreateAgendaPdf());
            // MD missing so pipeline does not early-exit; teaching already present (real PDF for MD step 2)
            File.WriteAllBytes(teachingPath, CreateTeachingOnlyPdf("existing teaching only"));
            var originalTeaching = await File.ReadAllBytesAsync(teachingPath);

            var pipeline = CreatePipeline();
            var result = await pipeline.RunAsync(new ParseRequest(
                SourceName: "2026-07-04-Lev-16.pdf",
                LocalInputPath: pdfPath,
                LocalOutputPath: mdPath,
                SkipIfDestinationExists: true,
                RequireStandardBlobName: false));

            Assert.True(result.Success, result.Message);
            Assert.True(File.Exists(mdPath));
            Assert.Equal(originalTeaching, await File.ReadAllBytesAsync(teachingPath));
            Assert.Equal(teachingPath, result.TeachingPdfUri);
            // Markdown must come from the existing teaching PDF, not a re-slice of the full agenda.
            Assert.Contains("existing teaching only", result.Markdown);
            Assert.DoesNotContain("Jude 6 teaching text", result.Markdown);
        }
        finally
        {
            TryDelete(pdfPath);
            TryDelete(mdPath);
            TryDelete(teachingPath);
            try
            {
                Directory.Delete(dir);
            }
            catch
            {
                // ignore
            }
        }
    }

    [Fact]
    public async Task RunAsync_FromTeaching_BuildsMarkdownWithoutAnchors()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"lmm-pipe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var teachingPath = Path.Combine(dir, "2026-07-04-Lev-16-teaching.pdf");
        var mdPath = Path.Combine(dir, "2026-07-04-Lev-16.md");

        try
        {
            File.WriteAllBytes(teachingPath, CreateTeachingOnlyPdf("from teaching body"));

            var pipeline = CreatePipeline();
            var result = await pipeline.RunAsync(new ParseRequest(
                SourceName: "2026-07-04-Lev-16-teaching.pdf",
                LocalInputPath: teachingPath,
                LocalOutputPath: mdPath,
                FromTeaching: true,
                RequireStandardBlobName: false));

            Assert.True(result.Success, result.Message);
            Assert.Null(result.Anchors);
            Assert.True(File.Exists(mdPath));
            Assert.Contains("source_pdf: 2026-07-04-Lev-16.pdf", result.Markdown);
            Assert.Contains("<!-- page 1 -->", result.Markdown);
            Assert.Contains("from teaching body", result.Markdown);
            Assert.DoesNotContain("Fair Use", result.Markdown, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(teachingPath);
            TryDelete(mdPath);
            try { Directory.Delete(dir); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task RunAsync_DryRun_DoesNotWriteFile()
    {
        var pdfPath = Path.Combine(Path.GetTempPath(), $"lmm-pipe-{Guid.NewGuid():N}.pdf");
        var mdPath = Path.ChangeExtension(pdfPath, ".md");

        try
        {
            File.WriteAllBytes(pdfPath, CreateAgendaPdf());

            var pipeline = CreatePipeline();
            var result = await pipeline.RunAsync(new ParseRequest(
                SourceName: "2026-07-04-Lev-16.pdf",
                LocalInputPath: pdfPath,
                LocalOutputPath: mdPath,
                DryRun: true,
                RequireStandardBlobName: false));

            Assert.True(result.Success, result.Message);
            Assert.False(File.Exists(mdPath));
            Assert.False(string.IsNullOrEmpty(result.Markdown));
            Assert.Null(result.TeachingPdfUri);
            var teachingPath = Path.Combine(
                Path.GetDirectoryName(mdPath)!,
                "2026-07-04-Lev-16-teaching.pdf");
            Assert.False(File.Exists(teachingPath));
        }
        finally
        {
            TryDelete(pdfPath);
            TryDelete(mdPath);
        }
    }

    [Fact]
    public async Task RunAsync_MissingFile_ReturnsSourceNotFound()
    {
        var pipeline = CreatePipeline();
        var result = await pipeline.RunAsync(new ParseRequest(
            SourceName: "missing.pdf",
            LocalInputPath: Path.Combine(Path.GetTempPath(), $"no-such-{Guid.NewGuid():N}.pdf"),
            RequireStandardBlobName: false));

        Assert.False(result.Success);
        Assert.StartsWith(ParseErrorCodes.SourceNotFound, result.Message);
    }

    [Fact]
    public async Task RunAsync_SkipExisting_DoesNotOverwrite()
    {
        var pdfPath = Path.Combine(Path.GetTempPath(), $"lmm-pipe-{Guid.NewGuid():N}.pdf");
        var mdPath = Path.ChangeExtension(pdfPath, ".md");

        try
        {
            File.WriteAllBytes(pdfPath, CreateAgendaPdf());
            await File.WriteAllTextAsync(mdPath, "already here");

            var pipeline = CreatePipeline();
            var result = await pipeline.RunAsync(new ParseRequest(
                SourceName: "2026-07-04-Lev-16.pdf",
                LocalInputPath: pdfPath,
                LocalOutputPath: mdPath,
                SkipIfDestinationExists: true,
                RequireStandardBlobName: false));

            Assert.True(result.Success, result.Message);
            Assert.Equal("already here", await File.ReadAllTextAsync(mdPath));
        }
        finally
        {
            TryDelete(pdfPath);
            TryDelete(mdPath);
        }
    }

    private static ParsePipeline CreatePipeline()
    {
        var options = Options.Create(new ParseOptions());
        return new ParsePipeline(
            new PdfPigPageSource(options.Value),
            new AnchorLocator(options.Value),
            new MarkdownBuilder(),
            options);
    }

    /// <summary>
    /// Page 1 Welcome/Bienvenido, page 2 intro, page 3 teaching, page 4 Avinu.
    /// </summary>
    private static byte[] CreateAgendaPdf()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        var p1 = builder.AddPage(612, 792);
        p1.AddText("Welcome", 24, new PdfPoint(72, 700), font);
        p1.AddText("Bienvenido", 24, new PdfPoint(72, 400), font);

        var p2 = builder.AddPage(612, 792);
        p2.AddText("Fair Use Policy and Legal Disclaimer", 14, new PdfPoint(72, 700), font);
        p2.AddText("What will talk about today?", 14, new PdfPoint(72, 650), font);

        var p3 = builder.AddPage(612, 792);
        p3.AddText("Jude 6 teaching text", 14, new PdfPoint(72, 700), font);

        var p4 = builder.AddPage(612, 792);
        p4.AddText("The Avinu Prayer", 24, new PdfPoint(72, 700), font);

        return builder.Build();
    }

    /// <summary>Single-page teaching-only PDF (step 1 output / --from-teaching input).</summary>
    private static byte[] CreateTeachingOnlyPdf(string bodyLine)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(612, 792);
        page.AddText(bodyLine, 14, new PdfPoint(72, 700), font);
        return builder.Build();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
