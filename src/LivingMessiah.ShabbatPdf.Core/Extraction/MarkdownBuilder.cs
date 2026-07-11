using System.Text;
using LivingMessiah.ShabbatPdf.Core.Models;

namespace LivingMessiah.ShabbatPdf.Core.Extraction;

/// <summary>
/// Builds minimal v1 Markdown from sliced PDF page text (no images, no OCR).
/// </summary>
public sealed class MarkdownBuilder
{
    public const string ToolName = "LMM-Parse-PDF";

    /// <summary>
    /// Build Markdown for the given content pages (already sliced).
    /// </summary>
    /// <param name="pages">Content pages in order.</param>
    /// <param name="sourcePdfName">Original PDF file name (path or blob name).</param>
    /// <param name="generatedUtc">Timestamp for front matter; defaults to <see cref="DateTimeOffset.UtcNow"/>.</param>
    public string Build(
        IReadOnlyList<PdfPageText> pages,
        string sourcePdfName,
        DateTimeOffset? generatedUtc = null)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePdfName);

        var meta = FilenameParser.Parse(sourcePdfName);
        var utc = generatedUtc ?? DateTimeOffset.UtcNow;

        var orderedPages = pages.OrderBy(p => p.PageNumber).ToList();
        var extractedPages = orderedPages.Count switch
        {
            0 => string.Empty,
            1 => orderedPages[0].PageNumber.ToString(),
            _ => $"{orderedPages[0].PageNumber}-{orderedPages[^1].PageNumber}"
        };

        var sb = new StringBuilder();

        sb.AppendLine("---");
        sb.AppendLine($"source_pdf: {meta.SourceFileName}");
        sb.AppendLine($"service_date: {meta.ServiceDate ?? string.Empty}");
        sb.AppendLine($"citation: {meta.Citation}");
        sb.AppendLine($"extracted_pages: {extractedPages}");
        sb.AppendLine($"generated_utc: {utc.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}");
        sb.AppendLine($"tool: {ToolName}");
        sb.AppendLine("---");
        sb.AppendLine();

        if (meta.IsStandardPattern)
        {
            sb.AppendLine($"# {meta.ServiceDate} — {meta.Citation}");
        }
        else
        {
            sb.AppendLine($"# {meta.BaseNameWithoutExtension}");
        }

        sb.AppendLine();

        for (var i = 0; i < orderedPages.Count; i++)
        {
            var page = orderedPages[i];
            sb.AppendLine($"<!-- page {page.PageNumber} -->");

            foreach (var raw in page.Lines)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                var line = raw.Trim();
                sb.AppendLine(IsOptionalHeading(line) ? $"## {line}" : line);
            }

            if (i < orderedPages.Count - 1)
            {
                sb.AppendLine();
            }
        }

        return CollapseExcessBlankLines(sb.ToString());
    }

    /// <summary>
    /// Optional heading: length ≤ 60, no . ? !, and ALL CAPS (with a letter) or Title Case.
    /// </summary>
    public static bool IsOptionalHeading(string line)
    {
        if (line.Length is 0 or > 60)
        {
            return false;
        }

        if (line.Contains('.') || line.Contains('?') || line.Contains('!'))
        {
            return false;
        }

        var hasLetter = line.Any(char.IsLetter);
        if (!hasLetter)
        {
            return false;
        }

        // ALL CAPS (letters only compared; digits/punctuation other than .?! allowed)
        var letters = line.Where(char.IsLetter).ToArray();
        if (letters.Length > 0 && letters.All(char.IsUpper))
        {
            return true;
        }

        // Title Case: every whitespace-separated word starts with uppercase letter
        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return false;
        }

        return words.All(w =>
        {
            var firstLetter = w.FirstOrDefault(char.IsLetter);
            return firstLetter != default && char.IsUpper(firstLetter);
        });
    }

    private static string CollapseExcessBlankLines(string markdown)
    {
        // Normalize to \n then collapse 3+ newlines to 2 (one blank line max between blocks)
        var normalized = markdown.Replace("\r\n", "\n").Replace('\r', '\n');
        while (normalized.Contains("\n\n\n"))
        {
            normalized = normalized.Replace("\n\n\n", "\n\n");
        }

        return normalized.TrimEnd() + "\n";
    }
}
