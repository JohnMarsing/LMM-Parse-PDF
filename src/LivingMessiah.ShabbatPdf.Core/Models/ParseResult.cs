namespace LivingMessiah.ShabbatPdf.Core.Models;

/// <summary>
/// Outcome of a parse pipeline run.
/// </summary>
public sealed record ParseResult(
    bool Success,
    string Message,
    string? Markdown = null,
    AnchorResult? Anchors = null,
    string? DestinationUri = null);
