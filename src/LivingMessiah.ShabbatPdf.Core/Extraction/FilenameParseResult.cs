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
}
