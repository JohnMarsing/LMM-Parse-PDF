using System.Text.RegularExpressions;

namespace LivingMessiah.ShabbatPdf.Core.Extraction;

/// <summary>
/// Parses <c>YYYY-MM-DD-Citation.pdf</c> names and maps them to Markdown file names.
/// Teaching-only names (<c>*-teaching.pdf</c>) strip the suffix so date, citation, and MD names stay on the agenda base.
/// </summary>
public static partial class FilenameParser
{
    /// <summary>
    /// Standard agenda name: date, hyphen, citation, .pdf (case-insensitive extension).
    /// </summary>
    [GeneratedRegex(
        @"^(?<date>\d{4}-\d{2}-\d{2})-(?<citation>.+)\.pdf$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StandardNameRegex();

    public static FilenameParseResult Parse(string sourceFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFileName);

        var name = Path.GetFileName(sourceFileName.Trim());
        var baseName = Path.GetFileNameWithoutExtension(name);

        // *-teaching.pdf is step-1 output; strip so date/citation/MD names stay on the agenda base.
        if (baseName.EndsWith("-teaching", StringComparison.OrdinalIgnoreCase))
        {
            baseName = baseName[..^"-teaching".Length];
        }

        // Canonical agenda name for front matter and pattern match.
        var agendaFileName = baseName + ".pdf";

        var match = StandardNameRegex().Match(agendaFileName);
        if (match.Success)
        {
            var date = match.Groups["date"].Value;
            var citation = match.Groups["citation"].Value;
            var standardBase = $"{date}-{citation}";
            return new FilenameParseResult(
                SourceFileName: standardBase + ".pdf",
                IsStandardPattern: true,
                ServiceDate: date,
                Citation: citation,
                BaseNameWithoutExtension: standardBase);
        }

        return new FilenameParseResult(
            SourceFileName: agendaFileName,
            IsStandardPattern: false,
            ServiceDate: null,
            Citation: "unknown",
            BaseNameWithoutExtension: baseName);
    }

    /// <summary>
    /// True when the file name looks like a teaching-only PDF (<c>*-teaching.pdf</c>).
    /// </summary>
    public static bool IsTeachingPdfName(string sourceFileName)
    {
        if (string.IsNullOrWhiteSpace(sourceFileName))
        {
            return false;
        }

        var fileName = Path.GetFileName(sourceFileName.Trim());
        return fileName.EndsWith("-teaching.pdf", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when the name matches the standard date-citation PDF pattern
    /// (teaching suffix is ignored for the match).
    /// </summary>
    public static bool IsStandardBlobName(string sourceFileName) =>
        Parse(sourceFileName).IsStandardPattern;
}
