using LivingMessiah.ShabbatPdf.Core.Extraction;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace LivingMessiah.ShabbatPdf.Tests.Extraction;

/// <summary>
/// Builds a tiny in-memory PDF (no multi-MB fixtures) and extracts lines via PdfPigPageSource.
/// </summary>
public class PdfPigPageSourceTests
{
    [Fact]
    public void ExtractPages_FromStream_ReturnsClusteredLines()
    {
        var pdfBytes = CreateSimplePdf();

        using var stream = new MemoryStream(pdfBytes);
        var source = new PdfPigPageSource();
        var pages = source.ExtractPages(stream);

        Assert.Single(pages);
        Assert.Equal(1, pages[0].PageNumber);
        Assert.Contains(pages[0].Lines, line =>
            line.Contains("Welcome", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(pages[0].Lines, line =>
            line.Contains("Bienvenido", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExtractPages_FromFile_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lmm-parse-test-{Guid.NewGuid():N}.pdf");
        try
        {
            File.WriteAllBytes(path, CreateSimplePdf());
            var pages = new PdfPigPageSource().ExtractPages(path);
            Assert.Single(pages);
            Assert.NotEmpty(pages[0].Lines);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static byte[] CreateSimplePdf()
    {
        var builder = new PdfDocumentBuilder();
        // Letter size in PDF points (no multi-MB agenda fixtures in repo)
        var page = builder.AddPage(612, 792);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        // Two lines far apart vertically so they do not merge
        page.AddText("Welcome", 24, new PdfPoint(72, 700), font);
        page.AddText("Bienvenido", 24, new PdfPoint(72, 400), font);

        return builder.Build();
    }
}
