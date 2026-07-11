using LivingMessiah.ShabbatPdf.Core.Models;

namespace LivingMessiah.ShabbatPdf.Core.Extraction;

/// <summary>
/// Selects pages in the inclusive content range from an anchor result.
/// </summary>
public static class ContentSlicer
{
    public static IReadOnlyList<PdfPageText> Slice(
        IReadOnlyList<PdfPageText> pages,
        AnchorResult anchors)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(anchors);

        return pages
            .Where(p =>
                p.PageNumber >= anchors.ContentStartPage
                && p.PageNumber <= anchors.ContentEndPage)
            .OrderBy(p => p.PageNumber)
            .ToList();
    }

    public static IReadOnlyList<PdfPageText> Slice(
        IReadOnlyList<PdfPageText> pages,
        int contentStartPage,
        int contentEndPage)
    {
        ArgumentNullException.ThrowIfNull(pages);

        return pages
            .Where(p =>
                p.PageNumber >= contentStartPage
                && p.PageNumber <= contentEndPage)
            .OrderBy(p => p.PageNumber)
            .ToList();
    }
}
