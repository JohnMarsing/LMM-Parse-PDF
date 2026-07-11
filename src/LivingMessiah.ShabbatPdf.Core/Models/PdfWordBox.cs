namespace LivingMessiah.ShabbatPdf.Core.Models;

/// <summary>
/// A single word from the PDF text layer with geometry in PDF user space
/// (Y increases upward).
/// </summary>
public sealed record PdfWordBox(
    string Text,
    double Left,
    double Right,
    double Bottom,
    double Top)
{
    public double MidY => (Bottom + Top) / 2.0;

    public double MidX => (Left + Right) / 2.0;
}
