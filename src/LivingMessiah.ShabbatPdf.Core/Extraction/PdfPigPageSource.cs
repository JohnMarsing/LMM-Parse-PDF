using LivingMessiah.ShabbatPdf.Core.Models;
using LivingMessiah.ShabbatPdf.Core.Options;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace LivingMessiah.ShabbatPdf.Core.Extraction;

/// <summary>
/// Extracts text-layer words via PdfPig and builds lines with <see cref="LineClusterer"/>.
/// Does not OCR images or reorder multi-column layouts.
/// </summary>
public sealed class PdfPigPageSource : IPdfPageSource
{
    private readonly LineClusterOptions _clusterOptions;

    public PdfPigPageSource(LineClusterOptions? clusterOptions = null)
    {
        _clusterOptions = clusterOptions ?? new LineClusterOptions();
    }

    public PdfPigPageSource(ParseOptions parseOptions)
        : this(parseOptions.ToLineClusterOptions())
    {
    }

    public IReadOnlyList<PdfPageText> ExtractPages(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using var document = PdfDocument.Open(filePath);
        return ExtractFromDocument(document);
    }

    public IReadOnlyList<PdfPageText> ExtractPages(Stream pdfStream)
    {
        ArgumentNullException.ThrowIfNull(pdfStream);

        using var document = PdfDocument.Open(pdfStream);
        return ExtractFromDocument(document);
    }

    private IReadOnlyList<PdfPageText> ExtractFromDocument(PdfDocument document)
    {
        var pages = new List<PdfPageText>(document.NumberOfPages);

        foreach (Page page in document.GetPages())
        {
            var words = page.GetWords()
                .Select(ToWordBox)
                .Where(w => !string.IsNullOrWhiteSpace(w.Text))
                .ToList();

            var lines = LineClusterer.ClusterLines(words, _clusterOptions);
            pages.Add(new PdfPageText(page.Number, lines));
        }

        return pages;
    }

    private static PdfWordBox ToWordBox(Word word)
    {
        var box = word.BoundingBox;
        return new PdfWordBox(
            Text: word.Text,
            Left: box.Left,
            Right: box.Right,
            Bottom: box.Bottom,
            Top: box.Top);
    }
}
