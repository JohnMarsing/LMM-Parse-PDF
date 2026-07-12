namespace LivingMessiah.ShabbatPdf.Core.Extraction;

/// <summary>
/// Result of parsing a Shabbat agenda PDF file name.
/// </summary>
public sealed record FilenameParseResult(
    string SourceFileName,
    bool IsStandardPattern,
    string? ServiceDate,
    string Citation,
    string BaseNameWithoutExtension)
{
    /// <summary>Destination blob/file name with .md extension.</summary>
    public string MarkdownFileName => BaseNameWithoutExtension + ".md";

    /// <summary>
    /// Teaching-only PDF name: same base with <c>-teaching</c> before the extension
    /// (e.g. <c>2026-07-04-Lev-16-teaching.pdf</c>).
    /// </summary>
    public string TeachingPdfFileName => BaseNameWithoutExtension + "-teaching.pdf";
}
