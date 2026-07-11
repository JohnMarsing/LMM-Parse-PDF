namespace LivingMessiah.ShabbatPdf.Core.Models;

/// <summary>
/// Input for a single parse pipeline run (local file, blob name, or provided stream).
/// </summary>
public sealed record ParseRequest(
    string SourceName,
    Stream? PdfStream = null,
    string? LocalInputPath = null,
    string? LocalOutputPath = null,
    bool Overwrite = true,
    bool SkipIfDestinationExists = false,
    bool DryRun = false,
    bool RequireStandardBlobName = true);
