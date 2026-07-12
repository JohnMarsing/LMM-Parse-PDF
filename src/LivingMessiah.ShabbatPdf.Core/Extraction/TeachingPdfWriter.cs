using UglyToad.PdfPig;
using UglyToad.PdfPig.Writer;

namespace LivingMessiah.ShabbatPdf.Core.Extraction;

/// <summary>
/// Copies a page range from a source PDF into a new PDF (teaching-only slice).
/// Uses PdfPig page import so visual content (text layer, images on those pages) is preserved.
/// </summary>
public static class TeachingPdfWriter
{
    /// <summary>
    /// Build a PDF containing pages <paramref name="startPage"/> through <paramref name="endPage"/>
    /// (1-based, inclusive) from <paramref name="sourcePdfPath"/>.
    /// </summary>
    public static byte[] WritePageRangeToBytes(string sourcePdfPath, int startPage, int endPage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePdfPath);
        ValidateRange(startPage, endPage);

        using var document = PdfDocument.Open(sourcePdfPath);
        return BuildBytes(document, startPage, endPage);
    }

    /// <summary>
    /// Build a PDF containing pages <paramref name="startPage"/> through <paramref name="endPage"/>
    /// (1-based, inclusive) from a seekable PDF stream.
    /// </summary>
    public static byte[] WritePageRangeToBytes(Stream sourcePdfStream, int startPage, int endPage)
    {
        ArgumentNullException.ThrowIfNull(sourcePdfStream);
        ValidateRange(startPage, endPage);

        if (sourcePdfStream.CanSeek)
        {
            sourcePdfStream.Position = 0;
        }

        using var document = PdfDocument.Open(sourcePdfStream);
        return BuildBytes(document, startPage, endPage);
    }

    /// <summary>
    /// Write the page-range PDF to <paramref name="destinationPdfPath"/> (creates parent directories).
    /// </summary>
    public static void WritePageRange(
        string sourcePdfPath,
        string destinationPdfPath,
        int startPage,
        int endPage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPdfPath);

        var bytes = WritePageRangeToBytes(sourcePdfPath, startPage, endPage);
        var directory = Path.GetDirectoryName(destinationPdfPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(destinationPdfPath, bytes);
    }

    private static byte[] BuildBytes(PdfDocument document, int startPage, int endPage)
    {
        var pageCount = document.NumberOfPages;
        if (startPage < 1 || endPage > pageCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endPage),
                $"Page range {startPage}-{endPage} is outside document (1-{pageCount}).");
        }

        var builder = new PdfDocumentBuilder();
        for (var page = startPage; page <= endPage; page++)
        {
            builder.AddPage(document, page);
        }

        return builder.Build();
    }

    private static void ValidateRange(int startPage, int endPage)
    {
        if (startPage < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(startPage), "Start page must be >= 1.");
        }

        if (endPage < startPage)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endPage),
                $"End page ({endPage}) must be >= start page ({startPage}).");
        }
    }
}
