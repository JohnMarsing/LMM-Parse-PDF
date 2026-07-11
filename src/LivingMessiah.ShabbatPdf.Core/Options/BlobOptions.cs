namespace LivingMessiah.ShabbatPdf.Core.Options;

/// <summary>
/// Azure Blob Storage settings. Binds from configuration section "Blob".
/// </summary>
public sealed class BlobOptions
{
    public const string SectionName = "Blob";

    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Account URI for Managed Identity (e.g. https://livingmessiahstorage.blob.core.windows.net).
    /// </summary>
    public string ServiceUri { get; set; } = string.Empty;

    public string SourceContainer { get; set; } = "shabbat-service";

    public string DestinationContainer { get; set; } = "shabbat-service-md";

    public bool UseDefaultAzureCredential { get; set; }
}
