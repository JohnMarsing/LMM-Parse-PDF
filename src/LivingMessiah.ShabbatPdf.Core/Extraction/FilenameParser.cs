using System.Text.RegularExpressions;

namespace LivingMessiah.ShabbatPdf.Core.Extraction;

/// <summary>
/// Parses <c>YYYY-MM-DD-Citation.pdf</c> names and maps them to Markdown file names.
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

        var match = StandardNameRegex().Match(name);
        if (match.Success)
        {
            return new FilenameParseResult(
                SourceFileName: name,
                IsStandardPattern: true,
                ServiceDate: match.Groups["date"].Value,
                Citation: match.Groups["citation"].Value,
                BaseNameWithoutExtension: baseName);
        }

        return new FilenameParseResult(
            SourceFileName: name,
            IsStandardPattern: false,
            ServiceDate: null,
            Citation: "unknown",
            BaseNameWithoutExtension: baseName);
    }

    /// <summary>
    /// True when the name matches the standard date-citation PDF pattern.
    /// </summary>
    public static bool IsStandardBlobName(string sourceFileName) =>
        Parse(sourceFileName).IsStandardPattern;
}
