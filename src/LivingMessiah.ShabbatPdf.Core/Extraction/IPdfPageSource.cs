using LivingMessiah.ShabbatPdf.Core.Models;

namespace LivingMessiah.ShabbatPdf.Core.Extraction;

/// <summary>
/// Opens a PDF and yields normalized text-layer lines per page.
/// Implementation (PdfPig) lands in a later PR.
/// </summary>
public interface IPdfPageSource
{
    IReadOnlyList<PdfPageText> ExtractPages(string filePath);

    IReadOnlyList<PdfPageText> ExtractPages(Stream pdfStream);
}
