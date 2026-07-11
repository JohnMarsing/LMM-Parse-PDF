using LivingMessiah.ShabbatPdf.Core.Models;
using LivingMessiah.ShabbatPdf.Core.Options;
using System.Text.RegularExpressions;

namespace LivingMessiah.ShabbatPdf.Core.Extraction;

/// <summary>
/// Locates Welcome/Bienvenido start, Avinu end, and applies intro-page skip.
/// </summary>
public sealed class AnchorLocator
{
    public const string EndMatchLine = "Line";
    public const string EndMatchCollapsed = "Collapsed";
    public const string EndMatchMultiLineSequence = "MultiLineSequence";

    private readonly ParseOptions _options;

    public AnchorLocator(ParseOptions? options = null)
    {
        _options = options ?? new ParseOptions();
    }

    /// <summary>
    /// Locate anchors on pre-extracted pages. Throws <see cref="AnchorException"/> on failure.
    /// </summary>
    public AnchorResult Locate(IReadOnlyList<PdfPageText> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        if (pages.Count == 0)
        {
            throw AnchorException.StartNotFound();
        }

        var byNumber = pages.ToDictionary(p => p.PageNumber);
        var orderedNumbers = pages.Select(p => p.PageNumber).OrderBy(n => n).ToList();

        var startPage = FindStartPage(orderedNumbers, byNumber)
            ?? throw AnchorException.StartNotFound();

        var provisionalStart = startPage + 1;

        var (endPage, endMethod) = FindEndPage(orderedNumbers, byNumber, provisionalStart)
            ?? throw AnchorException.EndNotFound();

        var contentEnd = endPage - 1;
        if (contentEnd < provisionalStart)
        {
            throw AnchorException.EmptySlice(
                $"Empty content range: provisional start {provisionalStart}, end anchor page {endPage}.");
        }

        var introSkipped = new List<int>();
        var contentStart = provisionalStart;

        if (_options.SkipIntroPages)
        {
            while (contentStart <= contentEnd)
            {
                if (!byNumber.TryGetValue(contentStart, out var page) || !IsIntroSkipPage(page))
                {
                    break;
                }

                introSkipped.Add(contentStart);
                contentStart++;
            }
        }

        if (contentStart > contentEnd)
        {
            throw AnchorException.EmptySlice(
                "Empty content range after intro-page skip (only intro pages between Welcome and Avinu).");
        }

        return new AnchorResult(
            StartAnchorPage: startPage,
            EndAnchorPage: endPage,
            ProvisionalContentStartPage: provisionalStart,
            ContentStartPage: contentStart,
            ContentEndPage: contentEnd,
            EndMatchMethod: endMethod,
            IntroSkippedPages: introSkipped);
    }

    /// <summary>
    /// True if any line contains a configured intro substring (case-insensitive).
    /// </summary>
    public bool IsIntroSkipPage(PdfPageText page)
    {
        ArgumentNullException.ThrowIfNull(page);

        foreach (var line in page.Lines)
        {
            var normalized = NormalizeLine(line);
            if (normalized.Length == 0)
            {
                continue;
            }

            foreach (var pattern in _options.IntroSkipLineContains)
            {
                if (string.IsNullOrWhiteSpace(pattern))
                {
                    continue;
                }

                if (normalized.Contains(pattern.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private int? FindStartPage(
        IReadOnlyList<int> orderedNumbers,
        IReadOnlyDictionary<int, PdfPageText> byNumber)
    {
        var welcome = _options.StartWelcomeLine.Trim();
        var bienvenidos = _options.StartBienvenidoLines
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToList();

        foreach (var pageNumber in orderedNumbers)
        {
            var lines = byNumber[pageNumber].Lines;
            var welcomeIndex = -1;

            for (var i = 0; i < lines.Count; i++)
            {
                if (LineEquals(lines[i], welcome))
                {
                    welcomeIndex = i;
                    break;
                }
            }

            if (welcomeIndex < 0)
            {
                continue;
            }

            for (var j = welcomeIndex + 1; j < lines.Count; j++)
            {
                if (bienvenidos.Any(b => LineEquals(lines[j], b)))
                {
                    return pageNumber;
                }
            }
        }

        return null;
    }

    private (int Page, string Method)? FindEndPage(
        IReadOnlyList<int> orderedNumbers,
        IReadOnlyDictionary<int, PdfPageText> byNumber,
        int fromPageInclusive)
    {
        var phrase = _options.EndAvinuPhrase.Trim();
        var phraseHead = "The Avinu";
        var phraseTail = "Prayer";

        // If options override the full phrase, derive head/tail for multi-line fallback.
        var parts = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            phraseHead = string.Join(' ', parts.Take(parts.Length - 1));
            phraseTail = parts[^1];
        }

        foreach (var pageNumber in orderedNumbers.Where(n => n >= fromPageInclusive))
        {
            var page = byNumber[pageNumber];
            var lines = page.Lines
                .Select(NormalizeLine)
                .Where(l => l.Length > 0)
                .ToList();

            // 1) Primary – full line equals or starts with phrase
            foreach (var line in lines)
            {
                if (LineEqualsOrStartsWith(line, phrase))
                {
                    return (pageNumber, EndMatchLine);
                }
            }

            // 2) Multi-line: "The Avinu" then next non-empty "Prayer"
            //    (split title when clustering is tight — check before collapsed so we
            //     report the more specific match when both would succeed)
            for (var i = 0; i < lines.Count - 1; i++)
            {
                if (LineEqualsOrStartsWith(lines[i], phraseHead)
                    && LineEqualsOrStartsWith(lines[i + 1], phraseTail))
                {
                    return (pageNumber, EndMatchMultiLineSequence);
                }
            }

            // 3) Collapsed page text contains phrase (mid-line / reflowed titles)
            if (page.CollapsedText.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                return (pageNumber, EndMatchCollapsed);
            }
        }

        return null;
    }

    private static string NormalizeLine(string line) =>
        Regex.Replace(line.Trim(), @"\s+", " ");

    private static bool LineEquals(string line, string expected)
    {
        var a = NormalizeLine(line);
        var b = expected.Trim();
        return a.Equals(b, StringComparison.OrdinalIgnoreCase);
    }

    private static bool LineEqualsOrStartsWith(string line, string expected)
    {
        var a = NormalizeLine(line);
        // strip trailing punctuation common on titles
        a = a.TrimEnd('.', ':', ';', ',', '!', '?', '—', '-');
        var b = expected.Trim();

        return a.Equals(b, StringComparison.OrdinalIgnoreCase)
               || a.StartsWith(b, StringComparison.OrdinalIgnoreCase);
    }
}
