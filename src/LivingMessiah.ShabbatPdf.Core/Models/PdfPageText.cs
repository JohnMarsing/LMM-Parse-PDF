using System.Text.RegularExpressions;

namespace LivingMessiah.ShabbatPdf.Core.Models;

/// <summary>
/// Normalized text-layer lines for one PDF page (no image OCR, no layout recovery).
/// </summary>
public sealed record PdfPageText(
    int PageNumber,
    IReadOnlyList<string> Lines)
{
    /// <summary>
    /// Whitespace-collapsed join of all lines (used for end-anchor phrase fallback).
    /// </summary>
    public string CollapsedText =>
        Regex.Replace(string.Join("\n", Lines), @"\s+", " ").Trim();
}
