using LivingMessiah.ShabbatPdf.Core.Extraction;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace LivingMessiah.ShabbatPdf.Tests.Extraction;

public class TeachingPdfWriterTests
{
    [Fact]
    public void WritePageRangeToBytes_CopiesOnlyRequestedPages()
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), $"lmm-teach-src-{Guid.NewGuid():N}.pdf");
        try
        {
            File.WriteAllBytes(sourcePath, CreateFourPagePdf());

            var bytes = TeachingPdfWriter.WritePageRangeToBytes(sourcePath, startPage: 2, endPage: 3);

            using var doc = PdfDocument.Open(bytes);
            Assert.Equal(2, doc.NumberOfPages);
            Assert.Contains("page-two", doc.GetPage(1).Text);
            Assert.Contains("page-three", doc.GetPage(2).Text);
            Assert.DoesNotContain("page-one", doc.GetPage(1).Text);
        }
        finally
        {
            TryDelete(sourcePath);
        }
    }

    [Fact]
    public void WritePageRange_WritesFile()
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), $"lmm-teach-src-{Guid.NewGuid():N}.pdf");
        var destPath = Path.Combine(Path.GetTempPath(), $"lmm-teach-out-{Guid.NewGuid():N}.pdf");
        try
        {
            File.WriteAllBytes(sourcePath, CreateFourPagePdf());

            TeachingPdfWriter.WritePageRange(sourcePath, destPath, startPage: 3, endPage: 3);

            Assert.True(File.Exists(destPath));
            using var doc = PdfDocument.Open(destPath);
            Assert.Equal(1, doc.NumberOfPages);
            Assert.Contains("page-three", doc.GetPage(1).Text);
        }
        finally
        {
            TryDelete(sourcePath);
            TryDelete(destPath);
        }
    }

    [Fact]
    public void WritePageRangeToBytes_InvalidRange_Throws()
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), $"lmm-teach-src-{Guid.NewGuid():N}.pdf");
        try
        {
            File.WriteAllBytes(sourcePath, CreateFourPagePdf());

            Assert.ThrowsAny<ArgumentException>(() =>
                TeachingPdfWriter.WritePageRangeToBytes(sourcePath, startPage: 3, endPage: 2));

            Assert.ThrowsAny<ArgumentException>(() =>
                TeachingPdfWriter.WritePageRangeToBytes(sourcePath, startPage: 1, endPage: 99));
        }
        finally
        {
            TryDelete(sourcePath);
        }
    }

    private static byte[] CreateFourPagePdf()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        foreach (var label in new[] { "page-one", "page-two", "page-three", "page-four" })
        {
            var page = builder.AddPage(612, 792);
            page.AddText(label, 14, new PdfPoint(72, 700), font);
        }

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
