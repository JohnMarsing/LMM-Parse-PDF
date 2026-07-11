namespace LivingMessiah.ShabbatPdf.Core.Models;

/// <summary>
/// Outer anchors (Welcome / Avinu) and the final content page range after intro skip.
/// </summary>
public sealed record AnchorResult(
    int StartAnchorPage,
    int EndAnchorPage,
    int ProvisionalContentStartPage,
    int ContentStartPage,
    int ContentEndPage,
    string EndMatchMethod,
    IReadOnlyList<int> IntroSkippedPages);
