namespace LivingMessiah.ShabbatPdf.Core.Extraction;

/// <summary>
/// Thrown when outer anchors are missing or the content slice is empty.
/// </summary>
public sealed class AnchorException : Exception
{
    public string Code { get; }

    public AnchorException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public static AnchorException StartNotFound() =>
        new("AnchorNotFound:Start", "Could not find a page with full-line Welcome followed by Bienvenido/Bienvenidos.");

    public static AnchorException EndNotFound() =>
        new("AnchorNotFound:End", "Could not find a page matching The Avinu Prayer after the start anchor.");

    public static AnchorException EmptySlice(string detail) =>
        new("EmptySlice", detail);
}
