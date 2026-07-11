namespace LivingMessiah.ShabbatPdf.Core.Options;

/// <summary>
/// Options for full-page midY word → line clustering.
/// </summary>
public sealed class LineClusterOptions
{
    /// <summary>
    /// Max |midY − clusterMeanMidY| to join a word into an existing line cluster.
    /// </summary>
    public double YTolerance { get; set; } = 3.0;
}
