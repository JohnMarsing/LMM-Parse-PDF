namespace LivingMessiah.ShabbatPdf.Core.Pipeline;

/// <summary>
/// Stable error codes returned in <see cref="Models.ParseResult.Message"/> prefixes.
/// </summary>
public static class ParseErrorCodes
{
    public const string SourceNotFound = "SourceNotFound";
    public const string InvalidName = "InvalidName";
    public const string UploadFailed = "UploadFailed";
    public const string ContainerNotFound = "ContainerNotFound";
    public const string IoError = "IoError";
    public const string Unexpected = "Unexpected";
}
