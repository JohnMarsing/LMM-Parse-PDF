namespace LivingMessiah.ShabbatPdf.Core.Options;

/// <summary>
/// Anchor phrases, intro-skip patterns, and parse pipeline defaults.
/// Binds from configuration section "Parse".
/// </summary>
public sealed class ParseOptions
{
    public const string SectionName = "Parse";

    public string StartWelcomeLine { get; set; } = "Welcome";

    public List<string> StartBienvenidoLines { get; set; } =
    [
        "Bienvenido",
        "Bienvenidos"
    ];

    public string EndAvinuPhrase { get; set; } = "The Avinu Prayer";

    public bool SkipIntroPages { get; set; } = true;

    public List<string> IntroSkipLineContains { get; set; } =
    [
        "what will we talk about today",
        "talk about today",
        "fair use",
        "legal disclaimer",
        "unless noted otherwise all text in english",
        "section 107"
    ];

    /// <summary>
    /// Line clustering tolerance (also available under <see cref="LineClusterOptions"/>).
    /// </summary>
    public double YTolerance { get; set; } = 3.0;

    public bool Overwrite { get; set; } = true;

    /// <summary>
    /// When true, --blob mode requires YYYY-MM-DD-… filenames.
    /// </summary>
    public bool RequireStandardBlobName { get; set; } = true;

    public LineClusterOptions ToLineClusterOptions() =>
        new() { YTolerance = YTolerance };
}
