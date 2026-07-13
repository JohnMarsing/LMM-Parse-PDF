namespace LivingMessiah.ShabbatPdf.Functions;

/// <summary>
/// Decides which blobs the Azure Function should process.
/// Skips teaching-only outputs written back to the source container (avoids re-entry loops).
/// </summary>
public static class ShabbatBlobTriggerFilter
{
    /// <summary>
    /// Returns true when the blob looks like a full agenda PDF that should be parsed.
    /// </summary>
    public static bool ShouldProcess(string? blobName)
    {
        if (string.IsNullOrWhiteSpace(blobName))
        {
            return false;
        }

        // Blob trigger {name} may include virtual folder prefixes.
        var name = Path.GetFileName(blobName.Trim());
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (!name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Our pipeline uploads *-teaching.pdf to the same source container.
        if (name.EndsWith("-teaching.pdf", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }
}
